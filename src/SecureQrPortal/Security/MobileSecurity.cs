using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureQrPortal.Data;

namespace SecureQrPortal.Security;

public static class MobileBearerDefaults
{
    public const string Scheme = "DA-Secure-Mobile-Bearer";
}

public static class MobileClaimTypes
{
    public const string OrganizationId = "organization_id";
    public const string SessionDatabaseId = "mobile_session_db_id";
}

public static class MobileClaims
{
    public static bool TryGetOrganizationId(ClaimsPrincipal user, out long organizationId) =>
        long.TryParse(user.FindFirstValue(MobileClaimTypes.OrganizationId), out organizationId);

    public static bool TryGetSessionDatabaseId(ClaimsPrincipal user, out long sessionId) =>
        long.TryParse(user.FindFirstValue(MobileClaimTypes.SessionDatabaseId), out sessionId);
}

public static class MobileNumberNormalizer
{
    public static string? NormalizeKuwait(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var digits = new string(input.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        if (digits.Length == 8) digits = "965" + digits;
        if (digits.Length != 11 || !digits.StartsWith("965", StringComparison.Ordinal)) return null;
        return digits;
    }
}

public sealed class MobileTokenService
{
    public string GenerateToken(int byteLength = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return WebEncoders.Base64UrlEncode(bytes);
    }

    public string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

public sealed record MobileOtpMaterial(string Otp, string OtpHash, string ProtectedVerificationKey);

public sealed class MobileSecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector otpProtector = provider.CreateProtector("DA-Secure-Mobile-OTP-v1");
    private readonly IDataProtector deviceProtector = provider.CreateProtector("DA-Secure-Mobile-FCM-v1");

    public MobileOtpMaterial CreateOtp(string challengeId)
    {
        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var key = RandomNumberGenerator.GetBytes(32);
        var hash = ComputeOtpHash(key, challengeId, otp);
        var protectedKey = Convert.ToBase64String(otpProtector.Protect(key));
        return new MobileOtpMaterial(otp, hash, protectedKey);
    }

    public bool VerifyOtp(string challengeId, string candidate, string storedHash, string protectedVerificationKey)
    {
        if (candidate.Length != 6 || candidate.Any(x => !char.IsDigit(x))) return false;
        try
        {
            var key = otpProtector.Unprotect(Convert.FromBase64String(protectedVerificationKey));
            var actual = Convert.FromHexString(ComputeOtpHash(key, challengeId, candidate));
            var expected = Convert.FromHexString(storedHash);
            return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public string ProtectFcmToken(string token) =>
        Convert.ToBase64String(deviceProtector.Protect(Encoding.UTF8.GetBytes(token)));

    public string? UnprotectFcmToken(string protectedToken)
    {
        try
        {
            return Encoding.UTF8.GetString(deviceProtector.Unprotect(Convert.FromBase64String(protectedToken)));
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string ComputeOtpHash(byte[] key, string challengeId, string otp)
    {
        using var hmac = new HMACSHA256(key);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{challengeId}:{otp}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed class MobileBearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApplicationDbContext db,
    MobileTokenService tokens,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var rawToken = authorization[7..].Trim();
        if (rawToken.Length < 32 || rawToken.Length > 256)
            return AuthenticateResult.Fail("Invalid mobile access token.");

        var tokenHash = tokens.HashToken(rawToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var session = await db.MobileSessions.AsNoTracking()
            .Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.AccessTokenHash == tokenHash);

        if (session is null || session.RevokedAtUtc.HasValue || session.AccessExpiresAtUtc <= now || !session.Organization.IsActive)
            return AuthenticateResult.Fail("Mobile session is expired or revoked.");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, session.SessionId),
            new Claim(MobileClaimTypes.SessionDatabaseId, session.Id.ToString()),
            new Claim(MobileClaimTypes.OrganizationId, session.OrganizationId.ToString())
        };
        var identity = new ClaimsIdentity(claims, MobileBearerDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, MobileBearerDefaults.Scheme));
    }
}
