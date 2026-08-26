using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;

namespace SecureQrPortal.Tests;

public sealed class QrRegistryMvcTests
{
    [Fact]
    public async Task Qr_registry_renders_with_real_organization_linked_secure_page()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"da-secure-qr-registry-{Guid.NewGuid():N}.db");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("SecureQrPortal:DefaultSqliteFile", dbPath);
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(TestAdminAuthenticationHandler.AuthenticationSchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAdminAuthenticationHandler>(TestAdminAuthenticationHandler.AuthenticationSchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultScheme = TestAdminAuthenticationHandler.AuthenticationSchemeName;
                    options.DefaultAuthenticateScheme = TestAdminAuthenticationHandler.AuthenticationSchemeName;
                    options.DefaultChallengeScheme = TestAdminAuthenticationHandler.AuthenticationSchemeName;
                    options.DefaultForbidScheme = TestAdminAuthenticationHandler.AuthenticationSchemeName;
                });
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();

            var organization = new Organization
            {
                NameArabic = "جهة اختبار سجل QR",
                NameEnglish = "QR Registry Test Organization",
                IsActive = true
            };
            db.Organizations.Add(organization);
            await db.SaveChangesAsync();

            var rawToken = tokens.GenerateToken();
            db.SecurePages.Add(new SecurePage
            {
                OrganizationId = organization.Id,
                Organization = organization,
                QrReference = "QR-2026-999991",
                PublicTokenHash = TokenService.HashToken(rawToken),
                ProtectedPublicToken = tokens.Protect(rawToken),
                CurrentTokenCreatedAtUtc = DateTime.UtcNow,
                TitleArabic = "صفحة اختبار سجل QR",
                TitleEnglish = "QR Registry Test Page",
                ContentArabicHtml = "<p>اختبار</p>",
                ContentEnglishHtml = "<p>Test</p>",
                IsActive = true,
                AccessLimitMode = AccessLimitMode.MaximumSuccessfulAccesses,
                MaxAccessCount = 10
            });
            await db.SaveChangesAsync();
            Assert.Equal(1, await db.SecurePages.CountAsync());
        }

        using var cultureSwitch = await client.GetAsync("/Localization/Switch?culture=ar&returnUrl=%2FAdmin%2FQr");
        Assert.Equal(HttpStatusCode.Redirect, cultureSwitch.StatusCode);
        using var response = await client.GetAsync("/Admin/Qr");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("سجل رموز QR", html, StringComparison.Ordinal);
        Assert.Contains("جهة اختبار سجل QR", html, StringComparison.Ordinal);
        Assert.Contains("صفحة اختبار سجل QR", html, StringComparison.Ordinal);
        Assert.DoesNotContain("System.NullReferenceException", html, StringComparison.Ordinal);
    }

    private sealed class TestAdminAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string AuthenticationSchemeName = "QrRegistryTestAdmin";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "qr-registry-test-admin"),
                new Claim(ClaimTypes.Name, "QR Registry Test Admin"),
                new Claim(ClaimTypes.Role, "Administrator")
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationSchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, AuthenticationSchemeName)));
        }
    }
}
