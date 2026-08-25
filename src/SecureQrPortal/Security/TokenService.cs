using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace SecureQrPortal.Security;

public sealed class TokenService
{
    private readonly IDataProtector _protector;
    public TokenService(IDataProtectionProvider provider) => _protector = provider.CreateProtector("SecureQrPortal.PublicToken.v1");

    public string GenerateToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public string Protect(string token) => _protector.Protect(token);
    public string Unprotect(string protectedToken) => _protector.Unprotect(protectedToken);
    public string Mask(string token) => token.Length <= 12 ? "••••••" : $"{token[..6]}••••••{token[^4..]}";
}
