using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;

namespace SecureQrPortal.Tests;

public sealed class MobileBearerAuthenticationTests
{
    [Fact]
    public async Task Revoked_mobile_session_token_is_rejected_by_bearer_handler()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var org = new Organization { NameArabic = "جهة", NameEnglish = "Org", MobileNumber = "96551111111", IsActive = true };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var tokens = new MobileTokenService();
        var rawAccess = tokens.GenerateToken();
        var session = new MobileSession
        {
            SessionId = tokens.GenerateToken(24),
            OrganizationId = org.Id,
            AccessTokenHash = tokens.HashToken(rawAccess),
            RefreshTokenHash = tokens.HashToken(tokens.GenerateToken(48)),
            CreatedAtUtc = DateTime.UtcNow,
            AccessExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
            RefreshExpiresAtUtc = DateTime.UtcNow.AddDays(30)
        };
        db.MobileSessions.Add(session);
        await db.SaveChangesAsync();

        var active = await AuthenticateAsync(db, tokens, rawAccess);
        Assert.True(active.Succeeded);
        Assert.Equal(org.Id.ToString(), active.Principal!.FindFirst(MobileClaimTypes.OrganizationId)!.Value);

        await db.MobileSessions.Where(x => x.Id == session.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, DateTime.UtcNow));
        var revoked = await AuthenticateAsync(db, tokens, rawAccess);
        Assert.False(revoked.Succeeded);
    }

    [Fact]
    public async Task Expired_mobile_session_token_is_rejected_by_bearer_handler()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var org = new Organization { NameArabic = "جهة", NameEnglish = "Org", MobileNumber = "96552222222", IsActive = true };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var tokens = new MobileTokenService();
        var rawAccess = tokens.GenerateToken();
        db.MobileSessions.Add(new MobileSession
        {
            SessionId = tokens.GenerateToken(24),
            OrganizationId = org.Id,
            AccessTokenHash = tokens.HashToken(rawAccess),
            RefreshTokenHash = tokens.HashToken(tokens.GenerateToken(48)),
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
            AccessExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1),
            RefreshExpiresAtUtc = DateTime.UtcNow.AddDays(29)
        });
        await db.SaveChangesAsync();

        Assert.False((await AuthenticateAsync(db, tokens, rawAccess)).Succeeded);
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(ApplicationDbContext db, MobileTokenService tokens, string rawAccess)
    {
        var handler = new MobileBearerAuthenticationHandler(
            new StaticOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            db,
            tokens,
            TimeProvider.System);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {rawAccess}";
        await handler.InitializeAsync(
            new AuthenticationScheme(MobileBearerDefaults.Scheme, MobileBearerDefaults.Scheme, typeof(MobileBearerAuthenticationHandler)),
            context);
        return await handler.AuthenticateAsync();
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T> where T : class
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
