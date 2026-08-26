using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureQrPortal.Data;
using SecureQrPortal.Models;

namespace SecureQrPortal.Tests;

public sealed class AdminRuntimeRegressionTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Empty_admin_surfaces_render_without_runtime_500(string culture)
    {
        await using var factory = new AdminRuntimeRegressionFactory();
        using var client = factory.CreateAdminClient(allowAutoRedirect: true);
        await SetCultureAsync(client, culture);

        var routes = new[]
        {
            "/Admin/Dashboard",
            "/Admin/Organizations",
            "/Admin/SecurePages",
            "/Admin/MobileDelivery/History",
            "/Admin/Logs/Access",
            "/Admin/Logs/Audit",
            "/Admin/Settings/General",
            "/Admin/Settings/Database",
            "/Admin/Settings/Backup",
            "/account/changepassword"
        };

        foreach (var route in routes)
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Populated_admin_surfaces_render_with_nullable_log_relationships(string culture)
    {
        await using var factory = new AdminRuntimeRegressionFactory();
        using var client = factory.CreateAdminClient(allowAutoRedirect: true);
        var (organizationId, pageId) = await SeedOrganizationAndPageAsync(factory, isDemo: false);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AccessLogs.Add(new AccessLog
            {
                SecurePageId = null,
                TimestampUtc = DateTime.UtcNow,
                EventType = "LOGIN_FAILED",
                WasSuccessful = false,
                IpAddress = "127.0.0.1",
                DeviceType = "Regression",
                Browser = "Test"
            });
            db.MobileDeliveries.Add(new MobileDelivery
            {
                OrganizationId = organizationId,
                SecurePageId = pageId,
                CreatedAtUtc = DateTime.UtcNow,
                DeliveryStatus = "CREATED"
            });
            await db.SaveChangesAsync();
        }

        await SetCultureAsync(client, culture);
        foreach (var route in new[] { "/Admin/Dashboard", "/Admin/SecurePages", "/Admin/MobileDelivery/History", "/Admin/Logs/Access" })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Organization_edit_for_deleted_row_returns_not_found()
    {
        await using var factory = new AdminRuntimeRegressionFactory();
        using var client = factory.CreateAdminClient();

        using var response = await client.PostAsync("/Admin/Organizations/Edit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = "999999",
            ["NameArabic"] = "جهة محذوفة",
            ["NameEnglish"] = "Deleted Organization",
            ["IsActive"] = "true"
        }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Organization_delete_with_mobile_dependency_is_rejected_without_500()
    {
        await using var factory = new AdminRuntimeRegressionFactory();
        using var client = factory.CreateAdminClient();
        long organizationId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var organization = NewOrganization("Dependency Organization");
            db.Organizations.Add(organization);
            await db.SaveChangesAsync();
            organizationId = organization.Id;
            db.MobileDevices.Add(new MobileDevice
            {
                OrganizationId = organizationId,
                DeviceId = "regression-device-" + Guid.NewGuid().ToString("N"),
                FcmTokenProtected = "protected-test-token",
                FcmTokenHash = Guid.NewGuid().ToString("N"),
                Platform = "android",
                AppVersion = "1.0-test",
                RegisteredAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var response = await client.PostAsync("/Admin/Organizations/Delete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = organizationId.ToString(),
            ["confirmation"] = "DELETE"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await verifyDb.Organizations.AnyAsync(x => x.Id == organizationId));
        Assert.True(await verifyDb.MobileDevices.AnyAsync(x => x.OrganizationId == organizationId));
    }

    [Fact]
    public async Task Secure_page_edit_for_deleted_row_returns_not_found()
    {
        await using var factory = new AdminRuntimeRegressionFactory();
        using var client = factory.CreateAdminClient();
        var (organizationId, _) = await SeedOrganizationAndPageAsync(factory, isDemo: false);

        using var response = await client.PostAsync("/Admin/SecurePages/Edit", ValidSecurePageForm(999999, organizationId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Secure_page_create_with_deleted_organization_returns_validation_view()
    {
        await using var factory = new AdminRuntimeRegressionFactory();
        using var client = factory.CreateAdminClient();

        using var response = await client.PostAsync("/Admin/SecurePages/Edit", ValidSecurePageForm(0, 999999, includePassword: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.SecurePages.AnyAsync());
    }

    [Fact]
    public async Task Secure_page_create_with_zero_organization_returns_validation_view()
    {
        await using var factory = new AdminRuntimeRegressionFactory();
        using var client = factory.CreateAdminClient();

        using var response = await client.PostAsync("/Admin/SecurePages/Edit", ValidSecurePageForm(0, 0, includePassword: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.SecurePages.AnyAsync());
    }

    [Fact]
    public async Task Restore_backup_without_file_returns_redirect_instead_of_500()
    {
        await using var factory = new AdminRuntimeRegressionFactory();
        using var client = factory.CreateAdminClient();

        using var response = await client.PostAsync("/Admin/Settings/RestoreBackup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["confirmation"] = "RESTORE"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Backup", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Restore_backup_with_malformed_sqlite_file_returns_redirect_instead_of_500()
    {
        await using var factory = new AdminRuntimeRegressionFactory();
        using var client = factory.CreateAdminClient();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("RESTORE"), "confirmation");
        form.Add(new ByteArrayContent(Enumerable.Repeat((byte)'X', 256).ToArray()), "backupFile", "malformed.db");

        using var response = await client.PostAsync("/Admin/Settings/RestoreBackup", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Backup", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Demo_delete_removes_delivery_dependency_before_demo_page()
    {
        await using var factory = new AdminRuntimeRegressionFactory();
        using var client = factory.CreateAdminClient();
        var (organizationId, pageId) = await SeedOrganizationAndPageAsync(factory, isDemo: true);
        long deliveryId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var delivery = new MobileDelivery
            {
                OrganizationId = organizationId,
                SecurePageId = pageId,
                CreatedAtUtc = DateTime.UtcNow,
                DeliveryStatus = "CREATED"
            };
            db.MobileDeliveries.Add(delivery);
            await db.SaveChangesAsync();
            deliveryId = delivery.Id;
        }

        using var response = await client.PostAsync("/Admin/Demo/Delete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["confirmation"] = "DELETE DEMO"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await verifyDb.MobileDeliveries.AnyAsync(x => x.Id == deliveryId));
        Assert.False(await verifyDb.SecurePages.AnyAsync(x => x.Id == pageId));
        Assert.False(await verifyDb.Organizations.AnyAsync(x => x.Id == organizationId));
    }

    [Fact]
    public async Task Demo_delete_preserves_demo_organization_with_registered_device()
    {
        await using var factory = new AdminRuntimeRegressionFactory();
        using var client = factory.CreateAdminClient();
        var (organizationId, pageId) = await SeedOrganizationAndPageAsync(factory, isDemo: true);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.MobileDevices.Add(new MobileDevice
            {
                OrganizationId = organizationId,
                DeviceId = "demo-device-" + Guid.NewGuid().ToString("N"),
                FcmTokenProtected = "protected-demo-token",
                FcmTokenHash = Guid.NewGuid().ToString("N"),
                Platform = "android",
                AppVersion = "1.0-test",
                RegisteredAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var response = await client.PostAsync("/Admin/Demo/Delete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["confirmation"] = "DELETE DEMO"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await verifyDb.SecurePages.AnyAsync(x => x.Id == pageId));
        Assert.True(await verifyDb.Organizations.AnyAsync(x => x.Id == organizationId));
        Assert.True(await verifyDb.MobileDevices.AnyAsync(x => x.OrganizationId == organizationId));
    }

    private static FormUrlEncodedContent ValidSecurePageForm(long id, long organizationId, bool includePassword = false)
    {
        var values = new Dictionary<string, string>
        {
            ["Id"] = id.ToString(),
            ["OrganizationId"] = organizationId.ToString(),
            ["TitleArabic"] = "صفحة اختبار",
            ["TitleEnglish"] = "Regression Page",
            ["ContentArabicHtml"] = "<p>اختبار</p>",
            ["ContentEnglishHtml"] = "<p>Test</p>",
            ["IsActive"] = "true",
            ["AccessLimitMode"] = AccessLimitMode.MaximumSuccessfulAccesses.ToString(),
            ["MaxAccessCount"] = "100",
            ["PageUsername"] = "regression-user"
        };
        if (includePassword) values["PagePassword"] = "Regression!Pass123";
        return new FormUrlEncodedContent(values);
    }

    private static async Task<(long OrganizationId, long PageId)> SeedOrganizationAndPageAsync(AdminRuntimeRegressionFactory factory, bool isDemo)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var organization = NewOrganization(isDemo ? "Demo Regression Organization" : "Regression Organization");
        organization.IsDemo = isDemo;
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        var page = new SecurePage
        {
            OrganizationId = organization.Id,
            QrReference = $"QR-2026-{Guid.NewGuid():N}"[..20],
            PublicTokenHash = Guid.NewGuid().ToString("N"),
            ProtectedPublicToken = "protected-regression-token",
            CurrentTokenCreatedAtUtc = DateTime.UtcNow,
            TitleArabic = "صفحة اختبار",
            TitleEnglish = "Regression Page",
            ContentArabicHtml = "<p>اختبار</p>",
            ContentEnglishHtml = "<p>Test</p>",
            IsActive = true,
            IsDemo = isDemo,
            AccessLimitMode = AccessLimitMode.MaximumSuccessfulAccesses,
            MaxAccessCount = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.SecurePages.Add(page);
        await db.SaveChangesAsync();
        return (organization.Id, page.Id);
    }

    private static Organization NewOrganization(string englishName) => new()
    {
        NameArabic = "جهة اختبار",
        NameEnglish = englishName,
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static async Task SetCultureAsync(HttpClient client, string culture)
    {
        using var response = await client.GetAsync($"/Localization/Switch?culture={culture}&returnUrl=%2FAdmin%2FDashboard");
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect);
    }

    private sealed class AdminRuntimeRegressionFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "secure-qr-admin-runtime-" + Guid.NewGuid().ToString("N"));
        private readonly string _databasePath;

        public AdminRuntimeRegressionFactory()
        {
            Directory.CreateDirectory(_tempDirectory);
            _databasePath = Path.Combine(_tempDirectory, "admin-runtime.db");
        }

        public HttpClient CreateAdminClient(bool allowAutoRedirect = false) => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("SecureQrPortal:DefaultSqliteFile", _databasePath);
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAdminAuthenticationHandler.AuthenticationSchemeName;
                    options.DefaultChallengeScheme = TestAdminAuthenticationHandler.AuthenticationSchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAdminAuthenticationHandler>(TestAdminAuthenticationHandler.AuthenticationSchemeName, _ => { });
                services.Configure<MvcOptions>(options => options.Filters.Add(new IgnoreAntiforgeryTokenAttribute()));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class TestAdminAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string AuthenticationSchemeName = "AdminRuntimeRegressionTestAdmin";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "admin-runtime-regression-test"),
                new Claim(ClaimTypes.Name, "Admin Runtime Regression Test"),
                new Claim(ClaimTypes.Role, "Administrator")
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationSchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, AuthenticationSchemeName)));
        }
    }
}
