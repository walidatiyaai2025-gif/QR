using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;

namespace SecureQrPortal.Services;

public enum MobileDeliveryAccessStatus
{
    Success,
    NotFound,
    Expired,
    Revoked,
    Disabled,
    NotStarted,
    LimitReached,
    InvalidCredentials,
    InvalidRevealGrant
}

public sealed record MobileInboxItem(
    long DeliveryId,
    DateTime? SentAtUtc,
    DateTime? ExpiresAtUtc,
    DateTime? FirstRevealedAtUtc,
    long? RemainingReveals,
    string Status);

public sealed record MobileInboxPage(int Page, int PageSize, int TotalCount, IReadOnlyList<MobileInboxItem> Items);

public sealed record MobileDeliveryDetails(
    long DeliveryId,
    DateTime? SentAtUtc,
    DateTime? ExpiresAtUtc,
    DateTime? FirstRevealedAtUtc,
    long? RemainingReveals,
    string Status);

public sealed record MobileAuthenticateOutcome(MobileDeliveryAccessStatus Status, string? RevealToken = null, DateTime? RevealExpiresAtUtc = null);

public sealed record MobileRevealOutcome(
    MobileDeliveryAccessStatus Status,
    string? ContentArabicHtml = null,
    string? ContentEnglishHtml = null,
    DateTime? SentAtUtc = null,
    DateTime? ExpiresAtUtc = null,
    long? RemainingReveals = null,
    DateTime? FirstRevealedAtUtc = null);

public sealed class MobileDeliveryAccessService(
    ApplicationDbContext db,
    SecurePageAccessService access,
    QrStatusService qrStatus,
    MobileTokenService tokens,
    AuditService audit,
    TimeProvider timeProvider)
{
    public async Task<MobileInboxPage> GetInboxAsync(long organizationId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var baseQuery = db.MobileDeliveries.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.SecurePage.OrganizationId == organizationId)
            .Include(x => x.SecurePage).ThenInclude(x => x.Organization)
            .OrderByDescending(x => x.SentAtUtc ?? x.CreatedAtUtc);
        var total = await baseQuery.CountAsync(ct);
        var deliveries = await baseQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = deliveries.Select(x =>
        {
            var state = GetState(x);
            return new MobileInboxItem(x.Id, x.SentAtUtc, EffectiveExpiry(x), x.FirstRevealedAtUtc,
                QrStatusService.RemainingAccesses(x.SecurePage), state.ToString().ToUpperInvariant());
        }).ToList();
        await audit.WriteAsync("MOBILE_INBOX_ACCESSED", "MobileInbox", null,
            $"OrganizationId={organizationId};Page={page};PageSize={pageSize}", ct);
        return new MobileInboxPage(page, pageSize, total, items);
    }

    public async Task<(MobileDeliveryAccessStatus Status, MobileDeliveryDetails? Details)> GetDetailsAsync(
        long organizationId,
        long deliveryId,
        CancellationToken ct = default)
    {
        var delivery = await OwnedDeliveryQuery(organizationId).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == deliveryId, ct);
        if (delivery is null) return (MobileDeliveryAccessStatus.NotFound, null);
        var state = GetState(delivery);
        var details = new MobileDeliveryDetails(delivery.Id, delivery.SentAtUtc, EffectiveExpiry(delivery),
            delivery.FirstRevealedAtUtc, QrStatusService.RemainingAccesses(delivery.SecurePage),
            state.ToString().ToUpperInvariant());
        return (state, details);
    }

    public async Task<MobileAuthenticateOutcome> AuthenticateAsync(
        long organizationId,
        long mobileSessionId,
        long deliveryId,
        string? username,
        string? password,
        HttpContext http,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return new(MobileDeliveryAccessStatus.InvalidCredentials);

        var delivery = await OwnedDeliveryQuery(organizationId)
            .SingleOrDefaultAsync(x => x.Id == deliveryId, ct);
        if (delivery is null) return new(MobileDeliveryAccessStatus.NotFound);
        var state = GetState(delivery);
        if (state != MobileDeliveryAccessStatus.Success) return new(state);

        var credentialResult = await access.VerifyPrimaryCredentialsAsync(
            delivery.SecurePage, username, password, http, ct);
        if (!credentialResult.Success)
        {
            await audit.WriteAsync("SECURE_MESSAGE_AUTH_FAILED", "MobileDelivery", delivery.Id.ToString(),
                $"OrganizationId={organizationId}", ct);
            return new(MobileDeliveryAccessStatus.InvalidCredentials);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var rawGrant = tokens.GenerateToken(32);
        var grant = new MobileRevealGrant
        {
            TokenHash = tokens.HashToken(rawGrant),
            MobileSessionId = mobileSessionId,
            MobileDeliveryId = delivery.Id,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(2)
        };
        db.MobileRevealGrants.Add(grant);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("SECURE_MESSAGE_AUTH_SUCCESS", "MobileDelivery", delivery.Id.ToString(),
            $"OrganizationId={organizationId}", ct);
        return new(MobileDeliveryAccessStatus.Success, rawGrant, grant.ExpiresAtUtc);
    }

    public async Task<MobileRevealOutcome> RevealAsync(
        long organizationId,
        long mobileSessionId,
        long deliveryId,
        string? revealToken,
        HttpContext http,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(revealToken) || revealToken.Length > 512)
            return new(MobileDeliveryAccessStatus.InvalidRevealGrant);

        var delivery = await OwnedDeliveryQuery(organizationId)
            .SingleOrDefaultAsync(x => x.Id == deliveryId, ct);
        if (delivery is null) return new(MobileDeliveryAccessStatus.NotFound);
        var state = GetState(delivery);
        if (state != MobileDeliveryAccessStatus.Success) return new(state);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var firstRevealStopsReminders = !delivery.FirstRevealedAtUtc.HasValue && delivery.ReminderEnabled;
        var grantHash = tokens.HashToken(revealToken.Trim());
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var consumed = await db.MobileRevealGrants
            .Where(x => x.TokenHash == grantHash && x.MobileSessionId == mobileSessionId &&
                        x.MobileDeliveryId == delivery.Id && x.ConsumedAtUtc == null && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(x => x.SetProperty(g => g.ConsumedAtUtc, now), ct);
        if (consumed != 1)
        {
            await transaction.RollbackAsync(ct);
            return new(MobileDeliveryAccessStatus.InvalidRevealGrant);
        }

        var accessState = await access.RegisterSuccessfulAccessAsync(delivery.SecurePage, http, ct: ct);
        if (accessState != QrStatus.ACTIVE)
        {
            await transaction.RollbackAsync(ct);
            return new(MapQrStatus(accessState));
        }

        await db.MobileDeliveries
            .Where(x => x.Id == delivery.Id && x.OrganizationId == organizationId && x.SecurePage.OrganizationId == organizationId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(d => d.FirstRevealedAtUtc, d => d.FirstRevealedAtUtc ?? now)
                .SetProperty(d => d.DeliveryStatus, "REVEALED")
                .SetProperty(d => d.NextReminderAtUtc, (DateTime?)null)
                .SetProperty(d => d.ConcurrencyStamp, Guid.NewGuid().ToString("N")), ct);
        await transaction.CommitAsync(ct);

        var refreshed = await db.MobileDeliveries.AsNoTracking()
            .Where(x => x.Id == delivery.Id && x.OrganizationId == organizationId && x.SecurePage.OrganizationId == organizationId)
            .Include(x => x.SecurePage).ThenInclude(x => x.Organization)
            .SingleAsync(ct);
        await audit.WriteAsync("SECURE_MESSAGE_REVEALED", "MobileDelivery", delivery.Id.ToString(),
            $"OrganizationId={organizationId};SuccessfulAccessCount={refreshed.SecurePage.CurrentSuccessfulAccessCount}", ct);
        if (firstRevealStopsReminders)
        {
            await audit.WriteAsync("MOBILE_REMINDER_STOPPED", "MobileDelivery", delivery.Id.ToString(),
                "Reason=FIRST_SECURE_REVEAL", ct);
        }

        return new MobileRevealOutcome(
            MobileDeliveryAccessStatus.Success,
            refreshed.SecurePage.ContentArabicHtml,
            refreshed.SecurePage.ContentEnglishHtml,
            refreshed.SentAtUtc,
            EffectiveExpiry(refreshed),
            QrStatusService.RemainingAccesses(refreshed.SecurePage),
            refreshed.FirstRevealedAtUtc);
    }

    private IQueryable<MobileDelivery> OwnedDeliveryQuery(long organizationId) =>
        db.MobileDeliveries
            .Where(x => x.OrganizationId == organizationId && x.SecurePage.OrganizationId == organizationId)
            .Include(x => x.SecurePage).ThenInclude(x => x.Organization)
            .Include(x => x.SecurePage).ThenInclude(x => x.Credential);

    private MobileDeliveryAccessStatus GetState(MobileDelivery delivery)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (delivery.RevokedAtUtc.HasValue) return MobileDeliveryAccessStatus.Revoked;
        if (delivery.ExpiresAtUtc.HasValue && delivery.ExpiresAtUtc.Value <= now) return MobileDeliveryAccessStatus.Expired;
        return MapQrStatus(qrStatus.GetStatus(delivery.SecurePage));
    }

    private static DateTime? EffectiveExpiry(MobileDelivery delivery)
    {
        if (!delivery.ExpiresAtUtc.HasValue) return delivery.SecurePage.ExpiresAtUtc;
        if (!delivery.SecurePage.ExpiresAtUtc.HasValue) return delivery.ExpiresAtUtc;
        return delivery.ExpiresAtUtc.Value <= delivery.SecurePage.ExpiresAtUtc.Value
            ? delivery.ExpiresAtUtc : delivery.SecurePage.ExpiresAtUtc;
    }

    private static MobileDeliveryAccessStatus MapQrStatus(QrStatus status) => status switch
    {
        QrStatus.ACTIVE => MobileDeliveryAccessStatus.Success,
        QrStatus.EXPIRED => MobileDeliveryAccessStatus.Expired,
        QrStatus.REVOKED => MobileDeliveryAccessStatus.Revoked,
        QrStatus.DISABLED => MobileDeliveryAccessStatus.Disabled,
        QrStatus.NOT_STARTED => MobileDeliveryAccessStatus.NotStarted,
        QrStatus.LIMIT_REACHED => MobileDeliveryAccessStatus.LimitReached,
        _ => MobileDeliveryAccessStatus.NotFound
    };
}
