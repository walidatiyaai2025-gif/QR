using System.Collections.Concurrent;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class FirebaseDurableClosureTests
{
    [Fact]
    public async Task Missing_firebase_credential_file_fails_closed()
    {
        var provider = new FirebaseAdminPushProvider(
            Options.Create(new FirebasePushOptions
            {
                ProjectId = "test-project",
                CredentialPath = "missing-service-account.json"
            }),
            new TestEnvironment(),
            NullLogger<FirebaseAdminPushProvider>.Instance);

        var result = await provider.SendAsync(
            "non-empty-token",
            new FirebasePushEnvelope(1, MobilePushConstants.InitialCategory));

        Assert.False(result.Accepted);
        Assert.Equal(FirebasePushOutcome.CredentialFailure, result.Outcome);
        Assert.Equal("PROVIDER_UNAVAILABLE", result.ProviderStatus);
        Assert.Equal("CREDENTIAL_FILE_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task Disabled_and_deactivated_devices_are_not_selected()
    {
        await using var f = await Fixture.CreateAsync();
        var disabled = await f.SeedDeliveryAsync(pushEnabled: false);
        f.Db.ChangeTracker.Clear();
        var disabledResult = await f.Dispatch.DispatchAsync(new MobilePushDispatchRequest(disabled.Id));
        Assert.Equal("NO_REGISTERED_DEVICE", disabledResult.ErrorCode);
        Assert.Empty(f.Provider.Calls);

        await f.ResetDomainAsync();
        var deactivated = await f.SeedDeliveryAsync(deactivated: true);
        f.Db.ChangeTracker.Clear();
        var deactivatedResult = await f.Dispatch.DispatchAsync(new MobilePushDispatchRequest(deactivated.Id));
        Assert.Equal("NO_REGISTERED_DEVICE", deactivatedResult.ErrorCode);
        Assert.Empty(f.Provider.Calls);
    }

    [Fact]
    public async Task Cross_organization_device_is_never_selected()
    {
        await using var f = await Fixture.CreateAsync();
        var delivery = await f.SeedDeliveryAsync(addDevice: false);
        var other = new Organization
        {
            NameArabic = "جهة أخرى",
            NameEnglish = "Other Org",
            MobileNumber = "96551112222",
            IsActive = true
        };
        f.Db.Organizations.Add(other);
        await f.Db.SaveChangesAsync();
        await f.AddDeviceAsync(other.Id, "other-device", "other-fcm-token");
        f.Db.ChangeTracker.Clear();

        var result = await f.Dispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));

        Assert.Equal("NO_REGISTERED_DEVICE", result.ErrorCode);
        Assert.Empty(f.Provider.Calls);
    }

    [Fact]
    public void Push_envelope_contains_only_safe_routing_fields()
    {
        var properties = typeof(FirebasePushEnvelope).GetProperties().Select(x => x.Name).ToArray();
        Assert.Equal(new[] { "DeliveryId", "Category", "Version" }, properties);
        Assert.DoesNotContain(properties, x => x.Contains("Body", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, x => x.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, x => x.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("لديك رسالة جديدة اضغط هنا لاستعراض الرسالة", MobilePushConstants.ArabicBody);
        Assert.Equal("You have a new message. Tap here to view it.", MobilePushConstants.EnglishBody);
    }

    [Fact]
    public async Task Accepted_initial_push_is_idempotent_for_same_delivery_and_device()
    {
        await using var f = await Fixture.CreateAsync();
        var delivery = await f.SeedDeliveryAsync();
        f.Db.ChangeTracker.Clear();

        var first = await f.Dispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));
        var second = await f.Dispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));

        Assert.True(first.ProviderAccepted);
        Assert.True(second.ProviderAccepted);
        Assert.Single(f.Provider.Calls);
        var attempt = await f.Db.MobilePushAttempts.AsNoTracking().SingleAsync();
        Assert.Equal("INITIAL", attempt.Kind);
        Assert.Equal("PROVIDER_ACCEPTED", attempt.Outcome);
    }

    [Fact]
    public async Task Invalid_token_is_retired_once_without_raw_token_audit()
    {
        await using var f = await Fixture.CreateAsync();
        const string rawToken = "raw-fcm-token-never-audit";
        var delivery = await f.SeedDeliveryAsync(fcmToken: rawToken);
        f.Provider.Results.Enqueue(new FirebasePushProviderResult(
            FirebasePushOutcome.InvalidToken,
            "INVALID_TOKEN",
            ErrorCode: "UNREGISTERED",
            PermanentFailure: true));
        f.Db.ChangeTracker.Clear();

        var result = await f.Dispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));

        Assert.Equal("INVALID_TOKEN", result.ErrorCode);
        Assert.Single(f.Provider.Calls);
        f.Db.ChangeTracker.Clear();
        var device = await f.Db.MobileDevices.AsNoTracking().SingleAsync();
        Assert.False(device.PushEnabled);
        Assert.NotNull(device.DeactivatedAtUtc);
        Assert.Equal(string.Empty, device.FcmTokenProtected);
        var auditText = string.Join('|', await f.Db.AuditLogs.AsNoTracking().Select(x => x.Details).ToListAsync());
        Assert.DoesNotContain(rawToken, auditText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transient_provider_failure_uses_bounded_retry_and_persists_attempts()
    {
        await using var f = await Fixture.CreateAsync(maxTransientRetries: 2);
        var delivery = await f.SeedDeliveryAsync();
        f.Provider.Results.Enqueue(new FirebasePushProviderResult(
            FirebasePushOutcome.ProviderUnavailable, "PROVIDER_UNAVAILABLE", ErrorCode: "UNAVAILABLE"));
        f.Provider.Results.Enqueue(new FirebasePushProviderResult(
            FirebasePushOutcome.ProviderUnavailable, "PROVIDER_UNAVAILABLE", ErrorCode: "INTERNAL"));
        f.Provider.Results.Enqueue(new FirebasePushProviderResult(
            FirebasePushOutcome.Accepted, "PROVIDER_ACCEPTED", "message-3"));
        f.Db.ChangeTracker.Clear();

        var result = await f.Dispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));

        Assert.True(result.ProviderAccepted);
        Assert.Equal(3, f.Provider.Calls.Count);
        Assert.Equal(3, await f.Db.MobilePushAttempts.AsNoTracking().CountAsync());
        Assert.Equal(
            new[] { 0, 1, 2 },
            await f.Db.MobilePushAttempts.AsNoTracking()
                .OrderBy(x => x.RetryNumber)
                .Select(x => x.RetryNumber)
                .ToArrayAsync());
    }

    [Fact]
    public async Task Due_unread_delivery_sends_one_reminder_and_schedules_next()
    {
        await using var f = await Fixture.CreateAsync();
        var delivery = await f.SeedDeliveryAsync(
            reminderEnabled: true,
            nextReminderAtUtc: f.Now.AddMinutes(-1));
        f.Db.ChangeTracker.Clear();

        var processed = await f.Processor.ProcessDueAsync();

        Assert.Equal(1, processed);
        Assert.Single(f.Provider.Calls);
        f.Db.ChangeTracker.Clear();
        var stored = await f.Db.MobileDeliveries.AsNoTracking().SingleAsync(x => x.Id == delivery.Id);
        Assert.Equal(1, stored.ReminderCount);
        Assert.Equal(1, stored.ReminderSequence);
        Assert.NotNull(stored.LastReminderAtUtc);
        Assert.True(stored.NextReminderAtUtc > f.Now);
        Assert.Null(stored.ProcessingLeaseId);
        Assert.Null(stored.ProcessingLeaseUntilUtc);
        Assert.Contains(
            await f.Db.MobilePushAttempts.AsNoTracking().ToListAsync(),
            x => x.Kind == "REMINDER" && x.Sequence == 1);
    }

    [Theory]
    [InlineData("future")]
    [InlineData("disabled")]
    public async Task Non_due_or_disabled_reminder_does_not_send(string scenario)
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedDeliveryAsync(
            reminderEnabled: scenario != "disabled",
            nextReminderAtUtc: f.Now.AddMinutes(scenario == "future" ? 10 : -1));
        f.Db.ChangeTracker.Clear();

        var processed = await f.Processor.ProcessDueAsync();

        Assert.Equal(0, processed);
        Assert.Empty(f.Provider.Calls);
    }

    [Theory]
    [InlineData("revealed")]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("organization-disabled")]
    [InlineData("page-disabled")]
    [InlineData("page-revoked")]
    public async Task Stop_conditions_clear_future_reminder_without_send(string scenario)
    {
        await using var f = await Fixture.CreateAsync();
        var delivery = await f.SeedDeliveryAsync(
            reminderEnabled: true,
            nextReminderAtUtc: f.Now.AddMinutes(-1),
            firstRevealedAtUtc: scenario == "revealed" ? f.Now.AddMinutes(-2) : null,
            revokedAtUtc: scenario == "revoked" ? f.Now.AddMinutes(-2) : null,
            expiresAtUtc: scenario == "expired" ? f.Now.AddSeconds(-1) : f.Now.AddHours(1),
            organizationActive: scenario != "organization-disabled",
            pageActive: scenario != "page-disabled",
            pageRevoked: scenario == "page-revoked");
        f.Db.ChangeTracker.Clear();

        var processed = await f.Processor.ProcessDueAsync();

        Assert.Equal(1, processed);
        Assert.Empty(f.Provider.Calls);
        f.Db.ChangeTracker.Clear();
        var stored = await f.Db.MobileDeliveries.AsNoTracking().SingleAsync(x => x.Id == delivery.Id);
        Assert.Null(stored.NextReminderAtUtc);
        Assert.Contains(
            await f.Db.AuditLogs.AsNoTracking().ToListAsync(),
            x => x.Action == "MOBILE_REMINDER_STOPPED");
    }

    [Fact]
    public async Task Authoritative_secure_reveal_clears_schedule_and_audits_stop()
    {
        await using var f = await Fixture.CreateAsync();
        var delivery = await f.SeedDeliveryAsync(
            reminderEnabled: true,
            nextReminderAtUtc: f.Now.AddMinutes(15));
        var organization = await f.Db.Organizations.SingleAsync(x => x.Id == delivery.OrganizationId);
        var mobileTokens = new MobileTokenService();
        var sessions = new MobileSessionService(f.Db, mobileTokens, f.Clock);
        var issued = await sessions.IssueAsync(organization);
        var session = await f.Db.MobileSessions.SingleAsync(x => x.SessionId == issued.SessionId);
        var revealToken = mobileTokens.GenerateToken();
        f.Db.MobileRevealGrants.Add(new MobileRevealGrant
        {
            TokenHash = mobileTokens.HashToken(revealToken),
            MobileSessionId = session.Id,
            MobileDeliveryId = delivery.Id,
            CreatedAtUtc = f.Now,
            ExpiresAtUtc = f.Now.AddMinutes(2)
        });
        await f.Db.SaveChangesAsync();
        var organizationId = organization.Id;
        var sessionId = session.Id;
        f.Db.ChangeTracker.Clear();
        var http = new DefaultHttpContext();
        var access = new SecurePageAccessService(f.Db, null!, f.QrStatus, new DeviceInfoService());
        var reveal = new MobileDeliveryAccessService(
            f.Db,
            access,
            f.QrStatus,
            mobileTokens,
            f.Audit,
            f.Clock);

        var result = await reveal.RevealAsync(
            organizationId,
            sessionId,
            delivery.Id,
            revealToken,
            http);

        Assert.Equal(MobileDeliveryAccessStatus.Success, result.Status);
        f.Db.ChangeTracker.Clear();
        var stored = await f.Db.MobileDeliveries.AsNoTracking().SingleAsync(x => x.Id == delivery.Id);
        Assert.NotNull(stored.FirstRevealedAtUtc);
        Assert.Null(stored.NextReminderAtUtc);
        Assert.Contains(
            await f.Db.AuditLogs.AsNoTracking().ToListAsync(),
            x => x.Action == "MOBILE_REMINDER_STOPPED" && x.Details == "Reason=FIRST_SECURE_REVEAL");
    }

    [Fact]
    public async Task Restart_recreates_context_and_due_schedule_remains_executable()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"da-secure-reminder-restart-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path};Default Timeout=30;Pooling=False";
        var protection = new EphemeralDataProtectionProvider();
        var provider = new FakeProvider();
        var clock = new TestClock(new DateTimeOffset(2026, 8, 25, 20, 0, 0, TimeSpan.Zero));
        try
        {
            await using (var db = CreateFileDb(connectionString))
            {
                await db.Database.EnsureCreatedAsync();
                await SeedFileDeliveryAsync(db, protection, clock.GetUtcNow().UtcDateTime);
            }

            await using (var restartedDb = CreateFileDb(connectionString))
            {
                var processor = BuildProcessor(restartedDb, provider, protection, clock);
                var processed = await processor.ProcessDueAsync();

                Assert.Equal(1, processed);
                Assert.Single(provider.Calls);
                restartedDb.ChangeTracker.Clear();
                Assert.Equal(
                    1,
                    (await restartedDb.MobileDeliveries.AsNoTracking().SingleAsync()).ReminderCount);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Concurrent_processors_claim_due_occurrence_once()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"da-secure-reminder-concurrency-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path};Default Timeout=30;Pooling=False";
        var protection = new EphemeralDataProtectionProvider();
        var provider = new FakeProvider();
        var clock = new TestClock(new DateTimeOffset(2026, 8, 25, 20, 0, 0, TimeSpan.Zero));
        try
        {
            await using (var db = CreateFileDb(connectionString))
            {
                await db.Database.EnsureCreatedAsync();
                await SeedFileDeliveryAsync(db, protection, clock.GetUtcNow().UtcDateTime);
            }

            await using (var db1 = CreateFileDb(connectionString))
            await using (var db2 = CreateFileDb(connectionString))
            {
                var p1 = BuildProcessor(db1, provider, protection, clock);
                var p2 = BuildProcessor(db2, provider, protection, clock);
                await Task.WhenAll(p1.ProcessDueAsync(), p2.ProcessDueAsync());
            }

            Assert.Single(provider.Calls);
            await using var verify = CreateFileDb(connectionString);
            var stored = await verify.MobileDeliveries.AsNoTracking().SingleAsync();
            Assert.Equal(1, stored.ReminderCount);
            Assert.Equal(1, stored.ReminderSequence);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static ApplicationDbContext CreateFileDb(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .Options);

    private static async Task SeedFileDeliveryAsync(
        ApplicationDbContext db,
        IDataProtectionProvider protection,
        DateTime now)
    {
        var org = new Organization
        {
            NameArabic = "جهة",
            NameEnglish = "Org",
            MobileNumber = "96550009999",
            IsActive = true
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        var page = NewPage(org, now);
        db.SecurePages.Add(page);
        await db.SaveChangesAsync();
        var tokens = new MobileTokenService();
        var secrets = new MobileSecretProtector(protection);
        db.MobileDevices.Add(new MobileDevice
        {
            OrganizationId = org.Id,
            DeviceId = "restart-device",
            FcmTokenProtected = secrets.ProtectFcmToken("restart-token"),
            FcmTokenHash = tokens.HashToken("restart-token"),
            Platform = "android",
            AppVersion = "0.1.0",
            PushEnabled = true,
            RegisteredAtUtc = now.AddHours(-1),
            LastSeenAtUtc = now
        });
        db.MobileDeliveries.Add(new MobileDelivery
        {
            OrganizationId = org.Id,
            SecurePageId = page.Id,
            CreatedAtUtc = now.AddMinutes(-5),
            SentAtUtc = now.AddMinutes(-5),
            DeliveryStatus = "PROVIDER_ACCEPTED",
            FirebaseStatus = "PROVIDER_ACCEPTED",
            ExpiresAtUtc = now.AddHours(1),
            ReminderEnabled = true,
            ReminderInterval = 10,
            ReminderUnit = "Minutes",
            NextReminderAtUtc = now.AddMinutes(-1)
        });
        await db.SaveChangesAsync();
    }

    private static MobileReminderProcessor BuildProcessor(
        ApplicationDbContext db,
        FakeProvider provider,
        IDataProtectionProvider protection,
        TimeProvider clock)
    {
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var audit = new AuditService(db, http);
        var tokens = new MobileTokenService();
        var options = Options.Create(new FirebasePushOptions
        {
            MaxTransientRetries = 0,
            RetryBaseMilliseconds = 100,
            LeaseSeconds = 120
        });
        var devices = new MobilePushDeviceStore(
            db,
            new MobileSecretProtector(protection),
            tokens,
            audit,
            clock);
        var attempts = new MobilePushAttemptService(db, provider, devices, options, clock);
        return new MobileReminderProcessor(
            db,
            new QrStatusService(clock),
            devices,
            attempts,
            audit,
            options,
            clock);
    }

    private static SecurePage NewPage(
        Organization org,
        DateTime now,
        bool isActive = true,
        bool revoked = false) => new()
    {
        OrganizationId = org.Id,
        Organization = org,
        QrReference = "QR-2026-FCM-" + Guid.NewGuid().ToString("N")[..10],
        PublicTokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        ProtectedPublicToken = "protected-test-token",
        TitleArabic = "رسالة",
        TitleEnglish = "Message",
        ContentArabicHtml = "<p>محتوى آمن</p>",
        ContentEnglishHtml = "<p>Secure content</p>",
        IsActive = isActive,
        ValidFromUtc = now.AddHours(-1),
        ExpiresAtUtc = now.AddHours(2),
        RevokedAtUtc = revoked ? now.AddMinutes(-1) : null,
        AccessLimitMode = AccessLimitMode.MaximumSuccessfulAccesses,
        MaxAccessCount = 5
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private int seedCounter;

        private Fixture(
            SqliteConnection connection,
            ApplicationDbContext db,
            TestClock clock,
            IDataProtectionProvider protection,
            FakeProvider provider,
            IOptions<FirebasePushOptions> options)
        {
            this.connection = connection;
            Db = db;
            Clock = clock;
            Provider = provider;
            Tokens = new MobileTokenService();
            Secrets = new MobileSecretProtector(protection);
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            Audit = new AuditService(Db, http);
            QrStatus = new QrStatusService(Clock);
            Devices = new MobilePushDeviceStore(Db, Secrets, Tokens, Audit, Clock);
            Attempts = new MobilePushAttemptService(Db, Provider, Devices, options, Clock);
            Dispatch = new FirebaseMobilePushDispatchService(Db, QrStatus, Devices, Attempts, Clock);
            Processor = new MobileReminderProcessor(Db, QrStatus, Devices, Attempts, Audit, options, Clock);
        }

        public ApplicationDbContext Db { get; }
        public TestClock Clock { get; }
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;
        public FakeProvider Provider { get; }
        public MobileTokenService Tokens { get; }
        public MobileSecretProtector Secrets { get; }
        public AuditService Audit { get; }
        public QrStatusService QrStatus { get; }
        public MobilePushDeviceStore Devices { get; }
        public MobilePushAttemptService Attempts { get; }
        public FirebaseMobilePushDispatchService Dispatch { get; }
        public MobileReminderProcessor Processor { get; }

        public static async Task<Fixture> CreateAsync(int maxTransientRetries = 0)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync();
            var clock = new TestClock(
                new DateTimeOffset(2026, 8, 25, 20, 0, 0, TimeSpan.Zero));
            var protection = new EphemeralDataProtectionProvider();
            var provider = new FakeProvider();
            var options = Options.Create(new FirebasePushOptions
            {
                MaxTransientRetries = maxTransientRetries,
                RetryBaseMilliseconds = 100,
                LeaseSeconds = 120
            });
            return new Fixture(connection, db, clock, protection, provider, options);
        }

        public async Task<MobileDelivery> SeedDeliveryAsync(
            bool addDevice = true,
            bool pushEnabled = true,
            bool deactivated = false,
            bool reminderEnabled = false,
            DateTime? nextReminderAtUtc = null,
            DateTime? firstRevealedAtUtc = null,
            DateTime? revokedAtUtc = null,
            DateTime? expiresAtUtc = null,
            bool organizationActive = true,
            bool pageActive = true,
            bool pageRevoked = false,
            string fcmToken = "test-fcm-token")
        {
            seedCounter++;
            var org = new Organization
            {
                NameArabic = $"جهة {seedCounter}",
                NameEnglish = $"Org {seedCounter}",
                MobileNumber = $"9655{seedCounter:D7}",
                IsActive = organizationActive
            };
            Db.Organizations.Add(org);
            await Db.SaveChangesAsync();
            var page = NewPage(org, Now, pageActive, pageRevoked);
            Db.SecurePages.Add(page);
            await Db.SaveChangesAsync();
            if (addDevice)
            {
                await AddDeviceAsync(
                    org.Id,
                    $"device-{seedCounter}",
                    fcmToken,
                    pushEnabled,
                    deactivated);
            }

            var delivery = new MobileDelivery
            {
                OrganizationId = org.Id,
                SecurePageId = page.Id,
                CreatedAtUtc = Now.AddMinutes(-5),
                SentAtUtc = Now.AddMinutes(-5),
                DeliveryStatus = "PROVIDER_ACCEPTED",
                FirebaseStatus = "PROVIDER_ACCEPTED",
                ExpiresAtUtc = expiresAtUtc ?? Now.AddHours(1),
                FirstRevealedAtUtc = firstRevealedAtUtc,
                RevokedAtUtc = revokedAtUtc,
                ReminderEnabled = reminderEnabled,
                ReminderInterval = reminderEnabled ? 10 : null,
                ReminderUnit = reminderEnabled ? "Minutes" : null,
                NextReminderAtUtc = nextReminderAtUtc
            };
            Db.MobileDeliveries.Add(delivery);
            await Db.SaveChangesAsync();
            return delivery;
        }

        public async Task AddDeviceAsync(
            long organizationId,
            string deviceId,
            string token,
            bool pushEnabled = true,
            bool deactivated = false)
        {
            Db.MobileDevices.Add(new MobileDevice
            {
                OrganizationId = organizationId,
                DeviceId = deviceId,
                FcmTokenProtected = Secrets.ProtectFcmToken(token),
                FcmTokenHash = Tokens.HashToken(token),
                Platform = "android",
                AppVersion = "0.1.0",
                PushEnabled = pushEnabled,
                RegisteredAtUtc = Now.AddHours(-1),
                LastSeenAtUtc = Now,
                DeactivatedAtUtc = deactivated ? Now.AddMinutes(-1) : null
            });
            await Db.SaveChangesAsync();
        }

        public async Task ResetDomainAsync()
        {
            Db.ChangeTracker.Clear();
            Db.MobilePushAttempts.RemoveRange(Db.MobilePushAttempts);
            Db.MobileDeliveries.RemoveRange(Db.MobileDeliveries);
            Db.MobileDevices.RemoveRange(Db.MobileDevices);
            Db.SecurePages.RemoveRange(Db.SecurePages);
            Db.Organizations.RemoveRange(Db.Organizations);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            Provider.Calls.Clear();
            while (Provider.Results.TryDequeue(out _)) { }
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FakeProvider : IFirebasePushProvider
    {
        public ConcurrentQueue<FirebasePushProviderResult> Results { get; } = new();
        public List<(string Token, FirebasePushEnvelope Envelope)> Calls { get; } = [];
        private readonly object gate = new();

        public Task<FirebasePushHealth> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new FirebasePushHealth("READY", "TEST"));

        public Task<FirebasePushProviderResult> SendAsync(
            string fcmToken,
            FirebasePushEnvelope envelope,
            CancellationToken ct = default)
        {
            int count;
            lock (gate)
            {
                Calls.Add((fcmToken, envelope));
                count = Calls.Count;
            }
            if (Results.TryDequeue(out var result)) return Task.FromResult(result);
            return Task.FromResult(new FirebasePushProviderResult(
                FirebasePushOutcome.Accepted,
                "PROVIDER_ACCEPTED",
                $"message-{count}"));
        }
    }

    private sealed class TestClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset current = initial;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan value) => current = current.Add(value);
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SecureQrPortal.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
