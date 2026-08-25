using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Services;

public sealed record MobileDeliverySendCommand(
    long SecurePageId,
    DateTime? ExpiresAtUtc,
    bool ReminderEnabled,
    int? ReminderInterval,
    string? ReminderUnit);

public sealed record MobileDeliveryAdminResult(
    bool Success,
    string Code,
    long? DeliveryId = null,
    string? ProviderStatus = null);

public sealed class MobileDeliveryAdminService(
    ApplicationDbContext db,
    QrStatusService qrStatus,
    IMobilePushDispatchService push,
    AuditService audit,
    TimeProvider timeProvider)
{
    public async Task<MobileDeliveryAdminResult> SendAsync(MobileDeliverySendCommand command, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var page = await db.SecurePages
            .Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == command.SecurePageId, ct);
        if (page is null) return Fail("SECURE_PAGE_NOT_FOUND");
        if (!page.Organization.IsActive) return Fail("ORGANIZATION_INACTIVE");
        if (qrStatus.GetStatus(page) != QrStatus.ACTIVE) return Fail("SECURE_PAGE_NOT_ACTIVE");
        if (string.IsNullOrWhiteSpace(page.Organization.MobileNumber)) return Fail("ORGANIZATION_MOBILE_NOT_CONFIGURED");

        var hasActiveDevice = await db.MobileDevices.AsNoTracking().AnyAsync(
            x => x.OrganizationId == page.OrganizationId && x.DeactivatedAtUtc == null && x.PushEnabled,
            ct);
        if (!hasActiveDevice) return Fail("NO_REGISTERED_DEVICE");

        if (command.ExpiresAtUtc.HasValue)
        {
            if (command.ExpiresAtUtc.Value <= now) return Fail("DELIVERY_EXPIRY_INVALID");
            if (page.ExpiresAtUtc.HasValue && command.ExpiresAtUtc.Value > page.ExpiresAtUtc.Value)
                return Fail("DELIVERY_EXPIRY_EXCEEDS_PAGE");
        }

        var reminder = ValidateReminder(command.ReminderEnabled, command.ReminderInterval, command.ReminderUnit);
        if (!reminder.Success) return Fail(reminder.Code);

        var delivery = new MobileDelivery
        {
            OrganizationId = page.OrganizationId,
            SecurePageId = page.Id,
            CreatedAtUtc = now,
            DeliveryStatus = "CREATED",
            FirebaseStatus = "PENDING",
            ExpiresAtUtc = EffectiveExpiry(command.ExpiresAtUtc, page.ExpiresAtUtc),
            ReminderEnabled = command.ReminderEnabled,
            ReminderInterval = command.ReminderEnabled ? command.ReminderInterval : null,
            ReminderUnit = command.ReminderEnabled ? reminder.NormalizedUnit : null,
            NextReminderAtUtc = null,
            ReminderCount = 0,
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };
        db.MobileDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("MOBILE_DELIVERY_CREATED", "MobileDelivery", delivery.Id.ToString(),
            $"SecurePageId={page.Id};OrganizationId={page.OrganizationId};ReminderEnabled={delivery.ReminderEnabled}", ct);
        await audit.WriteAsync("MOBILE_REMINDER_CONFIG_CHANGED", "MobileDelivery", delivery.Id.ToString(),
            $"Enabled={delivery.ReminderEnabled};Interval={delivery.ReminderInterval?.ToString() ?? "none"};Unit={delivery.ReminderUnit ?? "none"}", ct);
        await audit.WriteAsync("MOBILE_DELIVERY_SEND_REQUESTED", "MobileDelivery", delivery.Id.ToString(),
            $"SecurePageId={page.Id};OrganizationId={page.OrganizationId}", ct);

        MobilePushDispatchResult provider;
        try
        {
            provider = await push.DispatchAsync(new MobilePushDispatchRequest(delivery.Id), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            provider = new MobilePushDispatchResult(false, "PROVIDER_ERROR", ErrorCode: "PROVIDER_ERROR");
        }

        delivery.FirebaseStatus = SafeProviderStatus(provider.ProviderStatus);
        delivery.FirebaseProviderMessageId = provider.ProviderAccepted ? SafeProviderMessageId(provider.ProviderMessageId) : null;
        delivery.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        if (provider.ProviderAccepted)
        {
            delivery.DeliveryStatus = "PROVIDER_ACCEPTED";
            delivery.SentAtUtc = now;
            delivery.NextReminderAtUtc = delivery.ReminderEnabled
                ? AddInterval(now, delivery.ReminderInterval!.Value, delivery.ReminderUnit!)
                : null;
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync("MOBILE_DELIVERY_SEND_ACCEPTED", "MobileDelivery", delivery.Id.ToString(),
                $"ProviderStatus={delivery.FirebaseStatus}", ct);
            return new(true, "PROVIDER_ACCEPTED", delivery.Id, delivery.FirebaseStatus);
        }

        delivery.DeliveryStatus = "SEND_FAILED";
        delivery.NextReminderAtUtc = null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("MOBILE_DELIVERY_SEND_FAILED", "MobileDelivery", delivery.Id.ToString(),
            $"ProviderStatus={delivery.FirebaseStatus};ErrorCode={SafeCode(provider.ErrorCode)}", ct);
        return new(false, provider.ErrorCode ?? "SEND_FAILED", delivery.Id, delivery.FirebaseStatus);
    }

    public async Task<MobileDeliveryAdminResult> RevokeAsync(long deliveryId, CancellationToken ct = default)
    {
        var delivery = await db.MobileDeliveries.SingleOrDefaultAsync(x => x.Id == deliveryId, ct);
        if (delivery is null) return Fail("DELIVERY_NOT_FOUND");
        if (delivery.RevokedAtUtc.HasValue)
            return new(true, "ALREADY_REVOKED", delivery.Id, delivery.FirebaseStatus);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        delivery.RevokedAtUtc = now;
        delivery.DeliveryStatus = "REVOKED";
        delivery.NextReminderAtUtc = null;
        delivery.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("MOBILE_DELIVERY_REVOKED", "MobileDelivery", delivery.Id.ToString(),
            $"OrganizationId={delivery.OrganizationId};SecurePageId={delivery.SecurePageId}", ct);
        return new(true, "REVOKED", delivery.Id, delivery.FirebaseStatus);
    }

    public async Task<QrMobileDeliveryPanelVm?> GetPanelAsync(long securePageId, CancellationToken ct = default)
    {
        var page = await db.SecurePages.AsNoTracking().Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == securePageId, ct);
        if (page is null) return null;

        var devices = await db.MobileDevices.AsNoTracking()
            .Where(x => x.OrganizationId == page.OrganizationId)
            .OrderByDescending(x => x.LastSeenAtUtc)
            .Select(x => new MobileDeviceAdminVm
            {
                Platform = x.Platform,
                AppVersion = x.AppVersion,
                PushEnabled = x.PushEnabled,
                RegisteredAtUtc = x.RegisteredAtUtc,
                LastSeenAtUtc = x.LastSeenAtUtc,
                DeactivatedAtUtc = x.DeactivatedAtUtc
            })
            .ToListAsync(ct);

        var latestEntity = await DeliveryQuery()
            .Where(x => x.SecurePageId == securePageId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        return new QrMobileDeliveryPanelVm
        {
            SecurePageId = securePageId,
            QrReference = page.QrReference,
            SecurePageStatus = qrStatus.GetStatus(page),
            SecurePageExpiresAtUtc = page.ExpiresAtUtc,
            AccessLimitMode = page.AccessLimitMode,
            MaxAccessCount = page.MaxAccessCount,
            RemainingAccesses = QrStatusService.RemainingAccesses(page),
            OrganizationActive = page.Organization.IsActive,
            OrganizationMobileNumber = page.Organization.MobileNumber,
            RegisteredDeviceCount = devices.Count,
            ActiveDeviceCount = devices.Count(x => x.IsActive && x.PushEnabled),
            Devices = devices,
            LatestDelivery = latestEntity is null ? null : ToHistoryItem(latestEntity)
        };
    }

    public async Task<MobileDeliveryHistoryVm> HistoryAsync(
        long? organizationId, long? securePageId, string? status, bool? opened,
        int page, int pageSize, string sort, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var query = DeliveryQuery();
        if (organizationId.HasValue) query = query.Where(x => x.OrganizationId == organizationId.Value);
        if (securePageId.HasValue) query = query.Where(x => x.SecurePageId == securePageId.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.DeliveryStatus == status.Trim());
        if (opened.HasValue) query = opened.Value
            ? query.Where(x => x.FirstRevealedAtUtc != null)
            : query.Where(x => x.FirstRevealedAtUtc == null);

        query = sort switch
        {
            "created" => query.OrderBy(x => x.CreatedAtUtc),
            "sent" => query.OrderBy(x => x.SentAtUtc),
            "sent_desc" => query.OrderByDescending(x => x.SentAtUtc),
            _ => query.OrderByDescending(x => x.CreatedAtUtc)
        };

        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new MobileDeliveryHistoryVm
        {
            Items = rows.Select(ToHistoryItem).ToList(),
            OrganizationId = organizationId,
            SecurePageId = securePageId,
            Status = status,
            Opened = opened,
            Page = page,
            PageSize = pageSize,
            Total = total,
            Sort = sort
        };
    }

    public async Task<MobileDeliveryDetailsVm?> DetailsAsync(long deliveryId, CancellationToken ct = default)
    {
        var delivery = await DeliveryQuery().SingleOrDefaultAsync(x => x.Id == deliveryId, ct);
        if (delivery is null) return null;
        var sourceStatus = qrStatus.GetStatus(delivery.SecurePage).ToString();
        var remaining = QrStatusService.RemainingAccesses(delivery.SecurePage);
        var auditRows = await db.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == "MobileDelivery" && x.EntityId == deliveryId.ToString())
            .OrderByDescending(x => x.TimestampUtc)
            .Take(100)
            .ToListAsync(ct);
        var adminIds = auditRows.Select(x => x.AdminUserId).OfType<string>().Distinct().ToList();
        var adminNames = await db.Users.AsNoTracking()
            .Where(x => adminIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        return new MobileDeliveryDetailsVm
        {
            Delivery = ToHistoryItem(delivery),
            SourceStatus = sourceStatus,
            RemainingReveals = remaining,
            UnlimitedReveals = !remaining.HasValue,
            Audit = auditRows.Select(x => new MobileDeliveryAuditItemVm
            {
                TimestampUtc = x.TimestampUtc,
                Action = x.Action,
                Admin = x.AdminUserId is not null && adminNames.TryGetValue(x.AdminUserId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : "—",
                Details = x.Details
            }).ToList()
        };
    }

    private IQueryable<MobileDelivery> DeliveryQuery() => db.MobileDeliveries.AsNoTracking()
        .Include(x => x.Organization)
        .Include(x => x.SecurePage).ThenInclude(x => x.Organization);

    private static MobileDeliveryHistoryItemVm ToHistoryItem(MobileDelivery x) => new()
    {
        Id = x.Id,
        SecurePageId = x.SecurePageId,
        QrReference = x.SecurePage.QrReference,
        OrganizationId = x.OrganizationId,
        OrganizationArabic = x.Organization.NameArabic,
        OrganizationEnglish = x.Organization.NameEnglish,
        CreatedAtUtc = x.CreatedAtUtc,
        SentAtUtc = x.SentAtUtc,
        ExpiresAtUtc = x.ExpiresAtUtc,
        DeliveryStatus = x.DeliveryStatus,
        FirebaseStatus = x.FirebaseStatus,
        ReminderEnabled = x.ReminderEnabled,
        ReminderInterval = x.ReminderInterval,
        ReminderUnit = x.ReminderUnit,
        ReminderCount = x.ReminderCount,
        LastReminderAtUtc = x.LastReminderAtUtc,
        NextReminderAtUtc = x.NextReminderAtUtc,
        FirstRevealedAtUtc = x.FirstRevealedAtUtc,
        RevokedAtUtc = x.RevokedAtUtc
    };

    private static (bool Success, string Code, string? NormalizedUnit) ValidateReminder(bool enabled, int? interval, string? unit)
    {
        if (!enabled) return (true, "OK", null);
        if (!interval.HasValue || interval.Value <= 0) return (false, "REMINDER_INTERVAL_INVALID", null);
        var normalized = unit?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "minutes" when interval.Value <= 10080 => (true, "OK", "Minutes"),
            "hours" when interval.Value <= 168 => (true, "OK", "Hours"),
            "minutes" or "hours" => (false, "REMINDER_INTERVAL_OUT_OF_RANGE", null),
            _ => (false, "REMINDER_UNIT_INVALID", null)
        };
    }

    private static DateTime? EffectiveExpiry(DateTime? delivery, DateTime? page)
    {
        if (!delivery.HasValue) return page;
        if (!page.HasValue) return delivery;
        return delivery.Value <= page.Value ? delivery : page;
    }

    private static DateTime AddInterval(DateTime value, int interval, string unit) =>
        unit == "Hours" ? value.AddHours(interval) : value.AddMinutes(interval);

    private static string SafeProviderStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ? "UNKNOWN" : status.Trim()[..Math.Min(status.Trim().Length, 40)];

    private static string? SafeProviderMessageId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 200)];

    private static string SafeCode(string? value) => string.IsNullOrWhiteSpace(value) ? "NONE" : value.Trim()[..Math.Min(value.Trim().Length, 80)];
    private static MobileDeliveryAdminResult Fail(string code) => new(false, code);
}
