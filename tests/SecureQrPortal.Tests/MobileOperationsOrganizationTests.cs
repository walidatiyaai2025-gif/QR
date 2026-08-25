using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using SecureQrPortal.Areas.Admin.Controllers;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Services;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Tests;

public sealed class MobileOperationsOrganizationTests
{
    [Fact]
    public async Task Edit_normalizes_mobile_and_writes_masked_change_audit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var organization = new Organization { NameArabic = "جهة", NameEnglish = "Entity" };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.Edit(new OrganizationAdminEditVm
        {
            Id = organization.Id,
            NameArabic = organization.NameArabic,
            NameEnglish = organization.NameEnglish,
            MobileNumber = "+965 5000 0001",
            IsActive = true
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("96550000001", (await db.Organizations.FindAsync(organization.Id))!.MobileNumber);
        var audit = await db.AuditLogs.SingleAsync(x => x.Action == "MOBILE_ORGANIZATION_NUMBER_CHANGED");
        Assert.Equal("registeredMobile:not-configured->configured-0001", audit.Details);
        Assert.DoesNotContain("96550000001", audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_rejects_mobile_assigned_to_another_organization()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var first = new Organization { NameArabic = "الأولى", NameEnglish = "First" };
        var second = new Organization { NameArabic = "الثانية", NameEnglish = "Second", MobileNumber = "96550000002" };
        db.Organizations.AddRange(first, second);
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.Edit(new OrganizationAdminEditVm
        {
            Id = first.Id,
            NameArabic = first.NameArabic,
            NameEnglish = first.NameEnglish,
            MobileNumber = "50000002",
            IsActive = true
        });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Null((await db.Organizations.FindAsync(first.Id))!.MobileNumber);
    }

    [Fact]
    public async Task Edit_details_expose_masked_device_metadata_without_fcm_material()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var organization = new Organization
        {
            NameArabic = "جهة",
            NameEnglish = "Entity",
            MobileNumber = "96550000003"
        };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        db.MobileDevices.Add(new MobileDevice
        {
            OrganizationId = organization.Id,
            DeviceId = "device-identifier-sensitive",
            FcmTokenProtected = "protected-fcm-secret",
            FcmTokenHash = new string('a', 64),
            Platform = "iOS",
            AppVersion = "1.2.3",
            PushEnabled = true,
            RegisteredAtUtc = DateTime.UtcNow.AddDays(-2),
            LastSeenAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        var result = Assert.IsType<ViewResult>(await CreateController(db).Edit(organization.Id, default));
        var model = Assert.IsType<OrganizationAdminEditVm>(result.Model);
        var device = Assert.Single(model.Devices);

        Assert.Equal("device…tive", device.MaskedDeviceId);
        Assert.True(model.IsMobileReady);
        Assert.DoesNotContain("protected-fcm-secret", string.Join('|', DeviceStringValues(device)), StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(MobileDeviceAdminVm).GetProperties(), p =>
            p.Name.Contains("Fcm", StringComparison.OrdinalIgnoreCase)
            || p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> DeviceStringValues(MobileDeviceAdminVm device)
        => typeof(MobileDeviceAdminVm).GetProperties()
            .Where(x => x.PropertyType == typeof(string))
            .Select(x => (string?)x.GetValue(device) ?? string.Empty);

    private static ApplicationDbContext CreateDb(SqliteConnection connection)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private static OrganizationsController CreateController(ApplicationDbContext db)
    {
        var context = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = context };
        return new OrganizationsController(db, new AuditService(db, accessor), new TestEnvironment(), new UiText())
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SecureQrPortal.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
