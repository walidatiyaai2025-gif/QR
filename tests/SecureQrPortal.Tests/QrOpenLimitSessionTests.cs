using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class QrOpenLimitSessionTests
{
    [Fact]
    public async Task Authenticated_session_can_view_content_after_consuming_final_qr_open()
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
            QrReference = "QR-2026-LIMIT01",
            PublicTokenHash = new string('B', 64),
            ProtectedPublicToken = "protected",
            TitleArabic = "اختبار",
            TitleEnglish = "Test",
            IsActive = true,
            ValidFromUtc = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            AccessLimitMode = AccessLimitMode.MaximumQrOpens,
            MaxAccessCount = 1,
            CurrentQrOpenCount = 1
        };
        db.SecurePages.Add(page);
        await db.SaveChangesAsync();

        Assert.Equal(QrStatus.LIMIT_REACHED, new QrStatusService(TimeProvider.System).GetStatus(page));
        var service = new SecurePageAccessService(db, null!, new QrStatusService(TimeProvider.System), new DeviceInfoService());
        var result = await service.RegisterSuccessfulAccessAsync(page, new DefaultHttpContext(), allowQrOpenLimitSession: true);

        Assert.Equal(QrStatus.LIMIT_REACHED, result);
        var saved = await db.SecurePages.AsNoTracking().SingleAsync(x => x.Id == page.Id);
        Assert.Equal(1, saved.CurrentSuccessfulAccessCount);
    }
}
