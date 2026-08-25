using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class QrShareTests
{
    [Fact]
    public async Task One_time_share_reveals_once_and_same_request_is_idempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var org = new Organization { NameArabic = "جهة", NameEnglish = "Org", IsActive = true };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        var page = new SecurePage
        {
            OrganizationId = org.Id,
            Organization = org,
            QrReference = "QR-2026-SHARE01",
            PublicTokenHash = new string('A', 64),
            ProtectedPublicToken = "protected",
            TitleArabic = "صفحة",
            TitleEnglish = "Page",
            IsActive = true,
            ValidFromUtc = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2)
        };
        db.SecurePages.Add(page);
        await db.SaveChangesAsync();
        var credential = new PageCredential { SecurePageId = page.Id, Username = "recipient" };
        credential.PasswordHash = "unused-by-share";
        db.PageCredentials.Add(credential);
        await db.SaveChangesAsync();
        page.Credential = credential;

        var keyDir = Path.Combine(Path.GetTempPath(), "qr-share-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyDir);
        try
        {
            var provider = DataProtectionProvider.Create(new DirectoryInfo(keyDir));
            var service = new QrShareService(db, provider);
            var share = await service.CreateAsync(page, 1, 24, 15, "admin");
            var raw = service.GetRawToken(share);
            const string requestId = "recipient-browser-request-001";

            var first = await service.RevealAsync(raw, requestId);
            Assert.NotNull(first);
            Assert.Equal(1, first!.Share.CurrentOpenCount);
            Assert.NotNull(first.Share.AccessWindowEndsAtUtc);

            var retry = await service.RevealAsync(raw, requestId);
            Assert.NotNull(retry);
            Assert.Equal(first.Password, retry!.Password);
            Assert.Equal(1, retry.Share.CurrentOpenCount);

            var differentRequest = await service.RevealAsync(raw, "different-browser-request-002");
            Assert.Null(differentRequest);

            var verified = await service.VerifyCredentialAsync(page.Id, first.Share.Username, first.Password);
            Assert.True(verified.Success);
            Assert.NotNull(verified.HardExpiresAtUtc);
        }
        finally
        {
            Directory.Delete(keyDir, true);
        }
    }
}
