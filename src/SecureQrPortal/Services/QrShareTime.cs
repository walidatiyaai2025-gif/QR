using SecureQrPortal.Models;

namespace SecureQrPortal.Services;

public static class QrShareTime
{
    public static DateTime UtcNow(TimeProvider clock) => clock.GetUtcNow().UtcDateTime;

    // QrShareLink *Utc fields are persisted as UTC clock values. SQLite TEXT and
    // SQL Server datetime2 do not retain DateTime.Kind, so materialized values can
    // be Unspecified even though their ticks represent UTC.
    public static DateTime FromStorage(DateTime storedUtc) => storedUtc.Kind switch
    {
        DateTimeKind.Utc => storedUtc,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(storedUtc, DateTimeKind.Utc),
        DateTimeKind.Local => storedUtc.ToUniversalTime(),
        _ => throw new ArgumentOutOfRangeException(nameof(storedUtc))
    };

    public static DateTime? FromStorage(DateTime? storedUtc) =>
        storedUtc is DateTime value ? FromStorage(value) : null;

    public static DateTimeOffset ToCookieExpiry(DateTime storedUtc) =>
        new(FromStorage(storedUtc));

    public static void NormalizeMaterializedUtc(QrShareLink share)
    {
        share.ExpiresAtUtc = FromStorage(share.ExpiresAtUtc);
        share.FirstOpenedAtUtc = FromStorage(share.FirstOpenedAtUtc);
        share.LastOpenedAtUtc = FromStorage(share.LastOpenedAtUtc);
        share.AccessWindowEndsAtUtc = FromStorage(share.AccessWindowEndsAtUtc);
        share.RevokedAtUtc = FromStorage(share.RevokedAtUtc);
        share.CreatedAtUtc = FromStorage(share.CreatedAtUtc);
    }

    public static bool CanReveal(QrShareLink share, DateTime utcNow) =>
        share.RevokedAtUtc is null &&
        FromStorage(share.ExpiresAtUtc) > utcNow &&
        share.CurrentOpenCount < share.MaxOpenCount;

    public static string BlockReason(QrShareLink share, DateTime utcNow)
    {
        if (share.RevokedAtUtc is not null) return "REVOKED";
        if (FromStorage(share.ExpiresAtUtc) <= utcNow) return "LINK_EXPIRED";
        if (share.CurrentOpenCount >= share.MaxOpenCount) return "REVEAL_LIMIT_REACHED";
        return "REVEAL_SERVICE_REJECTED";
    }
}
