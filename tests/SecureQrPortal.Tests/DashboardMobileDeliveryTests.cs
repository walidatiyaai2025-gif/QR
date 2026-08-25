using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Areas.Admin.Controllers;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Services;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Tests;

public sealed class DashboardMobileDeliveryTests
{
    [Fact]
    public void Push_request_contains_only_delivery_routing_metadata()
    {
        var properties = typeof(MobilePushDispatchRequest).GetProperties();
        Assert.Single(properties);
        Assert.Equal("DeliveryId", properties[0].Name);
        Assert.DoesNotContain(properties, p => p.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Title", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Attachment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Send_contract_does_not_introduce_custom_notification_title_or_body()
    {
        var properties = typeof(MobileDeliverySendVm).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name.Contains("Title", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Subject", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Body", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unconfigured_provider_fails_closed()
    {
        var provider = new UnavailableMobilePushDispatchService();
        var result = await provider.DispatchAsync(new MobilePushDispatchRequest(12));
        Assert.False(result.ProviderAccepted);
        Assert.Equal("PROVIDER_UNAVAILABLE", result.ProviderStatus);
        Assert.Equal("PROVIDER_UNAVAILABLE", result.ErrorCode);
        Assert.Null(result.ProviderMessageId);
    }

    [Fact]
    public void Admin_controller_requires_administrator_role()
    {
        var authorization = typeof(MobileDeliveryController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();
        Assert.Equal("Administrator", authorization.Roles);
    }

    [Fact]
    public void Device_admin_projection_exposes_no_fcm_or_session_secrets()
    {
        var names = typeof(MobileDeviceAdminVm).GetProperties().Select(x => x.Name).ToList();
        Assert.DoesNotContain(names, x => x.Contains("Fcm", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Session", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unknown_secure_page_cannot_send()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.Service.SendAsync(new MobileDeliverySendCommand(999, null, false, null, null));
        Assert.False(result.Success);
        Assert.Equal("SECURE_PAGE_NOT_FOUND", result.Code);
        Assert.Empty(f.Db.MobileDeliveries);
        Assert.Empty(f.Push.Requests);
    }

    [Fact]
    public async Task Inactive_organization_cannot_send()
    {
        await using var f = await Fixture.CreateAsync();
        var page = await f.SeedReadyPageAsync(organizationActive: false);
        var result = await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, false, null, null));
        Assert.Equal("ORGANIZATION_INACTIVE", result.Code);
        Assert.Empty(f.Db.MobileDeliveries);
        Assert.Empty(f.Push.Requests);
    }

    [Fact]
    public async Task Revoked_secure_page_cannot_send()
    {
        await using var f = await Fixture.CreateAsync();
        var page = await f.SeedReadyPageAsync(revoked: true);
        var result = await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, false, null, null));
        Assert.Equal("SECURE_PAGE_NOT_ACTIVE", result.Code);
        Assert.Empty(f.Db.MobileDeliveries);
        Assert.Empty(f.Push.Requests);
    }

    [Fact]
    public async Task Missing_organization_mobile_cannot_send()
    {
        await using var f = await Fixture.CreateAsync();
        var page = await f.SeedReadyPageAsync(mobileConfigured: false);
        var result = await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, false, null, null));
        Assert.Equal("ORGANIZATION_MOBILE_NOT_CONFIGURED", result.Code);
        Assert.Empty(f.Db.MobileDeliveries);
    }

    [Fact]
    public async Task No_active_push_device_returns_truthful_failure()
    {
        await using var f = await Fixture.CreateAsync();
        var page = await f.SeedReadyPageAsync(addDevice: false);
        var result = await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, false, null, null));
        Assert.Equal("NO_REGISTERED_DEVICE", result.Code);
        Assert.Empty(f.Db.MobileDeliveries);
    }

    [Fact]
    public async Task Expiry_in_the_past_is_rejected()
    {
        await using var f = await Fixture.CreateAsync();
        var page = await f.SeedReadyPageAsync();
        var result = await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, f.Now.AddMinutes(-1), false, null, null));
        Assert.Equal("DELIVERY_EXPIRY_INVALID", result.Code);
        Assert.Empty(f.Push.Requests);
    }

    [Fact]
    public async Task Delivery_expiry_cannot_extend_beyond_secure_page_expiry()
    {
        await using var f = await Fixture.CreateAsync();
        var page = await f.SeedReadyPageAsync(pageExpiryUtc: f.Now.AddHours(2));
        var result = await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, f.Now.AddHours(3), false, null, null));
        Assert.Equal("DELIVERY_EXPIRY_EXCEEDS_PAGE", result.Code);
        Assert.Empty(f.Push.Requests);
    }

    [Fact]
    public async Task Invalid_reminder_configuration_is_rejected()
    {
        await using var f = await Fixture.CreateAsync();
        var page = await f.SeedReadyPageAsync();
        var zero = await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, true, 0, "Minutes"));
        var unit = await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, true, 5, "Days"));
        Assert.Equal("REMINDER_INTERVAL_INVALID", zero.Code);
        Assert.Equal("REMINDER_UNIT_INVALID", unit.Code);
        Assert.Empty(f.Push.Requests);
    }

    [Fact]
    public async Task Reminder_disabled_persists_no_schedule_fields()
    {
        await using var f = await Fixture.CreateAsync(providerAccepted: true);
        var page = await f.SeedReadyPageAsync();
        var result = await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, false, 99, "Hours"));
        Assert.True(result.Success);
        var delivery = await f.Db.MobileDeliveries.SingleAsync();
        Assert.False(delivery.ReminderEnabled);
        Assert.Null(delivery.ReminderInterval);
        Assert.Null(delivery.ReminderUnit);
        Assert.Null(delivery.NextReminderAtUtc);
    }

    [Fact]
    public async Task Provider_unavailable_is_persisted_as_failure_not_success()
    {
        await using var f = await Fixture.CreateAsync(useUnavailableProvider: true);
        var page = await f.SeedReadyPageAsync();
        var result = await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, false, null, null));
        Assert.False(result.Success);
        Assert.Equal("PROVIDER_UNAVAILABLE", result.Code);
        var delivery = await f.Db.MobileDeliveries.SingleAsync();
        Assert.Equal("SEND_FAILED", delivery.DeliveryStatus);
        Assert.Equal("PROVIDER_UNAVAILABLE", delivery.FirebaseStatus);
        Assert.Null(delivery.SentAtUtc);
        Assert.Null(delivery.FirstRevealedAtUtc);
    }

    [Fact]
    public async Task Provider_acceptance_is_not_marked_opened()
    {
        await using var f = await Fixture.CreateAsync(providerAccepted: true);
        var page = await f.SeedReadyPageAsync();
        var result = await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, true, 15, "Minutes"));
        Assert.True(result.Success);
        var delivery = await f.Db.MobileDeliveries.SingleAsync();
        Assert.Equal("PROVIDER_ACCEPTED", delivery.DeliveryStatus);
        Assert.Equal("ACCEPTED", delivery.FirebaseStatus);
        Assert.NotNull(delivery.SentAtUtc);
        Assert.Null(delivery.FirstRevealedAtUtc);
        Assert.Equal(f.Now.AddMinutes(15), delivery.NextReminderAtUtc);
        Assert.Single(f.Push.Requests);
        Assert.Equal(delivery.Id, f.Push.Requests[0].DeliveryId);
    }

    [Fact]
    public async Task Page_expiry_is_effective_expiry_when_delivery_does_not_override_it()
    {
        await using var f = await Fixture.CreateAsync(providerAccepted: true);
        var expiry = f.Now.AddHours(4);
        var page = await f.SeedReadyPageAsync(pageExpiryUtc: expiry);
        var result = await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, false, null, null));
        Assert.True(result.Success);
        Assert.Equal(expiry, (await f.Db.MobileDeliveries.SingleAsync()).ExpiresAtUtc);
    }

    [Fact]
    public async Task Opened_filter_uses_only_first_revealed_timestamp()
    {
        await using var f = await Fixture.CreateAsync(providerAccepted: true);
        var page = await f.SeedReadyPageAsync();
        await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, false, null, null));
        var delivery = await f.Db.MobileDeliveries.SingleAsync();
        delivery.DeliveryStatus = "PROVIDER_ACCEPTED";
        delivery.FirebaseStatus = "ACCEPTED";
        await f.Db.SaveChangesAsync();

        Assert.Empty((await f.Service.HistoryAsync(null, null, null, true, 1, 20, "created_desc")).Items);
        Assert.Single((await f.Service.HistoryAsync(null, null, null, false, 1, 20, "created_desc")).Items);

        delivery.FirstRevealedAtUtc = f.Now.AddMinutes(1);
        delivery.DeliveryStatus = "REVEALED";
        await f.Db.SaveChangesAsync();
        Assert.Single((await f.Service.HistoryAsync(null, null, null, true, 1, 20, "created_desc")).Items);
    }

    [Fact]
    public async Task Revocation_is_idempotent_clears_future_reminder_and_is_audited()
    {
        await using var f = await Fixture.CreateAsync(providerAccepted: true);
        var page = await f.SeedReadyPageAsync();
        await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, true, 1, "Hours"));
        var delivery = await f.Db.MobileDeliveries.SingleAsync();
        Assert.NotNull(delivery.NextReminderAtUtc);

        var first = await f.Service.RevokeAsync(delivery.Id);
        var second = await f.Service.RevokeAsync(delivery.Id);
        Assert.True(first.Success);
        Assert.Equal("ALREADY_REVOKED", second.Code);
        var stored = await f.Db.MobileDeliveries.SingleAsync();
        Assert.Equal("REVOKED", stored.DeliveryStatus);
        Assert.NotNull(stored.RevokedAtUtc);
        Assert.Null(stored.NextReminderAtUtc);
        Assert.Single(await f.Db.AuditLogs.Where(x => x.Action == "MOBILE_DELIVERY_REVOKED").ToListAsync());
    }

    [Fact]
    public async Task Reminder_configuration_is_audited_without_secure_content()
    {
        await using var f = await Fixture.CreateAsync(providerAccepted: true);
        var page = await f.SeedReadyPageAsync();
        page.ContentArabicHtml = "<p>TOP SECRET AR</p>";
        page.ContentEnglishHtml = "<p>TOP SECRET EN</p>";
        await f.Db.SaveChangesAsync();
        await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, true, 10, "Minutes"));
        var audit = await f.Db.AuditLogs.SingleAsync(x => x.Action == "MOBILE_REMINDER_CONFIG_CHANGED");
        Assert.Contains("Enabled=True", audit.Details);
        Assert.Contains("Interval=10", audit.Details);
        Assert.DoesNotContain("TOP SECRET", audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_is_server_paginated()
    {
        await using var f = await Fixture.CreateAsync(providerAccepted: true);
        var page = await f.SeedReadyPageAsync();
        for (var i = 0; i < 12; i++)
            await f.Service.SendAsync(new MobileDeliverySendCommand(page.Id, null, false, null, null));

        var first = await f.Service.HistoryAsync(null, null, null, null, 1, 10, "created_desc");
        var second = await f.Service.HistoryAsync(null, null, null, null, 2, 10, "created_desc");
        Assert.Equal(12, first.Total);
        Assert.Equal(10, first.Items.Count);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal(2, first.TotalPages);
    }

    [Fact]
    public async Task Panel_uses_authoritative_secure_page_access_policy_and_safe_device_projection()
    {
        await using var f = await Fixture.CreateAsync();
        var page = await f.SeedReadyPageAsync();
        page.MaxAccessCount = 5;
        page.CurrentSuccessfulAccessCount = 2;
        await f.Db.SaveChangesAsync();
        var panel = await f.Service.GetPanelAsync(page.Id);
        Assert.NotNull(panel);
        Assert.Equal(QrStatus.ACTIVE, panel!.SecurePageStatus);
        Assert.Equal(3, panel.RemainingAccesses);
        Assert.Equal(1, panel.RegisteredDeviceCount);
        Assert.Equal(1, panel.ActiveDeviceCount);
        Assert.DoesNotContain(typeof(MobileDeviceAdminVm).GetProperties(), p => p.Name.Contains("Fcm", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly FixedTimeProvider clock;
        private int seed;

        public ApplicationDbContext Db { get; }
        public DateTime Now => clock.GetUtcNow().UtcDateTime;
        public RecordingPush Push { get; }
        public MobileDeliveryAdminService Service { get; }

        private Fixture(SqliteConnection connection, ApplicationDbContext db, FixedTimeProvider clock, RecordingPush push, MobileDeliveryAdminService service)
        {
            this.connection = connection;
            Db = db;
            this.clock = clock;
            Push = push;
            Service = service;
        }

        public static async Task<Fixture> CreateAsync(bool providerAccepted = false, bool useUnavailableProvider = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
            var push = new RecordingPush(providerAccepted
                ? new MobilePushDispatchResult(true, "ACCEPTED", "provider-message-1")
                : new MobilePushDispatchResult(false, "PROVIDER_REJECTED", ErrorCode: "PROVIDER_REJECTED"));
            IMobilePushDispatchService provider = useUnavailableProvider ? new UnavailableMobilePushDispatchService() : push;
            var audit = new AuditService(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var service = new MobileDeliveryAdminService(db, new QrStatusService(clock), provider, audit, clock);
            return new Fixture(connection, db, clock, push, service);
        }

        public async Task<SecurePage> SeedReadyPageAsync(
            bool organizationActive = true,
            bool mobileConfigured = true,
            bool addDevice = true,
            bool revoked = false,
            DateTime? pageExpiryUtc = null)
        {
            seed++;
            var org = new Organization
            {
                NameArabic = $"جهة {seed}",
                NameEnglish = $"Organization {seed}",
                MobileNumber = mobileConfigured ? $"9655{seed:0000000}" : null,
                IsActive = organizationActive,
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
                ContentArabicHtml = "<p>secure ar</p>",
                ContentEnglishHtml = "<p>secure en</p>",
                IsActive = true,
                ValidFromUtc = Now.AddHours(-1),
                ExpiresAtUtc = pageExpiryUtc ?? Now.AddDays(1),
                AccessLimitMode = AccessLimitMode.MaximumSuccessfulAccesses,
                MaxAccessCount = 10,
                CurrentSuccessfulAccessCount = 0,
                RevokedAtUtc = revoked ? Now.AddMinutes(-1) : null,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now
            };
            Db.SecurePages.Add(page);
            await Db.SaveChangesAsync();

            if (addDevice)
            {
                Db.MobileDevices.Add(new MobileDevice
                {
                    DeviceId = $"device-{seed}",
                    OrganizationId = org.Id,
                    FcmTokenProtected = $"protected-fcm-{seed}",
                    FcmTokenHash = $"fcm-hash-{seed}",
                    Platform = "android",
                    AppVersion = "1.0.0",
                    PushEnabled = true,
                    RegisteredAtUtc = Now,
                    LastSeenAtUtc = Now,
                    ConcurrencyStamp = Guid.NewGuid().ToString("N")
                });
                await Db.SaveChangesAsync();
            }

            return page;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
