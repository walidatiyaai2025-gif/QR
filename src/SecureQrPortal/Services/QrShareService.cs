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
        string? adminUserId,
        CancellationToken ct = default)
    {
        maxOpenCount = Math.Clamp(maxOpenCount, 1, 100);
        linkLifetimeHours = Math.Clamp(linkLifetimeHours, 1, 24 * 30);
        sessionDurationMinutes = Math.Clamp(sessionDurationMinutes, 1, 24 * 60);

        if (page.Credential is null)
            page.Credential = await db.PageCredentials.SingleOrDefaultAsync(x => x.SecurePageId == page.Id, ct);

        var rawToken = GenerateToken();
        var password = GeneratePassword();
        var username = page.Credential?.Username ?? $"share-{page.QrReference}";
        var now = DateTime.UtcNow;

        var share = new QrShareLink
        {
            SecurePageId = page.Id,
            TokenHash = TokenService.HashToken(rawToken),
            ProtectedToken = _secretProtector.Protect(rawToken),
            Username = username,
            ProtectedPassword = _secretProtector.Protect(password),
            MaxOpenCount = maxOpenCount,
            SessionDurationMinutes = sessionDurationMinutes,
            ExpiresAtUtc = now.AddHours(linkLifetimeHours),
            CreatedAtUtc = now,
            CreatedByAdminId = adminUserId
        };

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

    public async Task<QrShareRevealResult?> RevealAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = TokenService.HashToken(rawToken);
        var now = DateTime.UtcNow;
        var candidate = await db.QrShareLinks.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (candidate is null || candidate.RevokedAtUtc != null || candidate.ExpiresAtUtc <= now || candidate.CurrentOpenCount >= candidate.MaxOpenCount)
            return null;

        var hardEnd = now.AddMinutes(candidate.SessionDurationMinutes);
        var affected = await db.QrShareLinks
            .Where(x => x.Id == candidate.Id && x.RevokedAtUtc == null && x.ExpiresAtUtc > now && x.CurrentOpenCount < x.MaxOpenCount)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CurrentOpenCount, x => x.CurrentOpenCount + 1)
                .SetProperty(x => x.FirstOpenedAtUtc, x => x.FirstOpenedAtUtc ?? now)
                .SetProperty(x => x.LastOpenedAtUtc, now)
                .SetProperty(x => x.AccessWindowEndsAtUtc, hardEnd), ct);

        if (affected == 0) return null;

        var share = await db.QrShareLinks
            .Include(x => x.SecurePage).ThenInclude(x => x.Organization)
            .AsNoTracking()
            .SingleAsync(x => x.Id == candidate.Id, ct);
        return new QrShareRevealResult(share, _secretProtector.Unprotect(share.ProtectedPassword));
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
                return new QrShareCredentialResult(true, share.AccessWindowEndsAtUtc, share.Id);
        }

        return QrShareCredentialResult.Failed;
    }

    public async Task<List<QrShareLink>> ListForPageAsync(long pageId, CancellationToken ct = default) =>
        await db.QrShareLinks.AsNoTracking().Where(x => x.SecurePageId == pageId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);

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

    private static string GenerateToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@$%#?";
        const string all = upper + lower + digits + special;

        var chars = new List<char>
        {
            upper[RandomNumberGenerator.GetInt32(upper.Length)],
            lower[RandomNumberGenerator.GetInt32(lower.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            special[RandomNumberGenerator.GetInt32(special.Length)]
        };
        while (chars.Count < 14) chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);

        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }
}
