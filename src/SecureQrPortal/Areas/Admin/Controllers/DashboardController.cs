using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Services;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Areas.Admin.Controllers;
[Area("Admin"),Authorize(Roles="Administrator")]
public sealed class DashboardController(ApplicationDbContext db,QrStatusService statuses) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var now=DateTime.UtcNow; var start=now.Date; var month=new DateTime(now.Year,now.Month,1,0,0,0,DateTimeKind.Utc);
        var pages=await db.SecurePages.Include(x=>x.Organization).AsNoTracking().ToListAsync(ct);
        var vm=new DashboardVm
        {
            TotalPages=pages.Count,TotalQr=pages.Count,Organizations=await db.Organizations.CountAsync(ct),
            ActivePages=pages.Count(x=>statuses.GetStatus(x)==QrStatus.ACTIVE),ExpiredPages=pages.Count(x=>statuses.GetStatus(x)==QrStatus.EXPIRED),
            DisabledPages=pages.Count(x=>statuses.GetStatus(x)==QrStatus.DISABLED),RevokedQr=pages.Count(x=>statuses.GetStatus(x)==QrStatus.REVOKED),LimitReachedQr=pages.Count(x=>statuses.GetStatus(x)==QrStatus.LIMIT_REACHED),
            ScansToday=await db.AccessLogs.LongCountAsync(x=>x.EventType==nameof(AccessEventType.QR_OPEN)&&x.TimestampUtc>=start,ct),
            ScansMonth=await db.AccessLogs.LongCountAsync(x=>x.EventType==nameof(AccessEventType.QR_OPEN)&&x.TimestampUtc>=month,ct),
            SuccessfulToday=await db.AccessLogs.LongCountAsync(x=>x.EventType==nameof(AccessEventType.PAGE_VIEW)&&x.WasSuccessful&&x.TimestampUtc>=start,ct),
            FailedToday=await db.AccessLogs.LongCountAsync(x=>x.EventType==nameof(AccessEventType.LOGIN_FAILURE)&&x.TimestampUtc>=start,ct),
            RecentActivity=await db.AccessLogs.Include(x=>x.SecurePage).ThenInclude(x=>x!.Organization).AsNoTracking().OrderByDescending(x=>x.TimestampUtc).Take(12).ToListAsync(ct)
        };
        vm.MostUsed=pages.OrderByDescending(x=>x.CurrentQrOpenCount).Take(5).Select(x=>(x.Id,x.QrReference,x.Organization.NameEnglish,x.TitleEnglish,x.CurrentQrOpenCount,x.CurrentSuccessfulAccessCount)).ToList();
        vm.ExpiringSoon=pages.Where(x=>x.ExpiresAtUtc.HasValue&&x.ExpiresAtUtc>now&&x.ExpiresAtUtc<=now.AddDays(30)).OrderBy(x=>x.ExpiresAtUtc).Take(8).Select(x=>(x.Id,x.QrReference,x.Organization.NameEnglish,x.TitleEnglish,x.ExpiresAtUtc!.Value)).ToList();
        return View(vm);
    }
}
