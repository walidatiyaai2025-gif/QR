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

public sealed class DashboardMobileDeliveryMvcTests
{
    [Fact]
    public async Task Delivery_history_requires_administrator_authentication()
    {
        await using var factory = CreateFactory(authenticatedAdmin: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/Admin/MobileDelivery/History");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authenticated_admin_send_without_antiforgery_token_is_rejected()
    {
        await using var factory = CreateFactory(authenticatedAdmin: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SecurePageId"] = "1",
            ["ReminderEnabled"] = "false",
            ["ReminderUnit"] = "Minutes"
        });
        var response = await client.PostAsync("/Admin/MobileDelivery/Send", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_history_route_renders_in_english_and_arabic()
    {
        await using var factory = CreateFactory(authenticatedAdmin: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true, HandleCookies = true });

        await client.GetAsync("/Localization/Switch?culture=en&returnUrl=%2FAdmin%2FMobileDelivery%2FHistory");
        using var englishResponse = await client.GetAsync("/Admin/MobileDelivery/History");
        var english = await englishResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, englishResponse.StatusCode);
        Assert.Contains("<html lang=\"en\" dir=\"ltr\">", english, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DA Secure Delivery History", english, StringComparison.Ordinal);

        await client.GetAsync("/Localization/Switch?culture=ar&returnUrl=%2FAdmin%2FMobileDelivery%2FHistory");
        using var arabicResponse = await client.GetAsync("/Admin/MobileDelivery/History");
        var arabic = await arabicResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, arabicResponse.StatusCode);
        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", arabic, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DA Secure", arabic, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool authenticatedAdmin)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"da-secure-dashboard-{Guid.NewGuid():N}.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("SecureQrPortal:DefaultSqliteFile", dbPath);
            if (authenticatedAdmin)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAdminAuthenticationHandler.AuthenticationSchemeName;
                        options.DefaultChallengeScheme = TestAdminAuthenticationHandler.AuthenticationSchemeName;
                    }).AddScheme<AuthenticationSchemeOptions, TestAdminAuthenticationHandler>(TestAdminAuthenticationHandler.AuthenticationSchemeName, _ => { });
                });
            }
        });
    }

    private sealed class TestAdminAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string AuthenticationSchemeName = "DashboardMobileTestAdmin";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "dashboard-mobile-test-admin"),
                new Claim(ClaimTypes.Name, "Dashboard Mobile Test Admin"),
                new Claim(ClaimTypes.Role, "Administrator")
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationSchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, AuthenticationSchemeName)));
        }
    }
}
