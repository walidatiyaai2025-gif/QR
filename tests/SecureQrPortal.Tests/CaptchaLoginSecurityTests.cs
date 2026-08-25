using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security.Captcha;

namespace SecureQrPortal.Tests;

public sealed class CaptchaLoginSecurityTests : IDisposable
{
    private const string CaptchaAnswer = "ABC234";
    private const string AdminEmail = "captcha-admin@example.test";
    private const string AdminPassword = "Strong!Pass123";
    private readonly CaptchaWebApplicationFactory _factory = new();

    [Fact]
    public async Task Login_page_contains_bilingual_accessible_captcha_without_answer_leakage()
    {
        using var client = CreateClient();

        using var englishResponse = await client.GetAsync("/account/login?culture=en&ui-culture=en");
        var english = await englishResponse.Content.ReadAsStringAsync();
        var arabic = await client.GetStringAsync("/account/login?culture=ar&ui-culture=ar");
        var decodedArabic = WebUtility.HtmlDecode(arabic);
        var script = await client.GetStringAsync("/js/site.js");

        Assert.Contains("Verification code", english);
        Assert.Contains("Enter the characters shown", english);
        Assert.Contains("Refresh verification code", english);
        Assert.Contains("not case-sensitive", english);
        Assert.Contains("رمز التحقق", decodedArabic);
        Assert.Contains("أدخل الأحرف الظاهرة", decodedArabic);
        Assert.Contains("تحديث رمز التحقق", decodedArabic);
        Assert.Contains("aria-describedby=\"captchaHelp captchaValidation\"", english);
        Assert.DoesNotContain(CaptchaAnswer, english, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CaptchaAnswer, arabic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CaptchaAnswer, script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CaptchaAnswer, string.Join(";", englishResponse.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : []), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CaptchaAnswer=", english, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex("[?&]answer=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), script);
        Assert.Matches(new Regex("src=\"/account/captcha/[A-Za-z0-9_-]+\"", RegexOptions.CultureInvariant), english);
    }

    [Fact]
    public async Task Captcha_image_endpoint_returns_non_cacheable_png()
    {
        using var client = CreateClient();
        var page = await GetLoginPageAsync(client);

        using var response = await client.GetAsync($"/account/captcha/{page.ChallengeId}");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        Assert.DoesNotContain(CaptchaAnswer, System.Text.Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_returns_new_challenge_and_invalidates_the_old_one()
    {
        using var client = CreateClient();
        var page = await GetLoginPageAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/captcha/refresh");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = page.AntiforgeryToken,
            ["challengeId"] = page.ChallengeId
        });

        using var response = await client.SendAsync(request);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newChallengeId = json.GetProperty("challengeId").GetString();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(newChallengeId);
        Assert.NotEqual(page.ChallengeId, newChallengeId);
        Assert.DoesNotContain(CaptchaAnswer, json.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/account/captcha/{page.ChallengeId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/account/captcha/{newChallengeId}")).StatusCode);
        Assert.Contains("CAPTCHA_REFRESHED", await GetAuditActionsAsync());
    }

    [Fact]
    public async Task Wrong_captcha_rejects_even_correct_credentials_and_issues_a_fresh_challenge()
    {
        await _factory.EnsureAdministratorAsync(AdminEmail, AdminPassword);
        using var client = CreateClient();
        var page = await GetLoginPageAsync(client);

        using var response = await PostLoginAsync(client, page, AdminEmail, AdminPassword, "WRONG1");
        var html = await response.Content.ReadAsStringAsync();
        var replacement = ReadInputValue(html, "CaptchaChallengeId");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(page.ChallengeId, replacement);
        Assert.Contains("Unable to sign in", html);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Admin/Dashboard")).StatusCode);
        Assert.Contains("CAPTCHA_FAILED", await GetAuditActionsAsync());
    }

    [Fact]
    public async Task Correct_captcha_with_wrong_password_rejects_and_issues_a_fresh_challenge()
    {
        await _factory.EnsureAdministratorAsync(AdminEmail, AdminPassword);
        using var client = CreateClient();
        var page = await GetLoginPageAsync(client);

        using var response = await PostLoginAsync(client, page, AdminEmail, "Wrong!Pass123", CaptchaAnswer);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Unable to sign in", html);
        Assert.NotEqual(page.ChallengeId, ReadInputValue(html, "CaptchaChallengeId"));
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_receive_the_same_generic_feedback()
    {
        await _factory.EnsureAdministratorAsync(AdminEmail, AdminPassword);
        using var knownClient = CreateClient();
        using var unknownClient = CreateClient();
        var knownPage = await GetLoginPageAsync(knownClient);
        var unknownPage = await GetLoginPageAsync(unknownClient);

        using var knownResponse = await PostLoginAsync(knownClient, knownPage, AdminEmail, "Wrong!Pass123", CaptchaAnswer);
        using var unknownResponse = await PostLoginAsync(unknownClient, unknownPage, "unknown@example.test", "Wrong!Pass123", CaptchaAnswer);
        var knownHtml = WebUtility.HtmlDecode(await knownResponse.Content.ReadAsStringAsync());
        var unknownHtml = WebUtility.HtmlDecode(await unknownResponse.Content.ReadAsStringAsync());
        const string genericMessage = "Unable to sign in. Check your details and verification code, then try again.";

        Assert.Equal(HttpStatusCode.OK, knownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknownResponse.StatusCode);
        Assert.Contains(genericMessage, knownHtml);
        Assert.Contains(genericMessage, unknownHtml);
        Assert.DoesNotContain("user not found", unknownHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account temporarily locked", knownHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Expired_captcha_rejects_login()
    {
        await _factory.EnsureAdministratorAsync(AdminEmail, AdminPassword);
        using var client = CreateClient();
        var page = await GetLoginPageAsync(client);
        _factory.Clock.Advance(TimeSpan.FromMinutes(3));

        using var response = await PostLoginAsync(client, page, AdminEmail, AdminPassword, CaptchaAnswer);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Unable to sign in", html);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Admin/Dashboard")).StatusCode);
        Assert.Contains("CAPTCHA_EXPIRED", await GetAuditActionsAsync());
    }

    [Fact]
    public async Task Consumed_captcha_cannot_be_replayed()
    {
        await _factory.EnsureAdministratorAsync(AdminEmail, AdminPassword);
        using var client = CreateClient();
        var page = await GetLoginPageAsync(client);

        using var first = await PostLoginAsync(client, page, AdminEmail, "Wrong!Pass123", CaptchaAnswer);
        using var replay = await PostLoginAsync(client, page, AdminEmail, AdminPassword, CaptchaAnswer);
        var replayHtml = await replay.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Contains("Unable to sign in", replayHtml);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Admin/Dashboard")).StatusCode);
        Assert.Contains("CAPTCHA_REPLAYED", await GetAuditActionsAsync());
    }

    [Fact]
    public async Task Successful_login_invalidates_challenge_and_creates_identity_session()
    {
        await _factory.EnsureAdministratorAsync(AdminEmail, AdminPassword);
        using var client = CreateClient();
        var page = await GetLoginPageAsync(client);

        using var response = await PostLoginAsync(client, page, AdminEmail, AdminPassword, CaptchaAnswer);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Admin", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/account/captcha/{page.ChallengeId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Dashboard")).StatusCode);
        Assert.Contains("ADMIN_LOGIN_SUCCESS", await GetAuditActionsAsync());
    }

    [Fact]
    public async Task Login_post_requires_antiforgery_token()
    {
        using var client = CreateClient();
        using var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = AdminEmail,
            ["Password"] = AdminPassword,
            ["CaptchaChallengeId"] = "missing",
            ["CaptchaAnswer"] = CaptchaAnswer
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sixth_login_post_within_window_is_rate_limited()
    {
        using var client = CreateClient();
        HttpResponseMessage? response = null;

        for (var attempt = 0; attempt < 6; attempt++)
        {
            response?.Dispose();
            response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>()));
        }

        using (response)
            Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
    }

    [Fact]
    public async Task Valid_captcha_password_failures_still_trigger_identity_lockout()
    {
        await _factory.EnsureAdministratorAsync(AdminEmail, AdminPassword);
        using var client = CreateClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var page = await GetLoginPageAsync(client);
            using var response = await PostLoginAsync(client, page, AdminEmail, "Wrong!Pass123", CaptchaAnswer);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var scope = _factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByEmailAsync(AdminEmail);

        Assert.NotNull(user);
        Assert.True(await manager.IsLockedOutAsync(user));
    }

    [Fact]
    public async Task Audit_records_safe_classifications_without_password_or_captcha_answer()
    {
        await _factory.EnsureAdministratorAsync(AdminEmail, AdminPassword);
        using var client = CreateClient();
        var page = await GetLoginPageAsync(client);

        using var response = await PostLoginAsync(client, page, AdminEmail, "Wrong!Pass123", CaptchaAnswer);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var logs = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .AuditLogs.AsNoTracking().ToListAsync();
        var serialized = JsonSerializer.Serialize(logs);

        Assert.Contains(logs, log => log.Action == "ADMIN_LOGIN_FAILED");
        Assert.DoesNotContain(CaptchaAnswer, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AdminPassword, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Wrong!Pass123", serialized, StringComparison.Ordinal);
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost")
    });

    private static async Task<LoginPage> GetLoginPageAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/account/login?culture=en&ui-culture=en");
        return new LoginPage(
            ReadInputValue(html, "__RequestVerificationToken"),
            ReadInputValue(html, "CaptchaChallengeId"));
    }

    private static Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        LoginPage page,
        string email,
        string password,
        string captchaAnswer) =>
        client.PostAsync("/account/login?culture=en&ui-culture=en", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = page.AntiforgeryToken,
            ["Email"] = email,
            ["Password"] = password,
            ["CaptchaChallengeId"] = page.ChallengeId,
            ["CaptchaAnswer"] = captchaAnswer,
            ["RememberMe"] = "false"
        }));

    private static string ReadInputValue(string html, string name)
    {
        var tag = Regex.Match(
            html,
            $"<input\\b[^>]*\\bname=\\\"{Regex.Escape(name)}\\\"[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(tag.Success, $"Input '{name}' was not found.");
        var value = Regex.Match(tag.Value, "\\bvalue=\\\"([^\\\"]*)\\\"", RegexOptions.IgnoreCase);
        Assert.True(value.Success, $"Input '{name}' did not have a value.");
        return WebUtility.HtmlDecode(value.Groups[1].Value);
    }

    private async Task<string[]> GetAuditActionsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .AuditLogs.AsNoTracking()
            .Select(log => log.Action)
            .ToArrayAsync();
    }

    private sealed record LoginPage(string AntiforgeryToken, string ChallengeId);
}

public sealed class CaptchaWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string CaptchaAnswer = "ABC234";
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "secure-qr-captcha-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;

    public CaptchaWebApplicationFactory()
    {
        Directory.CreateDirectory(_tempDirectory);
        _databasePath = Path.Combine(_tempDirectory, "captcha-tests.db");
    }

    public MutableTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("SecureQrPortal:DefaultSqliteFile", _databasePath);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecureQrPortal:DefaultSqliteFile"] = _databasePath,
                ["SecureQrPortal:DefaultCulture"] = "en"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.RemoveAll<ICaptchaAnswerGenerator>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ICaptchaAnswerGenerator>(new FixedCaptchaAnswerGenerator(CaptchaAnswer));
        });
    }

    public async Task EnsureAdministratorAsync(string email, string password)
    {
        _ = CreateClient();
        using var scope = Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByEmailAsync(email);
        if (user is not null)
            return;

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "CAPTCHA Test Administrator"
        };
        var result = await manager.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        result = await manager.AddToRoleAsync(user, "Administrator");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private sealed class FixedCaptchaAnswerGenerator(string answer) : ICaptchaAnswerGenerator
    {
        public char[] Generate(int length)
        {
            if (answer.Length != length)
                throw new InvalidOperationException("Fixed CAPTCHA answer length mismatch.");
            return answer.ToCharArray();
        }
    }
}
