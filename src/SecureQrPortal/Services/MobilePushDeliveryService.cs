using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;

namespace SecureQrPortal.Services;

public sealed record MobilePushTarget(
    long MobileDeviceId,
    long OrganizationId,
    string DeviceId,
    string FcmTokenHash,
    string FcmToken);

public sealed class MobilePushDeviceStore(
    ApplicationDbContext db,
    MobileSecretProtector secrets,
    MobileTokenService tokens,
    AuditService audit,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<MobilePushTarget>> GetActiveTargetsAsync(long organizationId, CancellationToken ct = default)
    {
        var devices = await db.MobileDevices.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.DeactivatedAtUtc == null && x.PushEnabled)
            .OrderByDescending(x => x.LastSeenAtUtc)
            .ToListAsync(ct);

        var targets = new List<MobilePushTarget>(devices.Count);
        foreach (var device in devices)
        {
            var rawToken = secrets.UnprotectFcmToken(device.FcmTokenProtected);
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                await DeactivateAsync(device.Id, organizationId, "TOKEN_UNPROTECT_FAILED", ct);
                continue;
            }

            targets.Add(new MobilePushTarget(device.Id, organizationId, device.DeviceId, device.FcmTokenHash, rawToken));
        }
        return targets;
    }

    public async Task DeactivateAsync(long deviceId, long organizationId, string reasonCode, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var retiredHash = tokens.HashToken($"retired:{deviceId}:{tokens.GenerateToken()}");
        var affected = await db.MobileDevices
            .Where(x => x.Id == deviceId && x.OrganizationId == organizationId && x.DeactivatedAtUtc == null)
            .ExecuteUpdateAsync(x => x
                .SetProperty(d => d.PushEnabled, false)
                .SetProperty(d => d.DeactivatedAtUtc, now)
                .SetProperty(d => d.FcmTokenProtected, string.Empty)
                .SetProperty(d => d.FcmTokenHash, retiredHash)
                .SetProperty(d => d.ConcurrencyStamp, Guid.NewGuid().ToString("N")), ct);
        if (affected == 1)
        {
            await audit.WriteAsync("MOBILE_DEVICE_PUSH_DISABLED", "MobileDevice", deviceId.ToString(),
                $"OrganizationId={organizationId};Reason={SafeCode(reasonCode)}", ct);
        }
    }

    private static string SafeCode(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value.Trim();
        return normalized[..Math.Min(normalized.Length, 80)];
    }
}

public sealed class MobilePushAttemptService(
    ApplicationDbContext db,
    IFirebasePushProvider provider,
    MobilePushDeviceStore devices,
    IOptions<FirebasePushOptions> configuredOptions,
    TimeProvider timeProvider)
{
    private readonly FirebasePushOptions options = configuredOptions.Value;

    public async Task<FirebasePushProviderResult> SendWithRetryAsync(
        long deliveryId,
        MobilePushTarget target,
        string kind,
        int sequence,
        string category,
        CancellationToken ct = default)
    {
        var maxRetries = Math.Clamp(options.MaxTransientRetries, 0, 5);
        for (var retry = 0; retry <= maxRetries; retry++)
        {
            var correlation = CorrelationKey(deliveryId, kind, sequence, retry, target.MobileDeviceId);
            var existing = await db.MobilePushAttempts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.CorrelationKey == correlation, ct);
            if (existing is not null)
                return FromPersistedAttempt(existing);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var attempt = new MobilePushAttempt
            {
                MobileDeliveryId = deliveryId,
                MobileDeviceId = target.MobileDeviceId,
                Kind = kind,
                Sequence = sequence,
                RetryNumber = retry,
                CorrelationKey = correlation,
                DeviceId = target.DeviceId,
                FcmTokenHash = target.FcmTokenHash,
                CreatedAtUtc = now,
                Outcome = "PENDING"
            };
            db.MobilePushAttempts.Add(attempt);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.Entry(attempt).State = EntityState.Detached;
                var raced = await db.MobilePushAttempts.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CorrelationKey == correlation, ct);
                if (raced is not null) return FromPersistedAttempt(raced);
                throw;
            }

            var result = await provider.SendAsync(
                target.FcmToken,
                new FirebasePushEnvelope(deliveryId, category),
                ct);

            attempt.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            attempt.Outcome = PersistedOutcome(result.Outcome);
            attempt.ProviderMessageId = SafeMessageId(result.ProviderMessageId);
            attempt.ProviderErrorCode = SafeErrorCode(result.ErrorCode);
            attempt.PermanentFailure = result.PermanentFailure;
            await db.SaveChangesAsync(ct);

            if (result.Outcome == FirebasePushOutcome.InvalidToken)
                await devices.DeactivateAsync(target.MobileDeviceId, target.OrganizationId, result.ErrorCode ?? "INVALID_TOKEN", ct);

            if (!ShouldRetry(result, retry, maxRetries)) return result;

            var delayMs = Math.Clamp(options.RetryBaseMilliseconds, 100, 10_000) * (1 << retry);
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct);
        }

        return new(FirebasePushOutcome.ProviderUnavailable, "PROVIDER_UNAVAILABLE", ErrorCode: "RETRY_EXHAUSTED");
    }

    private static bool ShouldRetry(FirebasePushProviderResult result, int retry, int maxRetries) =>
        result.Retryable && retry < maxRetries && !string.Equals(result.ErrorCode, "QUOTA_EXCEEDED", StringComparison.Ordinal);

    private static FirebasePushProviderResult FromPersistedAttempt(MobilePushAttempt attempt)
    {
        if (attempt.CompletedAtUtc is null || attempt.Outcome == "PENDING")
            return new(FirebasePushOutcome.Indeterminate, "INDETERMINATE", ErrorCode: "ATTEMPT_ALREADY_CLAIMED");

        var outcome = attempt.Outcome switch
        {
            "PROVIDER_ACCEPTED" => FirebasePushOutcome.Accepted,
            "PROVIDER_UNAVAILABLE" => FirebasePushOutcome.ProviderUnavailable,
            "CREDENTIAL_FAILURE" => FirebasePushOutcome.CredentialFailure,
            "INVALID_TOKEN" => FirebasePushOutcome.InvalidToken,
            "INDETERMINATE" => FirebasePushOutcome.Indeterminate,
            _ => FirebasePushOutcome.Failed
        };
        var status = outcome switch
        {
            FirebasePushOutcome.Accepted => "PROVIDER_ACCEPTED",
            FirebasePushOutcome.ProviderUnavailable or FirebasePushOutcome.CredentialFailure => "PROVIDER_UNAVAILABLE",
            FirebasePushOutcome.InvalidToken => "INVALID_TOKEN",
            FirebasePushOutcome.Indeterminate => "INDETERMINATE",
            _ => "SEND_FAILED"
        };
        return new(outcome, status, attempt.ProviderMessageId, attempt.ProviderErrorCode, attempt.PermanentFailure);
    }

    private static string PersistedOutcome(FirebasePushOutcome outcome) => outcome switch
    {
        FirebasePushOutcome.Accepted => "PROVIDER_ACCEPTED",
        FirebasePushOutcome.ProviderUnavailable => "PROVIDER_UNAVAILABLE",
        FirebasePushOutcome.CredentialFailure => "CREDENTIAL_FAILURE",
        FirebasePushOutcome.InvalidToken => "INVALID_TOKEN",
        FirebasePushOutcome.Indeterminate => "INDETERMINATE",
        _ => "SEND_FAILED"
    };

    private static string CorrelationKey(long deliveryId, string kind, int sequence, int retry, long deviceId) =>
        $"{deliveryId}:{kind}:{sequence}:{retry}:{deviceId}";

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

public sealed class FirebaseMobilePushDispatchService(
    ApplicationDbContext db,
    QrStatusService qrStatus,
    MobilePushDeviceStore devices,
    MobilePushAttemptService attempts,
    TimeProvider timeProvider) : IMobilePushDispatchService
{
    public async Task<MobilePushDispatchResult> DispatchAsync(MobilePushDispatchRequest request, CancellationToken ct = default)
    {
        var delivery = await db.MobileDeliveries.AsNoTracking()
            .Include(x => x.Organization)
            .Include(x => x.SecurePage).ThenInclude(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == request.DeliveryId, ct);
        if (delivery is null)
            return Fail("DELIVERY_NOT_FOUND", "SEND_FAILED");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (delivery.OrganizationId != delivery.SecurePage.OrganizationId)
            return Fail("TENANT_MISMATCH", "SEND_FAILED");
        if (!delivery.Organization.IsActive)
            return Fail("ORGANIZATION_INACTIVE", "SEND_FAILED");
        if (delivery.RevokedAtUtc.HasValue)
            return Fail("DELIVERY_REVOKED", "SEND_FAILED");
        if (delivery.ExpiresAtUtc.HasValue && delivery.ExpiresAtUtc.Value <= now)
            return Fail("DELIVERY_EXPIRED", "SEND_FAILED");
        if (qrStatus.GetStatus(delivery.SecurePage) != QrStatus.ACTIVE)
            return Fail("SECURE_PAGE_NOT_ACTIVE", "SEND_FAILED");

        var targets = await devices.GetActiveTargetsAsync(delivery.OrganizationId, ct);
        if (targets.Count == 0)
            return Fail("NO_REGISTERED_DEVICE", "NO_REGISTERED_DEVICE");

        var results = new List<FirebasePushProviderResult>(targets.Count);
        foreach (var target in targets)
        {
            results.Add(await attempts.SendWithRetryAsync(
                delivery.Id,
                target,
                "INITIAL",
                0,
                MobilePushConstants.InitialCategory,
                ct));
        }

        return Aggregate(results);
    }

    internal static MobilePushDispatchResult Aggregate(IReadOnlyList<FirebasePushProviderResult> results)
    {
        var accepted = results.FirstOrDefault(x => x.Accepted);
        if (accepted is not null)
            return new(true, "PROVIDER_ACCEPTED", accepted.ProviderMessageId);

        if (results.Any(x => x.Outcome is FirebasePushOutcome.ProviderUnavailable or FirebasePushOutcome.CredentialFailure))
        {
            var failure = results.First(x => x.Outcome is FirebasePushOutcome.ProviderUnavailable or FirebasePushOutcome.CredentialFailure);
            return Fail(failure.ErrorCode ?? "PROVIDER_UNAVAILABLE", "PROVIDER_UNAVAILABLE");
        }
        if (results.Any(x => x.Outcome == FirebasePushOutcome.Indeterminate))
            return Fail("ATTEMPT_INDETERMINATE", "INDETERMINATE");
        if (results.Count > 0 && results.All(x => x.Outcome == FirebasePushOutcome.InvalidToken))
            return Fail("INVALID_TOKEN", "INVALID_TOKEN");

        var error = results.Select(x => x.ErrorCode).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "SEND_FAILED";
        return Fail(error, "SEND_FAILED");
    }

    private static MobilePushDispatchResult Fail(string errorCode, string providerStatus) =>
        new(false, providerStatus, ErrorCode: errorCode);
}
