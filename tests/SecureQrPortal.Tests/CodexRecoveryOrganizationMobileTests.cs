using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

public sealed class CodexRecoveryOrganizationMobileTests
{
    [Fact]
    public async Task Index_normalizes_human_friendly_kuwait_mobile_search()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        db.Organizations.AddRange(
            new Organization { NameArabic = "الهدف", NameEnglish = "Target", MobileNumber = "96550000001", IsActive = true },
            new Organization { NameArabic = "أخرى", NameEnglish = "Other", MobileNumber = "96550000002", IsActive = true });
        await db.SaveChangesAsync();

        var result = Assert.IsType<ViewResult>(await CreateController(db).Index("+965 5000 0001", default));
        var rows = Assert.IsType<List<OrganizationMobileAdminRowVm>>(result.Model);
        var row = Assert.Single(rows);

        Assert.Equal("Target", row.NameEnglish);
        Assert.Equal("96550000001", row.MobileNumber);
    }

    [Fact]
    public async Task Edit_normalizes_mobile_and_audits_without_mobile_number()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var organization = new Organization { NameArabic = "جهة", NameEnglish = "Entity", IsActive = true };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        var result = await CreateController(db).Edit(new Organization
        {
            Id = organization.Id,
            NameArabic = organization.NameArabic,
            NameEnglish = organization.NameEnglish,
            MobileNumber = "+965 5000 0001",
            IsActive = true
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("96550000001", (await db.Organizations.FindAsync(organization.Id))!.MobileNumber);
        var audit = await db.AuditLogs.SingleAsync(x => x.Action == "ORGANIZATION_MOBILE_CHANGED");
        Assert.Equal("MobileConfigured=True", audit.Details);
        Assert.DoesNotContain("96550000001", audit.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("+965", audit.Details ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_rejects_mobile_assigned_to_another_organization()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var first = new Organization { NameArabic = "الأولى", NameEnglish = "First", IsActive = true };
        var second = new Organization { NameArabic = "الثانية", NameEnglish = "Second", MobileNumber = "96550000002", IsActive = true };
        db.Organizations.AddRange(first, second);
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.Edit(new Organization
        {
            Id = first.Id,
            NameArabic = first.NameArabic,
            NameEnglish = first.NameEnglish,
            MobileNumber = "50000002",
            IsActive = true
        });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(nameof(Organization.MobileNumber)));
        Assert.Null((await db.Organizations.FindAsync(first.Id))!.MobileNumber);
    }

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
