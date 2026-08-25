using SecureQrPortal.Models;

namespace SecureQrPortal.Services;

public sealed class QrStatusService(TimeProvider timeProvider)
{
    public QrStatus GetStatus(SecurePage page)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (page.RevokedAtUtc.HasValue) return QrStatus.REVOKED;
        if (!page.IsActive || page.Organization?.IsActive == false) return QrStatus.DISABLED;
        if (page.ValidFromUtc.HasValue && page.ValidFromUtc.Value > now) return QrStatus.NOT_STARTED;
        if (page.ExpiresAtUtc.HasValue && page.ExpiresAtUtc.Value <= now) return QrStatus.EXPIRED;
        if (LimitReached(page)) return QrStatus.LIMIT_REACHED;
        return QrStatus.ACTIVE;
    }

    public static bool LimitReached(SecurePage page)
    {
        if (!page.MaxAccessCount.HasValue) return false;
        return page.AccessLimitMode switch
        {
            AccessLimitMode.MaximumSuccessfulAccesses or AccessLimitMode.ExpiryAndSuccessfulAccesses => page.CurrentSuccessfulAccessCount >= page.MaxAccessCount.Value,
            AccessLimitMode.MaximumQrOpens or AccessLimitMode.ExpiryAndQrOpens => page.CurrentQrOpenCount >= page.MaxAccessCount.Value,
            _ => false
        };
    }

    public static long? RemainingAccesses(SecurePage page)
    {
        if (!page.MaxAccessCount.HasValue) return null;
        var used = page.AccessLimitMode is AccessLimitMode.MaximumQrOpens or AccessLimitMode.ExpiryAndQrOpens
            ? page.CurrentQrOpenCount : page.CurrentSuccessfulAccessCount;
        return Math.Max(0, page.MaxAccessCount.Value - used);
    }
}
