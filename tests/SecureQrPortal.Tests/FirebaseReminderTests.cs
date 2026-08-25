using FirebaseAdmin.Messaging;
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

public sealed class FirebaseReminderTests
{
    [Fact]
    public void Firebase_message_contains_only_safe_routing_metadata_and_fixed_copy()
    {
        var message = FirebaseAdminPushProvider.BuildMessage(
            "test-fcm-registration-token",
            new FirebasePushEnvelope(42, MobilePushConstants.InitialCategory));

        Assert.Null(message.Notification.Title);
        Assert.Equal("لديك رسالة جديدة اضغط هنا لاستعراض الرسالة", message.Notification.Body);
        Assert.Equal("You have a new message. Tap here to view it.", MobilePushConstants.EnglishBody);
        Assert.Equal(new[] { "category", "deliveryId", "version" }, message.Data.Keys.OrderBy(x => x).ToArray());
        Assert.Equal("42", message.Data["deliveryId"]);
        Assert.Equal("delivery", message.Data["category"]);
        Assert.Equal("1", message.Data["version"]);

        var serialized = string.Join("|", message.Data.Select(x => $"{x.Key}={x.Value}"));
        Assert.DoesNotContain("password", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("otp", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("content", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attachment", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qrToken", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_explicit_firebase_credential_path_fails_closed_without_throwing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"da-secure-missing-{Guid.NewGuid():N}.json");
        var provider = new FirebaseAdminPushProvider(
            Options.Create(new FirebasePushOptions { ProjectId = "daqr-a4a71", CredentialPath = missing }),
            new TestEnvironment(Path.GetTempPath()),
            NullLogger<FirebaseAdminPushProvider>.Instance);

        var health = await provider.CheckHealthAsync();
        var result = await provider.SendAsync("not-a-real-token", new FirebasePushEnvelope(1, MobilePushConstants.InitialCategory));

        Assert.Equal("CREDENTIAL_FAILURE", health.Status);
        Assert.Equal("CREDENTIAL_FILE_NOT_FOUND", health.DetailCode);
        Assert.False(result.Accepted);
        Assert.Equal(FirebasePushOutcome.CredentialFailure, result.Outcome);
        Assert.Equal("PROVIDER_UNAVAILABLE", result.ProviderStatus);
    }

    [Fact]
    public async Task Dispatch_uses_only_active_same_organization_devices()
    {
        await using var fixture = await Fixture.CreateAsync();
        var delivery = await fixture.SeedDeliveryAsync();
        await fixture.AddDeviceAsync(delivery.OrganizationId, "active", "token-active", pushEnabled: true);
        await fixture.AddDeviceAsync(delivery.OrganizationId, "disabled", "token-disabled", pushEnabled: false);
        await fixture.AddDeviceAsync(delivery.OrganizationId, "deactivated", "token-deactivated", pushEnabled: true, deactivated: true);
        var otherOrg = await fixture.AddOrganizationAsync("Other");
        await fixture.AddDeviceAsync(otherOrg.Id, "other", "token-other", pushEnabled: true);

        var result = await fixture.Dispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));

        Assert.True(result.ProviderAccepted);
        Assert.Equal(1, fixture.Provider.CallCount);
        Assert.Equal("token-active", fixture.Provider.Tokens.Single());
        Assert.Single(await fixture.Db.MobilePushAttempts.ToListAsync());
        Assert.DoesNotContain(await fixture.Db.MobilePushAttempts.Select(x => x.FcmTokenHash).ToListAsync(), x => x == "token-active");
    }

    [Fact]
    public async Task Dispatch_without_active_device_returns_no_registered_device()
    {
        await using var fixture = await Fixture.CreateAsync();
        var delivery = await fixture.SeedDeliveryAsync();
        await fixture.AddDeviceAsync(delivery.OrganizationId, "disabled", "token-disabled", pushEnabled: false);

        var result = await fixture.Dispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));

        Assert.False(result.ProviderAccepted);
        Assert.Equal("NO_REGISTERED_DEVICE", result.ProviderStatus);
        Assert.Equal(0, fixture.Provider.CallCount);
        Assert.Empty(await fixture.Db.MobilePushAttempts.ToListAsync());
    }

    [Fact]
    public async Task Provider_acceptance_and_message_id_are_persisted_in_attempt()
    {
        await using var fixture = await Fixture.CreateAsync(
            new FirebasePushProviderResult(FirebasePushOutcome.Accepted, "PROVIDER_ACCEPTED", "projects/test/messages/abc"));
        var delivery = await fixture.SeedDeliveryAsync();
        await fixture.AddDeviceAsync(delivery.OrganizationId, "device-1", "token-1");

        var result = await fixture.Dispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));
        var attempt = await fixture.Db.MobilePushAttempts.SingleAsync();

        Assert.True(result.ProviderAccepted);
        Assert.Equal("PROVIDER_ACCEPTED", attempt.Outcome);
        Assert.Equal("projects/test/messages/abc", attempt.ProviderMessageId);
        Assert.NotNull(attempt.CompletedAtUtc);
    }

    [Fact]
    public async Task Provider_failure_is_persisted_and_not_reported_as_opened()
    {
        await using var fixture = await Fixture.CreateAsync(
            new FirebasePushProviderResult(FirebasePushOutcome.Failed, "SEND_FAILED", ErrorCode: "INVALID_ARGUMENT", PermanentFailure: true));
        var delivery = await fixture.SeedDeliveryAsync();
        await fixture.AddDeviceAsync(delivery.OrganizationId, "device-1", "token-1");

        var result = await fixture.Dispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));
        var attempt = await fixture.Db.MobilePushAttempts.SingleAsync();

        Assert.False(result.ProviderAccepted);
        Assert.Equal("SEND_FAILED", attempt.Outcome);
        Assert.Equal("INVALID_ARGUMENT", attempt.ProviderErrorCode);
        Assert.True(attempt.PermanentFailure);
        Assert.Null(delivery.FirstRevealedAtUtc);
    }

    [Fact]
    public async Task Permanently_invalid_token_is_deactivated_and_not_retried()
    {
        await using var fixture = await Fixture.CreateAsync(
            new FirebasePushProviderResult(FirebasePushOutcome.InvalidToken, "INVALID_TOKEN", ErrorCode: "UNREGISTERED", PermanentFailure: true),
            maxTransientRetries: 3);
        var delivery = await fixture.SeedDeliveryAsync();
        var device = await fixture.AddDeviceAsync(delivery.OrganizationId, "device-1", "raw-sensitive-fcm-token");

        var result = await fixture.Dispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));
        fixture.Db.ChangeTracker.Clear();
        var storedDevice = await fixture.Db.MobileDevices.SingleAsync(x => x.Id == device.Id);
        var attempt = await fixture.Db.MobilePushAttempts.SingleAsync();

        Assert.False(result.ProviderAccepted);
        Assert.Equal("INVALID_TOKEN", result.ProviderStatus);
        Assert.Equal(1, fixture.Provider.CallCount);
        Assert.False(storedDevice.PushEnabled);
        Assert.NotNull(storedDevice.DeactivatedAtUtc);
        Assert.Equal(string.Empty, storedDevice.FcmTokenProtected);
        Assert.NotEqual(fixture.Tokens.HashToken("raw-sensitive-fcm-token"), storedDevice.FcmTokenHash);
        Assert.DoesNotContain("raw-sensitive-fcm-token", attempt.CorrelationKey, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-sensitive-fcm-token", attempt.FcmTokenHash, StringComparison.Ordinal);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "MOBILE_DEVICE_PUSH_DISABLED");
    }

    [Fact]
    public async Task Transient_provider_failure_retries_with_bound_and_persists_each_attempt()
    {
        await using var fixture = await Fixture.CreateAsync(
            maxTransientRetries: 2,
            results:
            [
                new(FirebasePushOutcome.ProviderUnavailable, "PROVIDER_UNAVAILABLE", ErrorCode: "UNAVAILABLE"),
                new(FirebasePushOutcome.Accepted, "PROVIDER_ACCEPTED", "message-after-retry")
            ]);
        var delivery = await fixture.SeedDeliveryAsync();
        await fixture.AddDeviceAsync(delivery.OrganizationId, "device-1", "token-1");

        var result = await fixture.Dispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));

        Assert.True(result.ProviderAccepted);
        Assert.Equal(2, fixture.Provider.CallCount);
        Assert.Equal(2, await fixture.Db.MobilePushAttempts.CountAsync());
        Assert.Equal(new[] { 0, 1 }, await fixture.Db.MobilePushAttempts.OrderBy(x => x.RetryNumber).Select(x => x.RetryNumber).ToArrayAsync());
    }

    [Fact]
    public async Task Repeating_same_initial_dispatch_reuses_persisted_attempt_instead_of_resending()
    {
        await using var fixture = await Fixture.CreateAsync();
        var delivery = await fixture.SeedDeliveryAsync();
        await fixture.AddDeviceAsync(delivery.OrganizationId, "device-1", "token-1");

        var first = await fixture.Dispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));
        var second = await fixture.Dispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));

        Assert.True(first.ProviderAccepted);
        Assert.True(second.ProviderAccepted);
        Assert.Equal(1, fixture.Provider.CallCount);
        Assert.Single(await fixture.Db.MobilePushAttempts.ToListAsync());
    }

    [Fact]
    public async Task Due_unread_delivery_sends_reminder_and_updates_schedule()
    {
        await using var fixture = await Fixture.CreateAsync();
        var delivery = await fixture.SeedDeliveryAsync(reminderDue: true, reminderInterval: 10);
        await fixture.AddDeviceAsync(delivery.OrganizationId, "device-1", "token-1");

        var processed = await fixture.Processor.ProcessDueAsync();
        fixture.Db.ChangeTracker.Clear();
        var stored = await fixture.Db.MobileDeliveries.SingleAsync(x => x.Id == delivery.Id);

        Assert.Equal(1, processed);
        Assert.Equal(1, fixture.Provider.CallCount);
        Assert.Equal(1, stored.ReminderCount);
        Assert.NotNull(stored.LastReminderAtUtc);
        Assert.Equal(fixture.Now.AddMinutes(10), stored.NextReminderAtUtc);
        Assert.Equal(1, stored.ReminderSequence);
        Assert.NotNull(stored.ReminderCycleCompletedAtUtc);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "MOBILE_REMINDER_SEND_ACCEPTED");
    }

    [Fact]
    public async Task Future_disabled_revealed_revoked_expired_source_revoked_and_org_disabled_do_not_send()
    {
        await AssertIneligibleDoesNotSendAsync((f, d) => d.NextReminderAtUtc = f.Now.AddMinutes(1));
        await AssertIneligibleDoesNotSendAsync((f, d) => d.ReminderEnabled = false);
        await AssertIneligibleDoesNotSendAsync((f, d) => d.FirstRevealedAtUtc = f.Now.AddMinutes(-1));
        await AssertIneligibleDoesNotSendAsync((f, d) => d.RevokedAtUtc = f.Now.AddMinutes(-1));
        await AssertIneligibleDoesNotSendAsync((f, d) => d.ExpiresAtUtc = f.Now.AddSeconds(-1));
        await AssertIneligibleDoesNotSendAsync((f, d) => d.SecurePage.RevokedAtUtc = f.Now.AddMinutes(-1));
        await AssertIneligibleDoesNotSendAsync((f, d) => d.Organization.IsActive = false);
    }

    [Fact]
    public async Task Invalid_token_during_reminder_does_not_loop_and_keeps_truthful_future_cycle()
    {
        await using var fixture = await Fixture.CreateAsync(
            new FirebasePushProviderResult(FirebasePushOutcome.InvalidToken, "INVALID_TOKEN", ErrorCode: "UNREGISTERED", PermanentFailure: true),
            maxTransientRetries: 4);
        var delivery = await fixture.SeedDeliveryAsync(reminderDue: true, reminderInterval: 5);
        await fixture.AddDeviceAsync(delivery.OrganizationId, "device-1", "token-1");

        await fixture.Processor.ProcessDueAsync();
        fixture.Db.ChangeTracker.Clear();
        var stored = await fixture.Db.MobileDeliveries.SingleAsync(x => x.Id == delivery.Id);

        Assert.Equal(1, fixture.Provider.CallCount);
        Assert.Equal(0, stored.ReminderCount);
        Assert.Equal("INVALID_TOKEN", stored.FirebaseStatus);
        Assert.Equal(fixture.Now.AddMinutes(5), stored.NextReminderAtUtc);
    }

    [Fact]
    public async Task First_reveal_state_stops_due_reminder_and_clears_future_schedule()
    {
        await using var fixture = await Fixture.CreateAsync();
        var delivery = await fixture.SeedDeliveryAsync(reminderDue: true);
        await fixture.AddDeviceAsync(delivery.OrganizationId, "device-1", "token-1");
        delivery.FirstRevealedAtUtc = fixture.Now.AddSeconds(-1);
        await fixture.Db.SaveChangesAsync();

        await fixture.Processor.ProcessDueAsync();
        fixture.Db.ChangeTracker.Clear();
        var stored = await fixture.Db.MobileDeliveries.SingleAsync(x => x.Id == delivery.Id);

        Assert.Equal(0, fixture.Provider.CallCount);
        Assert.Null(stored.NextReminderAtUtc);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x =>
            x.Action == "MOBILE_REMINDER_STOPPED" && x.Details != null && x.Details.Contains("FIRST_SECURE_REVEAL"));
    }

    [Fact]
    public async Task Push_routing_metadata_alone_does_not_stop_reminders()
    {
        await using var fixture = await Fixture.CreateAsync();
        var delivery = await fixture.SeedDeliveryAsync(reminderDue: false, reminderInterval: 15);
        var before = delivery.NextReminderAtUtc;
        _ = FirebaseAdminPushProvider.BuildMessage("token", new FirebasePushEnvelope(delivery.Id, MobilePushConstants.InitialCategory));
        fixture.Db.ChangeTracker.Clear();
        var stored = await fixture.Db.MobileDeliveries.SingleAsync(x => x.Id == delivery.Id);
        Assert.Null(stored.FirstRevealedAtUtc);
        Assert.Equal(before, stored.NextReminderAtUtc);
    }

    [Fact]
    public async Task Two_concurrent_processors_do_not_double_send_same_reminder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"da-secure-reminder-concurrency-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"da-secure-reminder-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keys);
        try
        {
            var provider = new RecordingProvider(delay: TimeSpan.FromMilliseconds(150));
            var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
            long deliveryId;
            await using (var seed = await FileFixture.CreateAsync(path, keys, provider, clock))
            {
                var delivery = await seed.SeedDeliveryAsync(reminderDue: true);
                await seed.AddDeviceAsync(delivery.OrganizationId, "device-1", "token-1");
                deliveryId = delivery.Id;
            }

            await using var first = await FileFixture.CreateAsync(path, keys, provider, clock, migrate: false);
            await using var second = await FileFixture.CreateAsync(path, keys, provider, clock, migrate: false);
            await Task.WhenAll(first.Processor.ProcessDueAsync(), second.Processor.ProcessDueAsync());

            Assert.Equal(1, provider.CallCount);
            await using var verify = await FileFixture.CreateAsync(path, keys, provider, clock, migrate: false);
            var stored = await verify.Db.MobileDeliveries.SingleAsync(x => x.Id == deliveryId);
            Assert.Equal(1, stored.ReminderCount);
            Assert.Equal(1, await verify.Db.MobilePushAttempts.CountAsync(x => x.MobileDeliveryId == deliveryId && x.Kind == "REMINDER"));
        }
        finally
        {
            TryDelete(path);
            TryDeleteDirectory(keys);
        }
    }

    [Fact]
    public async Task Persisted_due_schedule_survives_service_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"da-secure-reminder-restart-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"da-secure-reminder-restart-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keys);
        var provider = new RecordingProvider();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
        long deliveryId;
        try
        {
            await using (var beforeRestart = await FileFixture.CreateAsync(path, keys, provider, clock))
            {
                var delivery = await beforeRestart.SeedDeliveryAsync(reminderDue: true);
                await beforeRestart.AddDeviceAsync(delivery.OrganizationId, "device-1", "token-1");
                deliveryId = delivery.Id;
            }

            Assert.Equal(0, provider.CallCount);
            await using (var afterRestart = await FileFixture.CreateAsync(path, keys, provider, clock, migrate: false))
            {
                Assert.Equal(1, await afterRestart.Processor.ProcessDueAsync());
                var stored = await afterRestart.Db.MobileDeliveries.SingleAsync(x => x.Id == deliveryId);
                Assert.Equal(1, stored.ReminderCount);
            }
            Assert.Equal(1, provider.CallCount);
        }
        finally
        {
            TryDelete(path);
            TryDeleteDirectory(keys);
        }
    }

    private static async Task AssertIneligibleDoesNotSendAsync(Action<Fixture, MobileDelivery> mutate)
    {
        await using var fixture = await Fixture.CreateAsync();
        var delivery = await fixture.SeedDeliveryAsync(reminderDue: true);
        await fixture.AddDeviceAsync(delivery.OrganizationId, "device-1", "token-1");
        mutate(fixture, delivery);
        await fixture.Db.SaveChangesAsync();
        await fixture.Processor.ProcessDueAsync();
        Assert.Equal(0, fixture.Provider.CallCount);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly string keyDirectory;
        private int seed;

        public ApplicationDbContext Db { get; }
        public MutableTimeProvider Clock { get; }
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;
        public MobileTokenService Tokens { get; }
        public MobileSecretProtector Secrets { get; }
        public RecordingProvider Provider { get; }
        public FirebaseMobilePushDispatchService Dispatch { get; }
        public MobileReminderProcessor Processor { get; }

        private Fixture(
            SqliteConnection connection,
            string keyDirectory,
            ApplicationDbContext db,
            MutableTimeProvider clock,
            MobileTokenService tokens,
            MobileSecretProtector secrets,
            RecordingProvider provider,
            FirebaseMobilePushDispatchService dispatch,
            MobileReminderProcessor processor)
        {
            this.connection = connection;
            this.keyDirectory = keyDirectory;
            Db = db;
            Clock = clock;
            Tokens = tokens;
            Secrets = secrets;
            Provider = provider;
            Dispatch = dispatch;
            Processor = processor;
        }

        public static async Task<Fixture> CreateAsync(
            FirebasePushProviderResult? result = null,
            int maxTransientRetries = 2,
            IReadOnlyList<FirebasePushProviderResult>? results = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var keys = Path.Combine(Path.GetTempPath(), $"da-secure-test-keys-{Guid.NewGuid():N}");
            Directory.CreateDirectory(keys);
            var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
            var provider = results is not null
                ? new RecordingProvider(results: results)
                : new RecordingProvider(result ?? new(FirebasePushOutcome.Accepted, "PROVIDER_ACCEPTED", "message-1"));
            var tokens = new MobileTokenService();
            var secrets = new MobileSecretProtector(DataProtectionProvider.Create(new DirectoryInfo(keys)));
            var audit = new AuditService(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var options = Options.Create(new FirebasePushOptions
            {
                MaxTransientRetries = maxTransientRetries,
                RetryBaseMilliseconds = 100,
                LeaseSeconds = 60,
                ReminderScanSeconds = 30
            });
            var deviceStore = new MobilePushDeviceStore(db, secrets, tokens, audit, clock);
            var attemptService = new MobilePushAttemptService(db, provider, deviceStore, options, clock);
            var qrStatus = new QrStatusService(clock);
            var dispatch = new FirebaseMobilePushDispatchService(db, qrStatus, deviceStore, attemptService, clock);
            var processor = new MobileReminderProcessor(db, qrStatus, deviceStore, attemptService, audit, options, clock);
            return new Fixture(connection, keys, db, clock, tokens, secrets, provider, dispatch, processor);
        }

        public Task<Organization> AddOrganizationAsync(string suffix) => AddOrganizationCoreAsync(Db, Now, suffix);

        public async Task<MobileDelivery> SeedDeliveryAsync(bool reminderDue = false, int reminderInterval = 10)
        {
            seed++;
            var org = await AddOrganizationAsync(seed.ToString());
            var page = new SecurePage
            {
                OrganizationId = org.Id,
                Organization = org,
                QrReference = $"QR-TEST-{seed:0000}",
                PublicTokenHash = Tokens.HashToken($"qr-token-{seed}"),
                ProtectedPublicToken = "protected-test-token",
                CurrentTokenCreatedAtUtc = Now,
                TitleArabic = "اختبار",
                TitleEnglish = "Test",
                ContentArabicHtml = "<p>secure</p>",
                ContentEnglishHtml = "<p>secure</p>",
                IsActive = true,
                AccessLimitMode = AccessLimitMode.MaximumSuccessfulAccesses,
                MaxAccessCount = 10,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now
            };
            Db.SecurePages.Add(page);
            await Db.SaveChangesAsync();
            var delivery = new MobileDelivery
            {
                OrganizationId = org.Id,
                Organization = org,
                SecurePageId = page.Id,
                SecurePage = page,
                CreatedAtUtc = Now.AddMinutes(-30),
                SentAtUtc = Now.AddMinutes(-20),
                DeliveryStatus = "PROVIDER_ACCEPTED",
                FirebaseStatus = "PROVIDER_ACCEPTED",
                ReminderEnabled = true,
                ReminderInterval = reminderInterval,
                ReminderUnit = "Minutes",
                NextReminderAtUtc = reminderDue ? Now.AddSeconds(-1) : Now.AddMinutes(15)
            };
            Db.MobileDeliveries.Add(delivery);
            await Db.SaveChangesAsync();
            return delivery;
        }

        public async Task<MobileDevice> AddDeviceAsync(long organizationId, string deviceId, string rawToken, bool pushEnabled = true, bool deactivated = false)
        {
            var device = new MobileDevice
            {
                OrganizationId = organizationId,
                DeviceId = deviceId,
                FcmTokenProtected = Secrets.ProtectFcmToken(rawToken),
                FcmTokenHash = Tokens.HashToken(rawToken),
                Platform = "android",
                AppVersion = "0.1.0",
                PushEnabled = pushEnabled,
                RegisteredAtUtc = Now.AddHours(-1),
                LastSeenAtUtc = Now,
                DeactivatedAtUtc = deactivated ? Now.AddMinutes(-5) : null
            };
            Db.MobileDevices.Add(device);
            await Db.SaveChangesAsync();
            return device;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
            TryDeleteDirectory(keyDirectory);
        }
    }

    private sealed class FileFixture : IAsyncDisposable
    {
        private readonly string keyDirectory;
        private readonly MobileTokenService tokens;
        private readonly MobileSecretProtector secrets;
        private int seed;
        public ApplicationDbContext Db { get; }
        public MobileReminderProcessor Processor { get; }
        public DateTime Now { get; }

        private FileFixture(string keyDirectory, ApplicationDbContext db, MobileReminderProcessor processor, MutableTimeProvider clock, MobileTokenService tokens, MobileSecretProtector secrets)
        {
            this.keyDirectory = keyDirectory;
            Db = db;
            Processor = processor;
            Now = clock.GetUtcNow().UtcDateTime;
            this.tokens = tokens;
            this.secrets = secrets;
        }

        public static async Task<FileFixture> CreateAsync(string path, string keyDirectory, RecordingProvider provider, MutableTimeProvider clock, bool migrate = true)
        {
            var cs = $"Data Source={path};Cache=Shared;Default Timeout=10";
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(cs).Options);
            if (migrate) await db.Database.EnsureCreatedAsync();
            var tokens = new MobileTokenService();
            var secrets = new MobileSecretProtector(DataProtectionProvider.Create(new DirectoryInfo(keyDirectory)));
            var audit = new AuditService(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var options = Options.Create(new FirebasePushOptions { MaxTransientRetries = 0, RetryBaseMilliseconds = 100, LeaseSeconds = 60 });
            var deviceStore = new MobilePushDeviceStore(db, secrets, tokens, audit, clock);
            var attempts = new MobilePushAttemptService(db, provider, deviceStore, options, clock);
            var qrStatus = new QrStatusService(clock);
            var processor = new MobileReminderProcessor(db, qrStatus, deviceStore, attempts, audit, options, clock);
            return new FileFixture(keyDirectory, db, processor, clock, tokens, secrets);
        }

        public async Task<MobileDelivery> SeedDeliveryAsync(bool reminderDue)
        {
            seed++;
            var org = await AddOrganizationCoreAsync(Db, Now, $"file-{Guid.NewGuid():N}");
            var page = new SecurePage
            {
                OrganizationId = org.Id,
                Organization = org,
                QrReference = $"QR-FILE-{Guid.NewGuid():N}"[..30],
                PublicTokenHash = tokens.HashToken($"file-token-{Guid.NewGuid():N}"),
                ProtectedPublicToken = "protected-test-token",
                CurrentTokenCreatedAtUtc = Now,
                TitleArabic = "اختبار",
                TitleEnglish = "Test",
                IsActive = true,
                AccessLimitMode = AccessLimitMode.MaximumSuccessfulAccesses,
                MaxAccessCount = 10,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now
            };
            Db.SecurePages.Add(page);
            await Db.SaveChangesAsync();
            var delivery = new MobileDelivery
            {
                OrganizationId = org.Id,
                SecurePageId = page.Id,
                CreatedAtUtc = Now.AddMinutes(-20),
                SentAtUtc = Now.AddMinutes(-15),
                DeliveryStatus = "PROVIDER_ACCEPTED",
                FirebaseStatus = "PROVIDER_ACCEPTED",
                ReminderEnabled = true,
                ReminderInterval = 10,
                ReminderUnit = "Minutes",
                NextReminderAtUtc = reminderDue ? Now.AddSeconds(-1) : Now.AddMinutes(10)
            };
            Db.MobileDeliveries.Add(delivery);
            await Db.SaveChangesAsync();
            return delivery;
        }

        public async Task AddDeviceAsync(long organizationId, string deviceId, string rawToken)
        {
            Db.MobileDevices.Add(new MobileDevice
            {
                OrganizationId = organizationId,
                DeviceId = deviceId,
                FcmTokenProtected = secrets.ProtectFcmToken(rawToken),
                FcmTokenHash = tokens.HashToken(rawToken),
                Platform = "android",
                AppVersion = "0.1.0",
                PushEnabled = true,
                RegisteredAtUtc = Now,
                LastSeenAtUtc = Now
            });
            await Db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class RecordingProvider : IFirebasePushProvider
    {
        private readonly IReadOnlyList<FirebasePushProviderResult> results;
        private readonly TimeSpan delay;
        private int calls;
        public int CallCount => Volatile.Read(ref calls);
        public List<string> Tokens { get; } = [];

        public RecordingProvider(FirebasePushProviderResult? result = null, IReadOnlyList<FirebasePushProviderResult>? results = null, TimeSpan? delay = null)
        {
            this.results = results ?? [result ?? new(FirebasePushOutcome.Accepted, "PROVIDER_ACCEPTED", "message-1")];
            this.delay = delay ?? TimeSpan.Zero;
        }

        public async Task<FirebasePushProviderResult> SendAsync(string fcmToken, FirebasePushEnvelope envelope, CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref calls);
            lock (Tokens) Tokens.Add(fcmToken);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            return results[Math.Min(call - 1, results.Count - 1)];
        }

        public Task<FirebasePushHealth> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new FirebasePushHealth("READY", "TEST"));
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SecureQrPortal.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRoot);
    }

    private static async Task<Organization> AddOrganizationCoreAsync(ApplicationDbContext db, DateTime now, string suffix)
    {
        var org = new Organization
        {
            NameArabic = $"جهة {suffix}",
            NameEnglish = $"Organization {suffix}",
            MobileNumber = $"9655{Random.Shared.Next(1000000, 9999999)}",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return org;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
