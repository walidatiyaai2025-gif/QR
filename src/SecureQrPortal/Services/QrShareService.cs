using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;

namespace SecureQrPortal.Services;

public sealed record QrShareCredentialResult(bool Success, DateTime? HardExpiresAtUtc, long? ShareId)
{
    public static readonly QrShareCredentialResult Failed = new(false, null, null);
}

public sealed record QrShareRevealResult(QrShareLink Share, string Password);

public sealed class QrShareService(ApplicationDbContext db, IDataProtectionProvider protection)
{
    private readonly IDataProtector _secretProtector = protection.CreateProtector("SecureQrPortal.QrShare.Secret.v1");

    public async Task<QrShareLink> CreateAsync(
        SecurePage page,
        int maxOpenCount,
        int linkLifetimeHours,
        int sessionDurationMinutes,
        string pagePassword,
        string? messageTemplate,
        string? adminUserId,
        CancellationToken ct = default)
    {
        maxOpenCount = Math.Clamp(maxOpenCount, 1, 100);
        linkLifetimeHours = Math.Clamp(linkLifetimeHours, 1, 24 * 30);
        sessionDurationMinutes = Math.Clamp(sessionDurationMinutes, 1, 24 * 60);

        if (page.Credential is null)
            page.Credential = await db.PageCredentials.SingleOrDefaultAsync(x => x.SecurePageId == page.Id, ct);

        if (page.Credential is null)
            throw new InvalidOperationException("The QR page does not have credentials configured.");
        if (string.IsNullOrWhiteSpace(pagePassword))
            throw new InvalidOperationException("The current QR password is required to create a protected share link.");

        var pageHasher = new PasswordHasher<PageCredential>();
        if (pageHasher.VerifyHashedPassword(page.Credential, page.Credential.PasswordHash, pagePassword) == PasswordVerificationResult.Failed)
            throw new InvalidOperationException("The current QR password is incorrect.");

        var rawToken = GenerateToken();
        var password = pagePassword;
        var username = page.Credential.Username;
        var now = DateTime.UtcNow;

        var share = new QrShareLink
        {
            SecurePageId = page.Id,
            TokenHash = TokenService.HashToken(rawToken),
            ProtectedToken = _secretProtector.Protect(rawToken),
            Username = username,
            ProtectedPassword = _secretProtector.Protect(password),
            MessageTemplate = QrShareMessage.NormalizeTemplate(messageTemplate),
            MaxOpenCount = maxOpenCount,
            SessionDurationMinutes = sessionDurationMinutes,
            ExpiresAtUtc = now.AddHours(linkLifetimeHours),
            CreatedAtUtc = now,
            CreatedByAdminId = adminUserId
        };

        // The protected share reveals the exact same QR username/password configured
        // on the page. A share-specific hash is retained only for the access-window
        // policy and never changes the actual QR password.
        var hasher = new PasswordHasher<QrShareLink>();
        share.PasswordHash = hasher.HashPassword(share, password);
        db.QrShareLinks.Add(share);
        await db.SaveChangesAsync(ct);
        return share;
    }

    public async Task<QrShareLink?> FindByTokenAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = TokenService.HashToken(rawToken);
        return await db.QrShareLinks
            .Include(x => x.SecurePage).ThenInclude(x => x.Organization)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
    }

    public Task<QrShareRevealResult?> RevealAsync(string rawToken, CancellationToken ct = default) =>
        RevealAsync(rawToken, GenerateToken(), ct);

    public async Task<QrShareRevealResult?> RevealAsync(
        string rawToken,
        string revealRequestId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(revealRequestId) || revealRequestId.Length > 200)
            return null;

        var tokenHash = TokenService.HashToken(rawToken);
        var requestHash = TokenService.HashToken(revealRequestId.Trim());
        var now = DateTime.UtcNow;
        var candidate = await db.QrShareLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, ct);

        if (candidate is null || candidate.RevokedAtUtc is not null || candidate.ExpiresAtUtc <= now)
            return null;

        if (string.Equals(candidate.LastRevealRequestHash, requestHash, StringComparison.Ordinal) &&
            candidate.CurrentOpenCount > 0 &&
            candidate.AccessWindowEndsAtUtc is DateTime existingEnd &&
            QrShareUtcClock.AsUtc(existingEnd) > now)
        {
            return await LoadRevealResultAsync(candidate.Id, ct);
        }

        if (candidate.CurrentOpenCount >= candidate.MaxOpenCount)
            return null;

        var hardEnd = now.AddMinutes(candidate.SessionDurationMinutes);
        var affected = await db.QrShareLinks
            .Where(x => x.Id == candidate.Id &&
                        x.RevokedAtUtc == null &&
                        x.ExpiresAtUtc > now &&
                        x.CurrentOpenCount < x.MaxOpenCount)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CurrentOpenCount, x => x.CurrentOpenCount + 1)
                .SetProperty(x => x.FirstOpenedAtUtc, x => x.FirstOpenedAtUtc ?? now)
                .SetProperty(x => x.LastOpenedAtUtc, now)
                .SetProperty(x => x.AccessWindowEndsAtUtc, hardEnd)
                .SetProperty(x => x.LastRevealRequestHash, requestHash), ct);

        if (affected > 0)
            return await LoadRevealResultAsync(candidate.Id, ct);

        var raced = await db.QrShareLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == candidate.Id, ct);
        if (raced is not null &&
            raced.RevokedAtUtc is null &&
            raced.CurrentOpenCount > 0 &&
            raced.AccessWindowEndsAtUtc is DateTime racedEnd &&
            QrShareUtcClock.AsUtc(racedEnd) > DateTime.UtcNow &&
            string.Equals(raced.LastRevealRequestHash, requestHash, StringComparison.Ordinal))
        {
            return await LoadRevealResultAsync(candidate.Id, ct);
        }

        return null;
    }

    private async Task<QrShareRevealResult> LoadRevealResultAsync(long shareId, CancellationToken ct)
    {
        var share = await db.QrShareLinks
            .Include(x => x.SecurePage).ThenInclude(x => x.Organization)
            .AsNoTracking()
            .SingleAsync(x => x.Id == shareId, ct);
        return new QrShareRevealResult(share, GetPassword(share));
    }

    public async Task<QrShareCredentialResult> VerifyCredentialAsync(
        long pageId,
        string username,
        string password,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var candidates = await db.QrShareLinks
            .AsNoTracking()
            .Where(x => x.SecurePageId == pageId &&
                        x.Username == username &&
                        x.RevokedAtUtc == null &&
                        x.AccessWindowEndsAtUtc > now &&
                        x.CurrentOpenCount > 0)
            .OrderByDescending(x => x.LastOpenedAtUtc)
            .Take(10)
            .ToListAsync(ct);

        var hasher = new PasswordHasher<QrShareLink>();
        foreach (var share in candidates)
        {
            if (hasher.VerifyHashedPassword(share, share.PasswordHash, password) != PasswordVerificationResult.Failed)
                return new QrShareCredentialResult(
                    true,
                    QrShareUtcClock.AsUtc(share.AccessWindowEndsAtUtc!.Value),
                    share.Id);
        }

        return QrShareCredentialResult.Failed;
    }

    public async Task<List<QrShareLink>> ListForPageAsync(long pageId, CancellationToken ct = default) =>
        await db.QrShareLinks.AsNoTracking().Where(x => x.SecurePageId == pageId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);

    public async Task<QrShareLink?> GetForPageAsync(long shareId, long pageId, CancellationToken ct = default) =>
        await db.QrShareLinks.SingleOrDefaultAsync(x => x.Id == shareId && x.SecurePageId == pageId, ct);

    public async Task<bool> UpdateMessageAsync(long shareId, long pageId, string? messageTemplate, CancellationToken ct = default)
    {
        var normalized = QrShareMessage.NormalizeTemplate(messageTemplate);
        return await db.QrShareLinks
            .Where(x => x.Id == shareId && x.SecurePageId == pageId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.MessageTemplate, normalized), ct) > 0;
    }

    public async Task<bool> RevokeAsync(long shareId, long pageId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await db.QrShareLinks.Where(x => x.Id == shareId && x.SecurePageId == pageId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct) > 0;
    }

    public async Task<int> RevokeAllForPageAsync(long pageId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await db.QrShareLinks.Where(x => x.SecurePageId == pageId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
    }

    public string GetRawToken(QrShareLink share) => _secretProtector.Unprotect(share.ProtectedToken);

    public string GetPassword(QrShareLink share) => _secretProtector.Unprotect(share.ProtectedPassword);

    private static string GenerateToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
}
