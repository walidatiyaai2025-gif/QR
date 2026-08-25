using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Controllers;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class MobileSessionControllerTests
{
    [Fact]
    public async Task Logout_revokes_authenticated_mobile_session()
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
            MobileNumber = "96550001002",
            IsActive = true
        };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        var tokenService = new MobileTokenService();
        var sessions = new MobileSessionService(db, tokenService, TimeProvider.System);
        var issued = await sessions.IssueAsync(organization);
        var session = await db.MobileSessions.SingleAsync(x => x.SessionId == issued.SessionId);

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(MobileClaimTypes.OrganizationId, organization.Id.ToString()),
                new Claim(MobileClaimTypes.SessionDatabaseId, session.Id.ToString())
            }, MobileBearerDefaults.Scheme))
        };
        var audit = new AuditService(db, new HttpContextAccessor { HttpContext = http });
        var controller = new MobileSessionController(sessions, audit)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        Assert.IsType<NoContentResult>(await controller.Logout(default));

        var revoked = await db.MobileSessions.AsNoTracking().SingleAsync(x => x.Id == session.Id);
        Assert.NotNull(revoked.RevokedAtUtc);
        Assert.Contains(await db.AuditLogs.AsNoTracking().ToListAsync(),
            x => x.Action == "MOBILE_AUTH_LOGOUT" && x.EntityId == session.Id.ToString());
    }
}
