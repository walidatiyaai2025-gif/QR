using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SecureQrPortal.Tests;

/// <summary>
/// Final release-candidate smoke coverage only. This fixture does not replace or alter
/// production authentication; it supplies an Administrator principal so every major
/// Admin GET route can be rendered through the real MVC pipeline in both UI cultures.
/// </summary>
public sealed class DaSecureFinalAdminSmokeTests
{
    private static readonly string[] MajorAdminRoutes =
    [
        "/Admin/Dashboard",
        "/Admin/Organizations",
        "/Admin/Qr",
        "/Admin/SecurePages",
        "/Admin/MobileDelivery/History",
        "/Admin/Logs/Audit",
        "/Admin/Settings/General"
    ];

    [Theory]
    [InlineData("en", "ltr")]
    [InlineData("ar", "rtl")]
    public async Task Every_major_admin_route_renders_without_http_500_in_both_cultures(
        string culture,
        string direction)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true,
            HandleCookies = true
        });

        var switchResponse = await client.GetAsync(
            $"/Localization/Switch?culture={culture}&returnUrl=%2FAdmin%2FDashboard");
        Assert.True(
            (int)switchResponse.StatusCode < 500,
            $"Localization switch returned {(int)switchResponse.StatusCode} for {culture}.");

        foreach (var route in MajorAdminRoutes)
        {
            using var response = await client.GetAsync(route);
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(
                (int)response.StatusCode < 500,
                $"{culture} {route} returned server error {(int)response.StatusCode}.");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(
                $"<html lang=\"{culture}\" dir=\"{direction}\">",
                body,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"da-secure-final-admin-smoke-{Guid.NewGuid():N}.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("SecureQrPortal:DefaultSqliteFile", dbPath);
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAdminAuthenticationHandler.AuthenticationSchemeName;
                    options.DefaultChallengeScheme = TestAdminAuthenticationHandler.AuthenticationSchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAdminAuthenticationHandler>(
                    TestAdminAuthenticationHandler.AuthenticationSchemeName,
                    _ => { });
            });
        });
    }

    private sealed class TestAdminAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string AuthenticationSchemeName = "DaSecureFinalQaAdmin";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "da-secure-final-qa-admin"),
                new Claim(ClaimTypes.Name, "DA Secure Final QA Admin"),
                new Claim(ClaimTypes.Role, "Administrator")
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationSchemeName));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, AuthenticationSchemeName)));
        }
    }
}
