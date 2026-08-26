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
using SecureQrPortal.Models;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class AdminHotfixLocalizationTests
{
    [Fact]
    public void Mobile_delivery_presentation_localizes_machine_values()
    {
        Assert.Equal("Provider accepted", AdminMobileDeliveryText.DeliveryStatus("PROVIDER_ACCEPTED", false));
        Assert.Equal("تم قبول الإرسال من المزود", AdminMobileDeliveryText.DeliveryStatus("PROVIDER_ACCEPTED", true));
        Assert.Equal("Send failed", AdminMobileDeliveryText.DeliveryStatus("SEND_FAILED", false));
        Assert.Equal("فشل الإرسال", AdminMobileDeliveryText.DeliveryStatus("SEND_FAILED", true));
        Assert.Equal("Active", AdminMobileDeliveryText.SecurePageStatus(QrStatus.ACTIVE, false));
        Assert.Equal("نشط", AdminMobileDeliveryText.SecurePageStatus(QrStatus.ACTIVE, true));
        Assert.Equal("Maximum successful accesses", AdminMobileDeliveryText.AccessLimitMode(AccessLimitMode.MaximumSuccessfulAccesses, false));
        Assert.Equal("الحد الأقصى لعمليات الوصول الناجحة", AdminMobileDeliveryText.AccessLimitMode(AccessLimitMode.MaximumSuccessfulAccesses, true));
        Assert.Equal("Minutes", AdminMobileDeliveryText.ReminderUnit("Minutes", false));
        Assert.Equal("دقائق", AdminMobileDeliveryText.ReminderUnit("Minutes", true));
        Assert.Equal("Unknown delivery status", AdminMobileDeliveryText.DeliveryStatus("NEW_INTERNAL_CODE", false));
        Assert.Equal("حالة إرسال غير معروفة", AdminMobileDeliveryText.DeliveryStatus("NEW_INTERNAL_CODE", true));
    }

    [Fact]
    public void Change_password_presentation_is_bilingual_and_does_not_expose_identity_descriptions()
    {
        Assert.Equal("The current password is incorrect.", AdminAccountText.ChangePasswordError("PasswordMismatch", false));
        Assert.Equal("كلمة المرور الحالية غير صحيحة.", AdminAccountText.ChangePasswordError("PasswordMismatch", true));
        Assert.Equal("Password changed successfully.", AdminAccountText.PasswordChanged(false));
        Assert.Equal("تم تغيير كلمة المرور بنجاح.", AdminAccountText.PasswordChanged(true));
        Assert.Equal("Unable to update the password. Review the requirements and try again.", AdminAccountText.ChangePasswordError("UNEXPECTED_IDENTITY_CODE", false));
        Assert.Equal("تعذر تحديث كلمة المرور. راجع المتطلبات وحاول مرة أخرى.", AdminAccountText.ChangePasswordError("UNEXPECTED_IDENTITY_CODE", true));
    }

    [Fact]
    public async Task Admin_mobile_delivery_history_renders_localized_status_labels()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

        using var englishSwitch = await client.GetAsync("/Localization/Switch?culture=en&returnUrl=%2FAdmin%2FMobileDelivery%2FHistory");
        Assert.Equal(HttpStatusCode.Redirect, englishSwitch.StatusCode);
        using var englishResponse = await client.GetAsync("/Admin/MobileDelivery/History");
        var english = WebUtility.HtmlDecode(await englishResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, englishResponse.StatusCode);
        Assert.Contains("DA Secure Delivery History", english, StringComparison.Ordinal);
        Assert.Contains("Provider accepted", english, StringComparison.Ordinal);
        Assert.Contains("Send failed", english, StringComparison.Ordinal);
        Assert.DoesNotContain(">PROVIDER_ACCEPTED<", english, StringComparison.Ordinal);
        Assert.DoesNotContain(">SEND_FAILED<", english, StringComparison.Ordinal);

        using var arabicSwitch = await client.GetAsync("/Localization/Switch?culture=ar&returnUrl=%2FAdmin%2FMobileDelivery%2FHistory");
        Assert.Equal(HttpStatusCode.Redirect, arabicSwitch.StatusCode);
        using var arabicResponse = await client.GetAsync("/Admin/MobileDelivery/History");
        var arabic = WebUtility.HtmlDecode(await arabicResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, arabicResponse.StatusCode);
        Assert.Contains("تم قبول الإرسال من المزود", arabic, StringComparison.Ordinal);
        Assert.Contains("فشل الإرسال", arabic, StringComparison.Ordinal);
        Assert.DoesNotContain(">PROVIDER_ACCEPTED<", arabic, StringComparison.Ordinal);
        Assert.DoesNotContain(">SEND_FAILED<", arabic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Arabic_admin_branding_help_is_not_english_only()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        using var cultureSwitch = await client.GetAsync("/Localization/Switch?culture=ar&returnUrl=%2FAdmin%2FOrganizations%2FCreate");
        Assert.Equal(HttpStatusCode.Redirect, cultureSwitch.StatusCode);

        using var organizationResponse = await client.GetAsync("/Admin/Organizations/Create");
        var organizationHtml = WebUtility.HtmlDecode(await organizationResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, organizationResponse.StatusCode);
        Assert.Contains("هوية النظام ثابتة للديوان الأميري", organizationHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("System branding is fixed to Al Diwan Al Amiri", organizationHtml, StringComparison.Ordinal);

        using var settingsResponse = await client.GetAsync("/Admin/Settings/General");
        var settingsHtml = WebUtility.HtmlDecode(await settingsResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
        Assert.Contains("هوية التطبيق ثابتة مركزياً", settingsHtml, StringComparison.Ordinal);
        Assert.Contains("هوية النظام الثابتة", settingsHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Application branding is centrally fixed", settingsHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Fixed system identity:", settingsHtml, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"da-secure-admin-hotfix-{Guid.NewGuid():N}.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
    }

    private sealed class TestAdminAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string AuthenticationSchemeName = "AdminHotfixTestAdmin";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "admin-hotfix-test-admin"),
                new Claim(ClaimTypes.Name, "Admin Hotfix Test Admin"),
                new Claim(ClaimTypes.Role, "Administrator")
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationSchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, AuthenticationSchemeName)));
        }
    }
}
