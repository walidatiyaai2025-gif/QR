using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using SecureQrPortal.Models;
using SecureQrPortal.Security;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class SecurityTests : IDisposable
{
    private readonly string _keys = Path.Combine(Path.GetTempPath(), "sqr-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Public_tokens_are_random_url_safe_and_hashable()
    {
        Directory.CreateDirectory(_keys);
        var service = new TokenService(DataProtectionProvider.Create(new DirectoryInfo(_keys)));
        var first = service.GenerateToken();
        var second = service.GenerateToken();
        Assert.NotEqual(first, second);
        Assert.True(first.Length >= 40);
        Assert.DoesNotContain("+", first);
        Assert.DoesNotContain("/", first);
        Assert.Equal(64, TokenService.HashToken(first).Length);
        Assert.Equal(first, service.Unprotect(service.Protect(first)));
    }

    [Fact]
    public void Page_password_is_not_plaintext_and_verifies_with_identity_hasher()
    {
        var credential = new PageCredential { Username = "secure-user" };
        var hasher = new PasswordHasher<PageCredential>();
        credential.PasswordHash = hasher.HashPassword(credential, "Complex!Pass123");
        Assert.NotEqual("Complex!Pass123", credential.PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(credential, credential.PasswordHash, "Complex!Pass123"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(credential, credential.PasswordHash, "wrong"));
    }

    [Fact]
    public void Html_is_sanitized_server_side()
    {
        var sanitizer = new HtmlContentService();
        var output = sanitizer.Sanitize("<h2>Allowed</h2><script>alert(1)</script><img src=x onerror=alert(2)>");
        Assert.Contains("<h2>Allowed</h2>", output);
        Assert.DoesNotContain("<script", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", output, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_keys)) Directory.Delete(_keys, true);
    }
}
