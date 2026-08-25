using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureQrPortal.Data;
using SecureQrPortal.Models;

namespace SecureQrPortal.Services;

public sealed class MobileReminderProcessor(
    ApplicationDbContext db,
    QrStatusService qrStatus,
    MobilePushDeviceStore devices,
    MobilePushAttemptService attempts,
    AuditService audit,
    IOptions<FirebasePushOptions> configuredOptions,
    TimeProvider timeProvider)
{
    private readonly FirebasePushOptions options = configuredOptions.Value;

    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var dueIds = await db.MobileDeliveries.AsNoTracking()
            .Where(x => x.ReminderEnabled &&
                        x.NextReminderAtUtc != null && x.NextReminderAtUtc <= now &&
                        (x.ProcessingLeaseUntilUtc == null || x.ProcessingLeaseUntilUtc < now))
            .OrderBy(x => x.NextReminderAtUtc)
            .Select(x => x.Id)
            .Take(100)
            .ToListAsync(ct);

        var processed = 0;
        foreach (var deliveryId in dueIds)
        {
            if (await TryProcessAsync(deliveryId, ct)) processed++;
        }
        return processed;
    }

    private async Task<bool> TryProcessAsync(long deliveryId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var leaseId = Guid.NewGuid().ToString("N");
        var leaseUntil = now.AddSeconds(Math.Clamp(options.LeaseSeconds, 30, 900));
        var claimed = await db.MobileDeliveries
            .Where(x => x.Id == deliveryId && x.ReminderEnabled &&
                        x.NextReminderAtUtc != null && x.NextReminderAtUtc <= now &&
                        (x.ProcessingLeaseUntilUtc == null || x.ProcessingLeaseUntilUtc < now))
            .ExecuteUpdateAsync(x => x
                .SetProperty(d => d.ProcessingLeaseId, leaseId)
                .SetProperty(d => d.ProcessingLeaseUntilUtc, leaseUntil)
                .SetProperty(d => d.ConcurrencyStamp, Guid.NewGuid().ToString("N")), ct);
        if (claimed != 1) return false;

        try
        {
            var delivery = await LoadDeliveryAsync(deliveryId, ct);
            if (delivery is null) return true;

            var stopReason = StopReason(delivery, now);
            if (stopReason is not null)
            {
                await StopAsync(delivery, stopReason, now, ct);
                return true;
            }

            var next = NextOccurrence(now, delivery.ReminderInterval, delivery.ReminderUnit);
            if (!next.HasValue)
            {
                await StopAsync(delivery, "REMINDER_CONFIG_INVALID", now, ct);
                return true;
            }

            var resumeCycle = delivery.ReminderCycleStartedAtUtc.HasValue &&
                              (!delivery.ReminderCycleCompletedAtUtc.HasValue ||
                               delivery.ReminderCycleCompletedAtUtc.Value < delivery.ReminderCycleStartedAtUtc.Value);
            if (!resumeCycle)
            {
                delivery.ReminderSequence++;
                delivery.ReminderCycleStartedAtUtc = now;
                delivery.ReminderCycleCompletedAtUtc = null;
                delivery.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                await db.SaveChangesAsync(ct);
            }

            await audit.WriteAsync("MOBILE_REMINDER_DUE", "MobileDelivery", delivery.Id.ToString(),
                $"Sequence={delivery.ReminderSequence};OrganizationId={delivery.OrganizationId}", ct);

            // Re-check after the durable claim and immediately before any external send.
            var beforeSend = await LoadDeliveryAsNoTrackingAsync(deliveryId, ct);
            if (beforeSend is null) return true;
            stopReason = StopReason(beforeSend, timeProvider.GetUtcNow().UtcDateTime);
            if (stopReason is not null)
            {
                db.Entry(delivery).State = EntityState.Detached;
                var tracked = await LoadDeliveryAsync(deliveryId, ct);
                if (tracked is not null) await StopAsync(tracked, stopReason, timeProvider.GetUtcNow().UtcDateTime, ct);
                return true;
            }

            var targets = await devices.GetActiveTargetsAsync(delivery.OrganizationId, ct);
            if (targets.Count == 0)
            {
                delivery.FirebaseStatus = "NO_REGISTERED_DEVICE";
                delivery.FirebaseErrorCode = "NO_ACTIVE_DEVICE";
                delivery.ReminderCycleCompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                delivery.NextReminderAtUtc = next;
                delivery.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                await db.SaveChangesAsync(ct);
                await audit.WriteAsync("MOBILE_REMINDER_SKIPPED", "MobileDelivery", delivery.Id.ToString(),
                    $"Sequence={delivery.ReminderSequence};Reason=NO_REGISTERED_DEVICE", ct);
                return true;
            }

            var results = new List<FirebasePushProviderResult>(targets.Count);
            foreach (var target in targets)
            {
                results.Add(await attempts.SendWithRetryAsync(
                    delivery.Id,
                    target,
                    "REMINDER",
                    delivery.ReminderSequence,
                    MobilePushConstants.ReminderCategory,
                    ct));
            }

            var aggregate = FirebaseMobilePushDispatchService.Aggregate(results);
            var completedAt = timeProvider.GetUtcNow().UtcDateTime;

            // A reveal/revoke may race an in-flight network request. Never restore a future schedule after it stopped.
            var afterSend = await LoadDeliveryAsNoTrackingAsync(deliveryId, ct);
            var postSendStopReason = afterSend is null ? "DELIVERY_NOT_FOUND" : StopReason(afterSend, completedAt);

            if (aggregate.ProviderAccepted)
            {
                delivery.FirebaseStatus = "PROVIDER_ACCEPTED";
                delivery.FirebaseProviderMessageId = SafeMessageId(aggregate.ProviderMessageId);
                delivery.FirebaseErrorCode = null;
                delivery.LastReminderAtUtc = completedAt;
                delivery.ReminderCount++;
                delivery.ReminderCycleCompletedAtUtc = completedAt;
                delivery.NextReminderAtUtc = postSendStopReason is null
                    ? NextOccurrence(completedAt, delivery.ReminderInterval, delivery.ReminderUnit)
                    : null;
                delivery.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                await db.SaveChangesAsync(ct);
                await audit.WriteAsync("MOBILE_REMINDER_SEND_ACCEPTED", "MobileDelivery", delivery.Id.ToString(),
                    $"Sequence={delivery.ReminderSequence};ReminderCount={delivery.ReminderCount}", ct);
            }
            else
            {
                delivery.FirebaseStatus = SafeStatus(aggregate.ProviderStatus);
                delivery.FirebaseProviderMessageId = null;
                delivery.FirebaseErrorCode = SafeErrorCode(aggregate.ErrorCode);
                delivery.ReminderCycleCompletedAtUtc = completedAt;
                delivery.NextReminderAtUtc = postSendStopReason is null
                    ? NextOccurrence(completedAt, delivery.ReminderInterval, delivery.ReminderUnit)
                    : null;
                delivery.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                await db.SaveChangesAsync(ct);
                await audit.WriteAsync("MOBILE_REMINDER_SEND_FAILED", "MobileDelivery", delivery.Id.ToString(),
                    $"Sequence={delivery.ReminderSequence};ProviderStatus={delivery.FirebaseStatus};ErrorCode={delivery.FirebaseErrorCode ?? "NONE"}", ct);
            }

            if (postSendStopReason is not null && postSendStopReason != "DELIVERY_NOT_FOUND")
            {
                await audit.WriteAsync("MOBILE_REMINDER_STOPPED", "MobileDelivery", delivery.Id.ToString(),
                    $"Reason={postSendStopReason}", ct);
            }
            return true;
        }
        finally
        {
            await ReleaseLeaseAsync(deliveryId, leaseId, CancellationToken.None);
        }
    }

    private Task<MobileDelivery?> LoadDeliveryAsync(long deliveryId, CancellationToken ct) =>
        db.MobileDeliveries
            .Include(x => x.Organization)
            .Include(x => x.SecurePage).ThenInclude(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == deliveryId, ct);

    private Task<MobileDelivery?> LoadDeliveryAsNoTrackingAsync(long deliveryId, CancellationToken ct) =>
        db.MobileDeliveries.AsNoTracking()
            .Include(x => x.Organization)
            .Include(x => x.SecurePage).ThenInclude(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == deliveryId, ct);

    private string? StopReason(MobileDelivery delivery, DateTime now)
    {
        if (!delivery.ReminderEnabled) return "REMINDER_DISABLED";
        if (delivery.FirstRevealedAtUtc.HasValue) return "FIRST_SECURE_REVEAL";
        if (delivery.RevokedAtUtc.HasValue) return "DELIVERY_REVOKED";
        if (delivery.ExpiresAtUtc.HasValue && delivery.ExpiresAtUtc.Value <= now) return "DELIVERY_EXPIRED";
        if (!delivery.Organization.IsActive) return "ORGANIZATION_DISABLED";
        if (delivery.SecurePage.OrganizationId != delivery.OrganizationId) return "TENANT_MISMATCH";
        var sourceStatus = qrStatus.GetStatus(delivery.SecurePage);
        return sourceStatus == QrStatus.ACTIVE ? null : $"SECURE_PAGE_{sourceStatus}";
    }

    private async Task StopAsync(MobileDelivery delivery, string reason, DateTime now, CancellationToken ct)
    {
        delivery.NextReminderAtUtc = null;
        delivery.ReminderCycleCompletedAtUtc = delivery.ReminderCycleStartedAtUtc.HasValue ? now : delivery.ReminderCycleCompletedAtUtc;
        if (reason == "DELIVERY_EXPIRED" && !delivery.FirstRevealedAtUtc.HasValue && !delivery.RevokedAtUtc.HasValue)
            delivery.DeliveryStatus = "EXPIRED";
        delivery.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("MOBILE_REMINDER_STOPPED", "MobileDelivery", delivery.Id.ToString(), $"Reason={reason}", ct);
    }

    private async Task ReleaseLeaseAsync(long deliveryId, string leaseId, CancellationToken ct)
    {
        try
        {
            await db.MobileDeliveries
                .Where(x => x.Id == deliveryId && x.ProcessingLeaseId == leaseId)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(d => d.ProcessingLeaseId, d => (string?)null)
                    .SetProperty(d => d.ProcessingLeaseUntilUtc, d => (DateTime?)null), ct);
        }
        catch (Exception)
        {
            // A bounded DB lease expires automatically; release failure must not cause duplicate immediate execution.
        }
    }

    internal static DateTime? NextOccurrence(DateTime fromUtc, int? interval, string? unit)
    {
        if (!interval.HasValue || interval.Value <= 0) return null;
        return unit switch
        {
            "Minutes" => fromUtc.AddMinutes(interval.Value),
            "Hours" => fromUtc.AddHours(interval.Value),
            _ => null
        };
    }

    private static string SafeStatus(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "SEND_FAILED" : value.Trim();
        return normalized[..Math.Min(normalized.Length, 40)];
    }

    private static string? SafeMessageId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized[..Math.Min(normalized.Length, 200)];
    }

    private static string? SafeErrorCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized[..Math.Min(normalized.Length, 128)];
    }
}

public sealed class MobileReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<FirebasePushOptions> configuredOptions,
    ILogger<MobileReminderBackgroundService> logger) : BackgroundService
{
    private readonly FirebasePushOptions options = configuredOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Clamp(options.ReminderScanSeconds, 5, 3600));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<MobileReminderProcessor>();
                await processor.ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError("DA Secure reminder scan failed ({ExceptionType}).", ex.GetType().Name);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
