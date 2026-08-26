using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class QrShareTests
{
    [Fact]
    public async Task One_time_share_reveals_existing_qr_credentials_once_and_same_request_is_idempotent()
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

        const string existingQrPassword = "ExistingQr#2026";
        var credential = new PageCredential { SecurePageId = page.Id, Username = "recipient" };
        credential.PasswordHash = new PasswordHasher<PageCredential>().HashPassword(credential, existingQrPassword);
        db.PageCredentials.Add(credential);
        await db.SaveChangesAsync();
        page.Credential = credential;

        var keyDir = Path.Combine(Path.GetTempPath(), "qr-share-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyDir);
        try
        {
            var provider = DataProtectionProvider.Create(new DirectoryInfo(keyDir));
            var service = new QrShareService(db, provider);
            var share = await service.CreateAsync(
                page,
                1,
                24,
                15,
                existingQrPassword,
                "Open {ShareUrl} for {QrReference}",
                "admin");
            var raw = service.GetRawToken(share);
            const string requestId = "recipient-browser-request-001";

            var first = await service.RevealAsync(raw, requestId);
            Assert.NotNull(first);
            Assert.Equal("recipient", first!.Share.Username);
            Assert.Equal(existingQrPassword, first.Password);
            Assert.Equal(1, first.Share.CurrentOpenCount);
            Assert.NotNull(first.Share.AccessWindowEndsAtUtc);

            var retry = await service.RevealAsync(raw, requestId);
            Assert.NotNull(retry);
            Assert.Equal(existingQrPassword, retry!.Password);
            Assert.Equal(1, retry.Share.CurrentOpenCount);

            var differentRequest = await service.RevealAsync(raw, "different-browser-request-002");
            Assert.Null(differentRequest);

            var verified = await service.VerifyCredentialAsync(page.Id, first.Share.Username, first.Password);
            Assert.True(verified.Success);
            Assert.NotNull(verified.HardExpiresAtUtc);
            Assert.Equal(DateTimeKind.Utc, verified.HardExpiresAtUtc!.Value.Kind);
            Assert.InRange(
                verified.HardExpiresAtUtc.Value - DateTime.UtcNow,
                TimeSpan.FromMinutes(14),
                TimeSpan.FromMinutes(15));
        }
        finally
        {
            Directory.Delete(keyDir, true);
        }
    }

    [Fact]
    public async Task Sqlite_unspecified_utc_clock_is_not_shifted_when_share_credential_is_verified()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var expectedEnd = DateTime.SpecifyKind(now.AddMinutes(15), DateTimeKind.Unspecified);
        var org = new Organization { NameArabic = "جهة", NameEnglish = "Org", IsActive = true };
        var page = new SecurePage
        {
            Organization = org,
            QrReference = "QR-2026-UTC001",
            PublicTokenHash = new string('C', 64),
            ProtectedPublicToken = "protected",
            TitleArabic = "صفحة",
            TitleEnglish = "Page",
            IsActive = true
        };
        var share = new QrShareLink
        {
            SecurePage = page,
            TokenHash = new string('D', 64),
            ProtectedToken = "protected",
            Username = "utc-user",
            ProtectedPassword = "protected",
            MaxOpenCount = 1,
            CurrentOpenCount = 1,
            SessionDurationMinutes = 15,
            ExpiresAtUtc = DateTime.SpecifyKind(now.AddHours(1), DateTimeKind.Unspecified),
            AccessWindowEndsAtUtc = expectedEnd,
            CreatedAtUtc = DateTime.SpecifyKind(now, DateTimeKind.Unspecified)
        };
        share.PasswordHash = new PasswordHasher<QrShareLink>().HashPassword(share, "Utc#2026");
        db.QrShareLinks.Add(share);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var materialized = await db.QrShareLinks.AsNoTracking().SingleAsync();
        Assert.Equal(DateTimeKind.Unspecified, materialized.AccessWindowEndsAtUtc!.Value.Kind);

        var keyDir = Path.Combine(Path.GetTempPath(), "qr-share-utc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyDir);
        try
        {
            var service = new QrShareService(db, DataProtectionProvider.Create(new DirectoryInfo(keyDir)));
            var verified = await service.VerifyCredentialAsync(page.Id, "utc-user", "Utc#2026");

            Assert.True(verified.Success);
            Assert.NotNull(verified.HardExpiresAtUtc);
            Assert.Equal(DateTimeKind.Utc, verified.HardExpiresAtUtc!.Value.Kind);
            Assert.Equal(expectedEnd.Ticks, verified.HardExpiresAtUtc.Value.Ticks);
            Assert.EndsWith("Z", verified.HardExpiresAtUtc.Value.ToString("O"), StringComparison.Ordinal);
            Assert.InRange(
                verified.HardExpiresAtUtc.Value - now,
                TimeSpan.FromMinutes(14.99),
                TimeSpan.FromMinutes(15.01));
        }
        finally
        {
            Directory.Delete(keyDir, true);
        }
    }

    [Fact]
    public void Utc_normalization_preserves_clock_ticks_for_a_one_hour_link_and_real_expiry()
    {
        var now = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
        var persistedOneHourEnd = DateTime.SpecifyKind(now.AddHours(1), DateTimeKind.Unspecified);
        var persistedExpiredEnd = DateTime.SpecifyKind(now.AddSeconds(-1), DateTimeKind.Unspecified);

        var activeEnd = QrShareUtcClock.AsUtc(persistedOneHourEnd);
        var expiredEnd = QrShareUtcClock.AsUtc(persistedExpiredEnd);

        Assert.Equal(persistedOneHourEnd.Ticks, activeEnd.Ticks);
        Assert.Equal(DateTimeKind.Utc, activeEnd.Kind);
        Assert.Equal(TimeSpan.FromHours(1), activeEnd - now);
        Assert.True(activeEnd > now);
        Assert.True(expiredEnd <= now);
    }

    [Fact]
    public async Task Share_creation_rejects_a_password_that_is_not_the_current_qr_password()
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
            QrReference = "QR-2026-SHARE02",
            PublicTokenHash = new string('B', 64),
            ProtectedPublicToken = "protected",
            TitleArabic = "صفحة",
            TitleEnglish = "Page",
            IsActive = true
        };
        db.SecurePages.Add(page);
        await db.SaveChangesAsync();

        var credential = new PageCredential { SecurePageId = page.Id, Username = "recipient" };
        credential.PasswordHash = new PasswordHasher<PageCredential>().HashPassword(credential, "CorrectQr#2026");
        db.PageCredentials.Add(credential);
        await db.SaveChangesAsync();
        page.Credential = credential;

        var keyDir = Path.Combine(Path.GetTempPath(), "qr-share-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyDir);
        try
        {
            var provider = DataProtectionProvider.Create(new DirectoryInfo(keyDir));
            var service = new QrShareService(db, provider);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(page, 1, 24, 15, "WrongQr#2026", null, "admin"));
            Assert.Contains("incorrect", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(keyDir, true);
        }
    }
}
