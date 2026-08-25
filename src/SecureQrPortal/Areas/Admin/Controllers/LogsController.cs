using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Administrator")]
public sealed class LogsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Access(DateTime? from, DateTime? to, long? organizationId, long? securePageId, string? eventType, bool? success, string? ip, int page = 1, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var query = FilterAccess(db.AccessLogs.Include(x => x.SecurePage).ThenInclude(x => x!.Organization).AsNoTracking(), from, to, organizationId, securePageId, eventType, success, ip);
        var total = await query.CountAsync(ct);
        var vm = new AccessLogIndexVm
        {
            From = from, To = to, OrganizationId = organizationId, SecurePageId = securePageId, EventType = eventType, Success = success, Ip = ip, Page = page, Total = total,
            Items = await query.OrderByDescending(x => x.TimestampUtc).Skip((page - 1) * 50).Take(50).ToListAsync(ct)
        };
        ViewBag.Organizations = await db.Organizations.AsNoTracking().OrderBy(x => x.NameEnglish).ToListAsync(ct);
        ViewBag.Pages = await db.SecurePages.AsNoTracking().OrderBy(x => x.QrReference).Select(x => new { x.Id, x.QrReference, x.TitleArabic, x.TitleEnglish }).ToListAsync(ct);
        return View(vm);
    }

    public async Task<IActionResult> Audit(int page = 1, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var total = await db.AuditLogs.CountAsync(ct);
        return View(new AuditLogIndexVm { Page = page, Total = total, Items = await db.AuditLogs.AsNoTracking().OrderByDescending(x => x.TimestampUtc).Skip((page - 1) * 50).Take(50).ToListAsync(ct) });
    }

    [HttpGet]
    public async Task<IActionResult> ExportAccess(DateTime? from, DateTime? to, long? organizationId, long? securePageId, string? eventType, bool? success, string? ip, CancellationToken ct)
    {
        var rows = await FilterAccess(db.AccessLogs.Include(x => x.SecurePage).ThenInclude(x => x!.Organization).AsNoTracking(), from, to, organizationId, securePageId, eventType, success, ip)
            .OrderByDescending(x => x.TimestampUtc).Take(100000).ToListAsync(ct);
        var sb = new StringBuilder("Timestamp,Event,Success,QR Reference,Organization,Page,IP,Device,Browser,Country\n");
        foreach (var x in rows)
            sb.AppendLine(string.Join(',', Csv(x.TimestampUtc.ToString("O")), Csv(x.EventType), x.WasSuccessful, Csv(x.SecurePage?.QrReference ?? ""), Csv(x.SecurePage?.Organization?.NameEnglish ?? ""), Csv(x.SecurePage?.TitleEnglish ?? ""), Csv(x.IpAddress ?? ""), Csv(x.DeviceType ?? ""), Csv(x.Browser ?? ""), Csv(x.Country ?? "")));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"access-logs-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static IQueryable<AccessLog> FilterAccess(IQueryable<AccessLog> q, DateTime? from, DateTime? to, long? organizationId, long? securePageId, string? eventType, bool? success, string? ip)
    {
        if (from.HasValue) q = q.Where(x => x.TimestampUtc >= DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc));
        if (to.HasValue) q = q.Where(x => x.TimestampUtc < DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc));
        if (organizationId.HasValue) q = q.Where(x => x.SecurePage != null && x.SecurePage.OrganizationId == organizationId.Value);
        if (securePageId.HasValue) q = q.Where(x => x.SecurePageId == securePageId.Value);
        if (!string.IsNullOrWhiteSpace(eventType)) q = q.Where(x => x.EventType == eventType);
        if (success.HasValue) q = q.Where(x => x.WasSuccessful == success.Value);
        if (!string.IsNullOrWhiteSpace(ip)) q = q.Where(x => x.IpAddress != null && x.IpAddress.Contains(ip));
        return q;
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
