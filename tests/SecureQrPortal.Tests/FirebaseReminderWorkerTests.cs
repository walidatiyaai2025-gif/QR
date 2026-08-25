using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class FirebaseReminderWorkerTests
{
    [Fact]
    public async Task Firebase_dispatch_contains_only_fixed_copy_and_safe_routing_metadata()
    {
        await using var f = await Fixture.CreateAsync();
        var delivery = await f.SeedDeliveryAsync();

        var result = await f.FirebaseDispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));

        Assert.True(result.ProviderAccepted);
        var outbound = Assert.Single(f.Firebase.Messages);
        Assert.Equal("DA Secure", outbound.NotificationTitle);
        Assert.Equal(FirebaseMobilePushDispatchService.FixedHeadingArabic, outbound.NotificationBody);
        Assert.Equal(new[] { "deliveryId", "notificationCategory", "version" }, outbound.Data.Keys.OrderBy(x => x).ToArray());
        Assert.Equal(delivery.Id.ToString(), outbound.Data["deliveryId"]);
        Assert.Equal("secure_delivery", outbound.Data["notificationCategory"]);
        Assert.DoesNotContain("TOP SECRET", string.Join("|", outbound.Data.Values), StringComparison.Ordinal);
        Assert.DoesNotContain("TOP SECRET", outbound.NotificationBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unregistered_fcm_target_is_retired_without_logging_token()
    {
        await using var f = await Fixture.CreateAsync(tokenInvalid: true);
        var delivery = await f.SeedDeliveryAsync();

        var result = await f.FirebaseDispatch.DispatchAsync(new MobilePushDispatchRequest(delivery.Id));

        Assert.False(result.ProviderAccepted);
        f.Db.ChangeTracker.Clear();
        var device = await f.Db.MobileDevices.SingleAsync();
        Assert.False(device.PushEnabled);
        Assert.NotNull(device.DeactivatedAtUtc);
        Assert.Equal(string.Empty, device.FcmTokenProtected);
        var auditText = string.Join("|", await f.Db.AuditLogs.Select(x => x.Details).ToListAsync());
        Assert.DoesNotContain(Fixture.RawFcmToken, auditText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Due_reminder_is_claimed_once_and_schedules_next_interval()
    {
        await using var f = await Fixture.CreateAsync();
        var delivery = await f.SeedDeliveryAsync(reminderEnabled: true, reminderDue: true);

        var first = await f.Reminders.ProcessDueAsync();
        var second = await f.Reminders.ProcessDueAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Single(f.RecordingPush.Requests);
        Assert.Equal(delivery.Id, f.RecordingPush.Requests[0].DeliveryId);
        f.Db.ChangeTracker.Clear();
        var stored = await f.Db.MobileDeliveries.SingleAsync();
        Assert.Equal(1, stored.ReminderCount);
        Assert.Equal(f.Now, stored.LastReminderAtUtc);
        Assert.Equal(f.Now.AddMinutes(15), stored.NextReminderAtUtc);
    }

    [Fact]
    public async Task Expired_source_stops_reminder_without_dispatch()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedDeliveryAsync(reminderEnabled: true, reminderDue: true, pageExpired: true);

        var processed = await f.Reminders.ProcessDueAsync();

        Assert.Equal(1, processed);
        Assert.Empty(f.RecordingPush.Requests);
        f.Db.ChangeTracker.Clear();
        Assert.Null((await f.Db.MobileDeliveries.SingleAsync()).NextReminderAtUtc);
        Assert.Contains(await f.Db.AuditLogs.ToListAsync(), x => x.Action == "MOBILE_REMINDER_STOPPED");
    }

    [Fact]
    public async Task Failed_reminder_does_not_increment_count_and_retries_on_configured_interval()
    {
        await using var f = await Fixture.CreateAsync(reminderAccepted: false);
        await f.SeedDeliveryAsync(reminderEnabled: true, reminderDue: true);

        await f.Reminders.ProcessDueAsync();

        f.Db.ChangeTracker.Clear();
        var stored = await f.Db.MobileDeliveries.SingleAsync();
        Assert.Equal(0, stored.ReminderCount);
        Assert.Null(stored.LastReminderAtUtc);
        Assert.Equal(f.Now.AddMinutes(15), stored.NextReminderAtUtc);
        Assert.Equal("PROVIDER_UNAVAILABLE", stored.FirebaseStatus);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string RawFcmToken = "fcm-registration-token-test-only";

        private readonly SqliteConnection connection;
        private readonly MobileSecretProtector secrets;
        private int seed;

        public ApplicationDbContext Db { get; }
        public FixedTimeProvider Clock { get; }
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;
        public CapturingFirebaseClient Firebase { get; }
        public FirebaseMobilePushDispatchService FirebaseDispatch { get; }
        public RecordingPush RecordingPush { get; }
        public MobileReminderService Reminders { get; }

        private Fixture(
            SqliteConnection connection,
            ApplicationDbContext db,
            MobileSecretProtector secrets,
            FixedTimeProvider clock,
            CapturingFirebaseClient firebase,
            FirebaseMobilePushDispatchService firebaseDispatch,
            RecordingPush recordingPush,
            MobileReminderService reminders)
        {
            this.connection = connection;
            Db = db;
            this.secrets = secrets;
            Clock = clock;
            Firebase = firebase;
            FirebaseDispatch = firebaseDispatch;
            RecordingPush = recordingPush;
            Reminders = reminders;
        }

        public static async Task<Fixture> CreateAsync(bool tokenInvalid = false, bool reminderAccepted = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
            var audit = new AuditService(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var secrets = new MobileSecretProtector(new EphemeralDataProtectionProvider());
            var tokens = new MobileTokenService();
            var firebase = new CapturingFirebaseClient(tokenInvalid);
            var dispatch = new FirebaseMobilePushDispatchService(db, secrets, tokens, firebase, audit, clock);
            var recordingPush = new RecordingPush(reminderAccepted
                ? new MobilePushDispatchResult(true, "FCM_ACCEPTED", "reminder-message-1")
                : new MobilePushDispatchResult(false, "PROVIDER_UNAVAILABLE", ErrorCode: "PROVIDER_UNAVAILABLE"));
            var reminders = new MobileReminderService(db, recordingPush, new QrStatusService(clock), audit, clock);
            return new Fixture(connection, db, secrets, clock, firebase, dispatch, recordingPush, reminders);
        }

        public async Task<MobileDelivery> SeedDeliveryAsync(
            bool reminderEnabled = false,
            bool reminderDue = false,
            bool pageExpired = false)
        {
            seed++;
            var org = new Organization
            {
                NameArabic = $"جهة {seed}",
                NameEnglish = $"Organization {seed}",
                MobileNumber = $"9655{seed:0000000}",
                IsActive = true,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now
            };
            Db.Organizations.Add(org);
            await Db.SaveChangesAsync();

            var page = new SecurePage
            {
                OrganizationId = org.Id,
                Organization = org,
                QrReference = $"QR-2026-{seed:000000}",
                PublicTokenHash = $"hash-{seed}",
                ProtectedPublicToken = $"protected-{seed}",
                CurrentTokenCreatedAtUtc = Now,
                TitleArabic = "عنوان",
                TitleEnglish = "Title",
                ContentArabicHtml = "<p>TOP SECRET AR</p>",
                ContentEnglishHtml = "<p>TOP SECRET EN</p>",
                IsActive = true,
                ValidFromUtc = Now.AddHours(-1),
                ExpiresAtUtc = pageExpired ? Now.AddMinutes(-1) : Now.AddDays(1),
                AccessLimitMode = AccessLimitMode.MaximumSuccessfulAccesses,
                MaxAccessCount = 10,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now
            };
            Db.SecurePages.Add(page);
            await Db.SaveChangesAsync();

            var device = new MobileDevice
            {
                DeviceId = $"device-{seed}",
                OrganizationId = org.Id,
                FcmTokenProtected = secrets.ProtectFcmToken(RawFcmToken),
                FcmTokenHash = new MobileTokenService().HashToken(RawFcmToken),
                Platform = "android",
                AppVersion = "1.0.0",
                PushEnabled = true,
                RegisteredAtUtc = Now,
                LastSeenAtUtc = Now,
                ConcurrencyStamp = Guid.NewGuid().ToString("N")
            };
            Db.MobileDevices.Add(device);

            var delivery = new MobileDelivery
            {
                OrganizationId = org.Id,
                Organization = org,
                SecurePageId = page.Id,
                SecurePage = page,
                CreatedAtUtc = Now.AddMinutes(-30),
                SentAtUtc = Now.AddMinutes(-30),
                DeliveryStatus = "PROVIDER_ACCEPTED",
                FirebaseStatus = "FCM_ACCEPTED",
                ExpiresAtUtc = page.ExpiresAtUtc,
                ReminderEnabled = reminderEnabled,
                ReminderInterval = reminderEnabled ? 15 : null,
                ReminderUnit = reminderEnabled ? "Minutes" : null,
                NextReminderAtUtc = reminderEnabled
                    ? (reminderDue ? Now.AddMinutes(-1) : Now.AddMinutes(15))
                    : null,
                ReminderCount = 0,
                ConcurrencyStamp = Guid.NewGuid().ToString("N")
            };
            Db.MobileDeliveries.Add(delivery);
            await Db.SaveChangesAsync();
            return delivery;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class CapturingFirebaseClient(bool tokenInvalid) : IFirebaseMessagingClient
    {
        public List<FirebaseOutboundMessage> Messages { get; } = [];

        public Task<FirebaseBatchSendResult> SendAsync(IReadOnlyList<FirebaseOutboundMessage> messages, CancellationToken ct = default)
        {
            Messages.AddRange(messages);
            var results = messages.Select(x => tokenInvalid
                ? new FirebaseTargetSendResult(x.DeviceId, false, ErrorCode: "UNREGISTERED", TokenInvalid: true)
                : new FirebaseTargetSendResult(x.DeviceId, true, $"message-{x.DeviceId}"))
                .ToList();
            return Task.FromResult(new FirebaseBatchSendResult(true, tokenInvalid ? "FCM_REJECTED" : "FCM_ACCEPTED", results));
        }
    }

    private sealed class RecordingPush(MobilePushDispatchResult result) : IMobilePushDispatchService
    {
        public List<MobilePushDispatchRequest> Requests { get; } = [];

        public Task<MobilePushDispatchResult> DispatchAsync(MobilePushDispatchRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }

    public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
