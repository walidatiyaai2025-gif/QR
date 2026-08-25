using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Services;
using SecureQrPortal.Security;
using Microsoft.AspNetCore.DataProtection;

namespace SecureQrPortal.Tests;

public sealed class CounterTests
{
    [Fact]
    public async Task Qr_open_and_successful_access_counters_are_separate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var page = await SeedPageAsync(db, AccessLimitMode.MaximumSuccessfulAccesses, 2);
        var service = Service(db);

        Assert.Equal(QrStatus.ACTIVE, await service.RegisterQrOpenAsync(page, new DefaultHttpContext()));
        var afterOpen = await db.SecurePages.AsNoTracking().SingleAsync(x => x.Id == page.Id);
        Assert.Equal(1, afterOpen.CurrentQrOpenCount);
        Assert.Equal(0, afterOpen.CurrentSuccessfulAccessCount);

        Assert.Equal(QrStatus.ACTIVE, await service.RegisterSuccessfulAccessAsync(afterOpen, new DefaultHttpContext()));
        var afterAccess = await db.SecurePages.AsNoTracking().SingleAsync(x => x.Id == page.Id);
        Assert.Equal(1, afterAccess.CurrentSuccessfulAccessCount);
    }

    [Fact]
    public async Task Failed_login_never_consumes_successful_access_limit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var page = await SeedPageAsync(db, AccessLimitMode.MaximumSuccessfulAccesses, 1);
        page = await db.SecurePages.Include(x => x.Organization).Include(x => x.Credential).SingleAsync(x => x.Id == page.Id);

        Assert.False(await Service(db).VerifyCredentialsAsync(page, page.Credential!.Username, "wrong-password", new DefaultHttpContext()));
        var check = await db.SecurePages.AsNoTracking().SingleAsync(x => x.Id == page.Id);
        Assert.Equal(0, check.CurrentSuccessfulAccessCount);
        Assert.Equal(1, check.CurrentFailedLoginCount);
    }

    [Fact]
    public async Task Atomic_counter_cannot_exceed_limit_under_concurrent_attempts()
    {
        var file = Path.Combine(Path.GetTempPath(), $"secure-qr-counter-{Guid.NewGuid():N}.db");
        var cs = $"Data Source={file};Cache=Shared;Default Timeout=10";
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(cs).Options;
            await using (var seedDb = new ApplicationDbContext(options))
            {
                await seedDb.Database.EnsureCreatedAsync();
                await SeedPageAsync(seedDb, AccessLimitMode.MaximumSuccessfulAccesses, 1);
            }

            async Task<QrStatus> Attempt()
            {
                await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(cs).Options);
                var p = await context.SecurePages.Include(x => x.Organization).SingleAsync();
                return await Service(context).RegisterSuccessfulAccessAsync(p, new DefaultHttpContext());
            }

            var results = await Task.WhenAll(Attempt(), Attempt());
            await using var verify = new ApplicationDbContext(options);
            var final = await verify.SecurePages.AsNoTracking().SingleAsync();
            Assert.Equal(1, final.CurrentSuccessfulAccessCount);
            Assert.Single(results, x => x == QrStatus.ACTIVE);
            Assert.Single(results, x => x == QrStatus.LIMIT_REACHED);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            if (File.Exists(file + "-wal")) File.Delete(file + "-wal");
            if (File.Exists(file + "-shm")) File.Delete(file + "-shm");
        }
    }

    private static SecurePageAccessService Service(ApplicationDbContext db) => new(db, null!, new QrStatusService(TimeProvider.System), new DeviceInfoService());

    private static async Task<SecurePage> SeedPageAsync(ApplicationDbContext db, AccessLimitMode mode, long max)
    {
        var org = new Organization { NameArabic = "جهة", NameEnglish = "Org", IsActive = true };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        var page = new SecurePage
        {
            OrganizationId = org.Id, Organization = org, QrReference = "QR-2026-000001-" + Guid.NewGuid().ToString("N")[..8],
            PublicTokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), ProtectedPublicToken = "test",
            TitleArabic = "اختبار", TitleEnglish = "Test", IsActive = true, ValidFromUtc = DateTime.UtcNow.AddDays(-1), ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
            AccessLimitMode = mode, MaxAccessCount = max
        };
        db.SecurePages.Add(page);
        await db.SaveChangesAsync();
        var credential = new PageCredential { SecurePageId = page.Id, Username = "page-user" };
        credential.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<PageCredential>().HashPassword(credential, "Correct!Pass123");
        db.PageCredentials.Add(credential);
        await db.SaveChangesAsync();
        page.Credential = credential;
        return page;
    }
}
