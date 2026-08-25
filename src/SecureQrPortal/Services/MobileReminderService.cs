using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;

namespace SecureQrPortal.Services;

public sealed class MobileReminderService(
    ApplicationDbContext db,
    IMobilePushDispatchService push,
    QrStatusService qrStatus,
    AuditService audit,
    TimeProvider timeProvider)
{
    public async Task<int> ProcessDueAsync(
        int batchSize = 20,
        TimeSpan? leaseDuration = null,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        batchSize = Math.Clamp(batchSize, 1, 100);
        var lease = leaseDuration ?? TimeSpan.FromMinutes(2);
        if (lease < TimeSpan.FromSeconds(30)) lease = TimeSpan.FromSeconds(30);
        if (lease > TimeSpan.FromMinutes(10)) lease = TimeSpan.FromMinutes(10);

        var candidates = await db.MobileDeliveries.AsNoTracking()
            .Where(x => x.ReminderEnabled &&
                        x.NextReminderAtUtc != null && x.NextReminderAtUtc <= now &&
                        x.FirstRevealedAtUtc == null && x.RevokedAtUtc == null)
            .OrderBy(x => x.NextReminderAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => new ReminderCandidate(x.Id, x.ConcurrencyStamp))
            .Take(batchSize)
            .ToListAsync(ct);

        var processed = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryProcessAsync(candidate, now, lease, ct)) processed++;
        }
        return processed;
    }

    private async Task<bool> TryProcessAsync(
        ReminderCandidate candidate,
        DateTime now,
        TimeSpan lease,
        CancellationToken ct)
    {
        var claimStamp = Guid.NewGuid().ToString("N");
        var leaseUntil = now.Add(lease);
        var claimed = await db.MobileDeliveries
            .Where(x => x.Id == candidate.Id &&
                        x.ConcurrencyStamp == candidate.ConcurrencyStamp &&
                        x.ReminderEnabled && x.NextReminderAtUtc != null && x.NextReminderAtUtc <= now &&
                        x.FirstRevealedAtUtc == null && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.NextReminderAtUtc, leaseUntil)
                .SetProperty(x => x.ConcurrencyStamp, claimStamp), ct);
        if (claimed != 1) return false;

        var delivery = await db.MobileDeliveries.AsNoTracking()
            .Include(x => x.Organization)
            .Include(x => x.SecurePage).ThenInclude(x => x.Organization)
            .SingleAsync(x => x.Id == candidate.Id, ct);

        var stopReason = StopReason(delivery, now);
        if (stopReason is not null)
        {
            await StopClaimAsync(delivery.Id, claimStamp, stopReason, ct);
            return true;
        }

        await audit.WriteAsync("MOBILE_REMINDER_SEND_REQUESTED", "MobileDelivery", delivery.Id.ToString(),
            $"OrganizationId={delivery.OrganizationId};ReminderNumber={delivery.ReminderCount + 1}", ct);

        MobilePushDispatchResult provider;
        try
        {
            provider = await push.DispatchAsync(new MobilePushDispatchRequest(delivery.Id), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            provider = new MobilePushDispatchResult(false, "PROVIDER_ERROR", ErrorCode: "PROVIDER_ERROR");
        }

        var next = AddInterval(now, delivery.ReminderInterval!.Value, delivery.ReminderUnit!);
        var nextStamp = Guid.NewGuid().ToString("N");
        var providerStatus = Safe(provider.ProviderStatus, 40, "UNKNOWN");
        var providerMessageId = provider.ProviderAccepted ? SafeNullable(provider.ProviderMessageId, 200) : null;

        if (provider.ProviderAccepted)
        {
            var updated = await db.MobileDeliveries
                .Where(x => x.Id == delivery.Id && x.ConcurrencyStamp == claimStamp &&
                            x.ReminderEnabled && x.FirstRevealedAtUtc == null && x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LastReminderAtUtc, now)
                    .SetProperty(x => x.ReminderCount, x => x.ReminderCount + 1)
                    .SetProperty(x => x.NextReminderAtUtc, next)
                    .SetProperty(x => x.FirebaseStatus, providerStatus)
                    .SetProperty(x => x.FirebaseProviderMessageId, providerMessageId)
                    .SetProperty(x => x.ConcurrencyStamp, nextStamp), ct);
            if (updated == 1)
            {
                await audit.WriteAsync("MOBILE_REMINDER_SEND_ACCEPTED", "MobileDelivery", delivery.Id.ToString(),
                    $"ProviderStatus={providerStatus};NextReminderAtUtc={next:o}", ct);
            }
            else
            {
                await audit.WriteAsync("MOBILE_REMINDER_RESULT_IGNORED_STATE_CHANGED", "MobileDelivery", delivery.Id.ToString(),
                    "Provider accepted after delivery state changed; no future reminder was scheduled.", ct);
            }
            return true;
        }

        var retryUpdated = await db.MobileDeliveries
            .Where(x => x.Id == delivery.Id && x.ConcurrencyStamp == claimStamp &&
                        x.ReminderEnabled && x.FirstRevealedAtUtc == null && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.NextReminderAtUtc, next)
                .SetProperty(x => x.FirebaseStatus, providerStatus)
                .SetProperty(x => x.FirebaseProviderMessageId, (string?)null)
                .SetProperty(x => x.ConcurrencyStamp, nextStamp), ct);
        if (retryUpdated == 1)
        {
            await audit.WriteAsync("MOBILE_REMINDER_SEND_FAILED", "MobileDelivery", delivery.Id.ToString(),
                $"ProviderStatus={providerStatus};ErrorCode={Safe(provider.ErrorCode, 80, "NONE")};NextAttemptAtUtc={next:o}", ct);
        }
        return true;
    }

    private string? StopReason(MobileDelivery delivery, DateTime now)
    {
        if (delivery.FirstRevealedAtUtc.HasValue) return "REVEALED";
        if (delivery.RevokedAtUtc.HasValue) return "REVOKED";
        if (!delivery.ReminderEnabled) return "DISABLED";
        if (delivery.ExpiresAtUtc.HasValue && delivery.ExpiresAtUtc.Value <= now) return "DELIVERY_EXPIRED";
        if (!delivery.Organization.IsActive) return "ORGANIZATION_INACTIVE";
        if (qrStatus.GetStatus(delivery.SecurePage) != QrStatus.ACTIVE) return "SOURCE_NOT_ACTIVE";
        if (!delivery.ReminderInterval.HasValue || delivery.ReminderInterval.Value <= 0) return "INVALID_INTERVAL";
        if (delivery.ReminderUnit is not ("Minutes" or "Hours")) return "INVALID_UNIT";
        return null;
    }

    private async Task StopClaimAsync(long deliveryId, string claimStamp, string reason, CancellationToken ct)
    {
        var updated = await db.MobileDeliveries
            .Where(x => x.Id == deliveryId && x.ConcurrencyStamp == claimStamp)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.NextReminderAtUtc, (DateTime?)null)
                .SetProperty(x => x.ConcurrencyStamp, Guid.NewGuid().ToString("N")), ct);
        if (updated == 1)
            await audit.WriteAsync("MOBILE_REMINDER_STOPPED", "MobileDelivery", deliveryId.ToString(), $"Reason={reason}", ct);
    }

    private static DateTime AddInterval(DateTime value, int interval, string unit) =>
        unit == "Hours" ? value.AddHours(interval) : value.AddMinutes(interval);

    private static string Safe(string? value, int maxLength, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var trimmed = value.Trim();
        return trimmed[..Math.Min(trimmed.Length, maxLength)];
    }

    private static string? SafeNullable(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed[..Math.Min(trimmed.Length, maxLength)];
    }

    private sealed record ReminderCandidate(long Id, string ConcurrencyStamp);
}

public sealed class MobileReminderWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MobileReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = Math.Clamp(configuration.GetValue("MobileReminder:PollSeconds", 30), 10, 300);
        var batchSize = Math.Clamp(configuration.GetValue("MobileReminder:BatchSize", 20), 1, 100);
        var leaseSeconds = Math.Clamp(configuration.GetValue("MobileReminder:LeaseSeconds", 120), 30, 600);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<MobileReminderService>();
                await service.ProcessDueAsync(batchSize, TimeSpan.FromSeconds(leaseSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError("DA Secure reminder worker iteration failed with {ExceptionType}.", ex.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
