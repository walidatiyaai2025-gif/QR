using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class MobileTenantBoundaryTests
{
    [Fact]
    public async Task Delivery_cannot_expose_secure_page_owned_by_another_organization_even_if_delivery_row_is_inconsistent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var orgA = new Organization { NameArabic = "جهة أ", NameEnglish = "Org A", MobileNumber = "96550001003", IsActive = true };
        var orgB = new Organization { NameArabic = "جهة ب", NameEnglish = "Org B", MobileNumber = "96550001004", IsActive = true };
        db.Organizations.AddRange(orgA, orgB);
        await db.SaveChangesAsync();

        var pageB = new SecurePage
        {
            OrganizationId = orgB.Id,
            Organization = orgB,
            QrReference = "QR-2026-TENANT-" + Guid.NewGuid().ToString("N")[..8],
            PublicTokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            ProtectedPublicToken = "protected-test-token",
            TitleArabic = "محتوى ب",
            TitleEnglish = "B content",
            ContentArabicHtml = "<p>سر ب</p>",
            ContentEnglishHtml = "<p>B secret</p>",
            IsActive = true,
            ValidFromUtc = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            AccessLimitMode = AccessLimitMode.MaximumSuccessfulAccesses,
            MaxAccessCount = 5
        };
        db.SecurePages.Add(pageB);
        await db.SaveChangesAsync();

        var inconsistentDelivery = new MobileDelivery
        {
            OrganizationId = orgA.Id,
            SecurePageId = pageB.Id,
            SecurePage = pageB,
            CreatedAtUtc = DateTime.UtcNow,
            SentAtUtc = DateTime.UtcNow,
            DeliveryStatus = "SENT",
            ReminderEnabled = false,
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };
        db.MobileDeliveries.Add(inconsistentDelivery);
        await db.SaveChangesAsync();

        var tokens = new MobileTokenService();
        var sessions = new MobileSessionService(db, tokens, TimeProvider.System);
        var issued = await sessions.IssueAsync(orgA);
        var session = await db.MobileSessions.AsNoTracking().SingleAsync(x => x.SessionId == issued.SessionId);
        var rawRevealGrant = tokens.GenerateToken();
        db.MobileRevealGrants.Add(new MobileRevealGrant
        {
            TokenHash = tokens.HashToken(rawRevealGrant),
            MobileSessionId = session.Id,
            MobileDeliveryId = inconsistentDelivery.Id,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2)
        });
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext();
        var audit = new AuditService(db, new HttpContextAccessor { HttpContext = http });
        var status = new QrStatusService(TimeProvider.System);
        var access = new SecurePageAccessService(db, null!, status, new DeviceInfoService());
        var encryption = new SecureMessageEncryptionService(
            new EphemeralDataProtectionProvider(),
            new SecureMessageSecuritySettingsService(new AppSettingsService(db)));
        var service = new MobileDeliveryAccessService(db, access, status, tokens, encryption, audit, TimeProvider.System);

        var inbox = await service.GetInboxAsync(orgA.Id, 1, 20);
        Assert.Empty(inbox.Items);

        var details = await service.GetDetailsAsync(orgA.Id, inconsistentDelivery.Id);
        Assert.Equal(MobileDeliveryAccessStatus.NotFound, details.Status);
        Assert.Null(details.Details);

        var reveal = await service.RevealAsync(orgA.Id, session.Id, inconsistentDelivery.Id, rawRevealGrant, http);
        Assert.Equal(MobileDeliveryAccessStatus.NotFound, reveal.Status);
        Assert.Equal(0, (await db.SecurePages.AsNoTracking().SingleAsync(x => x.Id == pageB.Id)).CurrentSuccessfulAccessCount);
    }
}



