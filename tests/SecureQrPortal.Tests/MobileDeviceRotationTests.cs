using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class MobileDeviceRotationTests
{
    [Fact]
    public async Task Same_organization_can_rotate_existing_fcm_token_to_new_device_id()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var organization = new Organization
        {
            NameArabic = "جهة",
            NameEnglish = "Organization",
            MobileNumber = "96550001001",
            IsActive = true
        };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext();
        var audit = new AuditService(db, new HttpContextAccessor { HttpContext = http });
        var tokens = new MobileTokenService();
        var secrets = new MobileSecretProtector(new EphemeralDataProtectionProvider());
        var service = new MobileDeviceService(db, secrets, tokens, audit, TimeProvider.System);

        var first = await service.RegisterAsync(
            organization.Id, "device-old", "shared-fcm-token", "android", "1.0.0", true);
        var rotated = await service.RegisterAsync(
            organization.Id, "device-new", "shared-fcm-token", "android", "1.0.1", true);

        Assert.Equal(MobileDeviceRegistrationStatus.Success, first.Status);
        Assert.Equal(MobileDeviceRegistrationStatus.Success, rotated.Status);

        var rows = await db.MobileDevices.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, rows.Count);

        var retired = Assert.Single(rows, x => x.DeviceId == "device-old");
        Assert.NotNull(retired.DeactivatedAtUtc);
        Assert.False(retired.PushEnabled);
        Assert.Empty(retired.FcmTokenProtected);
        Assert.NotEqual(tokens.HashToken("shared-fcm-token"), retired.FcmTokenHash);

        var active = Assert.Single(rows, x => x.DeviceId == "device-new");
        Assert.Null(active.DeactivatedAtUtc);
        Assert.True(active.PushEnabled);
        Assert.Equal(tokens.HashToken("shared-fcm-token"), active.FcmTokenHash);
        Assert.Equal("shared-fcm-token", secrets.UnprotectFcmToken(active.FcmTokenProtected));
    }
}
