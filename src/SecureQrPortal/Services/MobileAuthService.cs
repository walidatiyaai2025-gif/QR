using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;

namespace SecureQrPortal.Services;

public sealed record MobileSessionTokens(
    string AccessToken,
    DateTime AccessExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshExpiresAtUtc,
    long OrganizationId,
    string OrganizationNameArabic,
    string OrganizationNameEnglish,
    string SessionId);

public sealed class MobileSessionService(
    ApplicationDbContext db,
    MobileTokenService tokens,
    TimeProvider timeProvider)
{
    public async Task<MobileSessionTokens> IssueAsync(Organization organization, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var accessToken = tokens.GenerateToken();
        var refreshToken = tokens.GenerateToken(48);
        var session = new MobileSession
        {
            SessionId = tokens.GenerateToken(24),
            OrganizationId = organization.Id,
            AccessTokenHash = tokens.HashToken(accessToken),
            RefreshTokenHash = tokens.HashToken(refreshToken),
            CreatedAtUtc = now,
            AccessExpiresAtUtc = now.AddMinutes(15),
            RefreshExpiresAtUtc = now.AddDays(30)
        };
        db.MobileSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return ToTokens(session, accessToken, refreshToken, organization);
    }

    public async Task<MobileSessionTokens?> RefreshAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length < 32 || refreshToken.Length > 512)
            return null;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var refreshHash = tokens.HashToken(refreshToken);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await db.MobileSessions
            .Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.RefreshTokenHash == refreshHash, ct);

        if (current is null || current.RevokedAtUtc.HasValue || current.RefreshUsedAtUtc.HasValue ||
            current.RefreshExpiresAtUtc <= now || !current.Organization.IsActive)
            return null;

        var newAccessToken = tokens.GenerateToken();
        var newRefreshToken = tokens.GenerateToken(48);
        var replacement = new MobileSession
        {
            SessionId = tokens.GenerateToken(24),
            OrganizationId = current.OrganizationId,
            AccessTokenHash = tokens.HashToken(newAccessToken),
            RefreshTokenHash = tokens.HashToken(newRefreshToken),
            CreatedAtUtc = now,
            AccessExpiresAtUtc = now.AddMinutes(15),
            RefreshExpiresAtUtc = now.AddDays(30)
        };

        var affected = await db.MobileSessions
            .Where(x => x.Id == current.Id && x.RevokedAtUtc == null && x.RefreshUsedAtUtc == null && x.RefreshExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RefreshUsedAtUtc, now)
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.ReplacedBySessionId, replacement.SessionId), ct);
        if (affected != 1)
        {
            await transaction.RollbackAsync(ct);
            return null;
        }

        db.MobileSessions.Add(replacement);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToTokens(replacement, newAccessToken, newRefreshToken, current.Organization);
    }

    public async Task RevokeAsync(long sessionDatabaseId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await db.MobileSessions.Where(x => x.Id == sessionDatabaseId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, now), ct);
    }

    private static MobileSessionTokens ToTokens(MobileSession session, string accessToken, string refreshToken, Organization organization) =>
        new(accessToken, session.AccessExpiresAtUtc, refreshToken, session.RefreshExpiresAtUtc,
            organization.Id, organization.NameArabic, organization.NameEnglish, session.SessionId);
}

public enum MobileOtpRequestStatus
{
    Accepted,
    InvalidMobile,
    Cooldown,
    RateLimited
}

public sealed record MobileOtpRequestOutcome(
    MobileOtpRequestStatus Status,
    string? ChallengeId,
    DateTime? ExpiresAtUtc,
    DateTime? ResendAvailableAtUtc,
    int? RetryAfterSeconds = null);

public enum MobileOtpVerifyStatus
{
    Success,
    Invalid,
    Expired,
    TooManyAttempts
}

public sealed record MobileOtpVerifyOutcome(MobileOtpVerifyStatus Status, MobileSessionTokens? Session = null);

public sealed class MobileOtpThrottle(TimeProvider timeProvider)
{
    private sealed class Bucket
    {
        public readonly object Gate = new();
        public readonly Queue<DateTime> Requests = new();
        public DateTime? CooldownUntilUtc;
    }

    private readonly ConcurrentDictionary<string, Bucket> buckets = new(StringComparer.Ordinal);

    public MobileOtpRequestStatus TryAcquire(string normalizedMobile, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var bucket = buckets.GetOrAdd(normalizedMobile, _ => new Bucket());
        lock (bucket.Gate)
        {
            while (bucket.Requests.Count > 0 && bucket.Requests.Peek() <= now.AddMinutes(-15))
                bucket.Requests.Dequeue();

            if (bucket.CooldownUntilUtc.HasValue && bucket.CooldownUntilUtc.Value > now)
            {
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((bucket.CooldownUntilUtc.Value - now).TotalSeconds));
                return MobileOtpRequestStatus.Cooldown;
            }

            if (bucket.Requests.Count >= 5)
            {
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((bucket.Requests.Peek().AddMinutes(15) - now).TotalSeconds));
                return MobileOtpRequestStatus.RateLimited;
            }

            bucket.Requests.Enqueue(now);
            bucket.CooldownUntilUtc = now.AddSeconds(60);
            return MobileOtpRequestStatus.Accepted;
        }
    }
}

public sealed class MobileOtpService(
    ApplicationDbContext db,
    MobileSecretProtector secrets,
    MobileSessionService sessions,
    MobileOtpThrottle throttle,
    SmsGatewayService sms,
    AuditService audit,
    MobileTokenService tokens,
    TimeProvider timeProvider)
{
    public async Task<MobileOtpRequestOutcome> RequestAsync(string? mobileNumber, CancellationToken ct = default)
    {
        var normalized = MobileNumberNormalizer.NormalizeKuwait(mobileNumber);
        if (normalized is null)
            return new(MobileOtpRequestStatus.InvalidMobile, null, null, null);

        var throttleStatus = throttle.TryAcquire(normalized, out var retryAfterSeconds);
        if (throttleStatus != MobileOtpRequestStatus.Accepted)
            return new(throttleStatus, null, null, null, retryAfterSeconds);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var challengeId = tokens.GenerateToken(24);
        var expiresAt = now.AddMinutes(5);
        var resendAt = now.AddSeconds(60);
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(
            x => x.IsActive && x.MobileNumber == normalized, ct);

        if (organization is null)
        {
            await audit.WriteAsync("MOBILE_OTP_REQUESTED", "MobileAuth", challengeId,
                "Request accepted with no externally disclosed registration result.", ct);
            return new(MobileOtpRequestStatus.Accepted, challengeId, expiresAt, resendAt);
        }

        var recent = await db.MobileOtpChallenges.AsNoTracking()
            .Where(x => x.MobileNumber == normalized && x.ConsumedAtUtc == null && x.RevokedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (recent is not null && recent.ResendAvailableAtUtc > now)
        {
            var retry = Math.Max(1, (int)Math.Ceiling((recent.ResendAvailableAtUtc - now).TotalSeconds));
            return new(MobileOtpRequestStatus.Cooldown, null, null, null, retry);
        }

        await db.MobileOtpChallenges
            .Where(x => x.MobileNumber == normalized && x.ConsumedAtUtc == null && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.RevokedAtUtc, now), ct);

        var material = secrets.CreateOtp(challengeId);
        var challenge = new MobileOtpChallenge
        {
            ChallengeId = challengeId,
            OrganizationId = organization.Id,
            MobileNumber = normalized,
            OtpHash = material.OtpHash,
            ProtectedVerificationKey = material.ProtectedVerificationKey,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            ResendAvailableAtUtc = resendAt,
            MaxAttempts = 5
        };
        db.MobileOtpChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("MOBILE_OTP_REQUESTED", "MobileOtpChallenge", challengeId,
            $"OrganizationId={organization.Id}", ct);

        var providerResult = await sms.SendAsync(normalized,
            $"DA Secure verification code: {material.Otp}. It expires in 5 minutes.", ct);
        challenge.ProviderSucceeded = providerResult.Success;
        challenge.ProviderHttpStatusCode = providerResult.HttpStatusCode;
        challenge.ProviderResultCode = providerResult.Success ? "SUCCESS" : "FAILED";
        if (!providerResult.Success)
            challenge.RevokedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        if (!providerResult.Success)
            await audit.WriteAsync("MOBILE_OTP_PROVIDER_FAILED", "MobileOtpChallenge", challengeId,
                $"OrganizationId={organization.Id};HttpStatus={providerResult.HttpStatusCode?.ToString() ?? "none"}", ct);

        return new(MobileOtpRequestStatus.Accepted, challengeId, expiresAt, resendAt);
    }

    public async Task<MobileOtpVerifyOutcome> VerifyAsync(string? challengeId, string? otp, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(challengeId) || string.IsNullOrWhiteSpace(otp))
            return new(MobileOtpVerifyStatus.Invalid);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var challenge = await db.MobileOtpChallenges.AsNoTracking().Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.ChallengeId == challengeId, ct);
        if (challenge is null || challenge.RevokedAtUtc.HasValue || challenge.ConsumedAtUtc.HasValue)
        {
            await audit.WriteAsync("MOBILE_OTP_FAILED", "MobileOtpChallenge", challengeId, "Invalid or replayed challenge.", ct);
            return new(MobileOtpVerifyStatus.Invalid);
        }
        if (challenge.ExpiresAtUtc <= now)
        {
            await audit.WriteAsync("MOBILE_OTP_FAILED", "MobileOtpChallenge", challengeId, "Expired challenge.", ct);
            return new(MobileOtpVerifyStatus.Expired);
        }
        if (challenge.AttemptCount >= challenge.MaxAttempts)
        {
            await audit.WriteAsync("MOBILE_OTP_FAILED", "MobileOtpChallenge", challengeId, "Attempt limit reached.", ct);
            return new(MobileOtpVerifyStatus.TooManyAttempts);
        }

        if (!secrets.VerifyOtp(challenge.ChallengeId, otp.Trim(), challenge.OtpHash, challenge.ProtectedVerificationKey))
        {
            await db.MobileOtpChallenges
                .Where(x => x.Id == challenge.Id && x.ConsumedAtUtc == null && x.RevokedAtUtc == null && x.AttemptCount < x.MaxAttempts)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.AttemptCount, c => c.AttemptCount + 1), ct);
            var attempts = await db.MobileOtpChallenges.AsNoTracking()
                .Where(x => x.Id == challenge.Id).Select(x => new { x.AttemptCount, x.MaxAttempts }).SingleAsync(ct);
            await audit.WriteAsync("MOBILE_OTP_FAILED", "MobileOtpChallenge", challengeId, "Invalid OTP.", ct);
            return new(attempts.AttemptCount >= attempts.MaxAttempts ? MobileOtpVerifyStatus.TooManyAttempts : MobileOtpVerifyStatus.Invalid);
        }

        var consumed = await db.MobileOtpChallenges
            .Where(x => x.Id == challenge.Id && x.ConsumedAtUtc == null && x.RevokedAtUtc == null &&
                        x.ExpiresAtUtc > now && x.AttemptCount < x.MaxAttempts)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.ConsumedAtUtc, now), ct);
        if (consumed != 1)
        {
            var latest = await db.MobileOtpChallenges.AsNoTracking()
                .Where(x => x.Id == challenge.Id)
                .Select(x => new { x.AttemptCount, x.MaxAttempts, x.ExpiresAtUtc, x.ConsumedAtUtc, x.RevokedAtUtc })
                .SingleAsync(ct);
            await audit.WriteAsync("MOBILE_OTP_FAILED", "MobileOtpChallenge", challengeId, "Replay or concurrent verification denied.", ct);
            if (latest.AttemptCount >= latest.MaxAttempts) return new(MobileOtpVerifyStatus.TooManyAttempts);
            if (latest.ExpiresAtUtc <= now) return new(MobileOtpVerifyStatus.Expired);
            return new(MobileOtpVerifyStatus.Invalid);
        }

        var session = await sessions.IssueAsync(challenge.Organization, ct);
        await audit.WriteAsync("MOBILE_OTP_SUCCESS", "MobileOtpChallenge", challengeId,
            $"OrganizationId={challenge.OrganizationId}", ct);
        await audit.WriteAsync("MOBILE_AUTH_SUCCESS", "MobileSession", session.SessionId,
            $"OrganizationId={challenge.OrganizationId}", ct);
        return new(MobileOtpVerifyStatus.Success, session);
    }
}
