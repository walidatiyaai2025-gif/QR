using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;
using SecureQrPortal.Services;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Administrator")]
public sealed class SecurePagesController(
    ApplicationDbContext db,
    TokenService tokens,
    HtmlContentService html,
    AdminIdentityService admin,
    AuditService audit,
    UiText text) : Controller
{
    public async Task<IActionResult> Index(string? q, string? statusFilter, long? organizationId, int page = 1, int pageSize = 20, string sort = "created_desc", CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var now = DateTime.UtcNow;
        var query = db.SecurePages.Include(p => p.Organization).AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(p => p.QrReference.Contains(q) || p.TitleArabic.Contains(q) || p.TitleEnglish.Contains(q) || p.Organization.NameArabic.Contains(q) || p.Organization.NameEnglish.Contains(q));
        if (organizationId.HasValue) query = query.Where(x => x.OrganizationId == organizationId.Value);
        if (!string.IsNullOrWhiteSpace(statusFilter)) query = ApplyStatusFilter(query, statusFilter, now);

        query = sort switch
        {
            "ref" => query.OrderBy(x => x.QrReference),
            "org" => query.OrderBy(x => x.Organization.NameEnglish),
            "expiry" => query.OrderBy(x => x.ExpiresAtUtc),
            "updated_desc" => query.OrderByDescending(x => x.UpdatedAtUtc),
            _ => query.OrderByDescending(x => x.CreatedAtUtc)
        };

        var total = await query.CountAsync(ct);
        var vm = new SecurePageIndexVm
        {
            Search = q,
            Status = statusFilter,
            OrganizationId = organizationId,
            Page = page,
            PageSize = pageSize,
            Total = total,
            Sort = sort,
            Items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)
        };
        ViewBag.Organizations = await db.Organizations.AsNoTracking().OrderBy(x => x.NameEnglish).ToListAsync(ct);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await Lists(ct);
        return View("Edit", new SecurePageEditVm { ValidFromLocal = DateTime.Now, ExpiresAtLocal = DateTime.Now.AddDays(30) });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(long id, CancellationToken ct)
    {
        var p = await db.SecurePages.Include(x => x.Credential).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        await Lists(ct);
        return View(new SecurePageEditVm
        {
            Id = p.Id, OrganizationId = p.OrganizationId, TitleArabic = p.TitleArabic, TitleEnglish = p.TitleEnglish,
            ContentArabicHtml = p.ContentArabicHtml, ContentEnglishHtml = p.ContentEnglishHtml, IsActive = p.IsActive,
            ValidFromLocal = p.ValidFromUtc?.ToLocalTime(), ExpiresAtLocal = p.ExpiresAtUtc?.ToLocalTime(),
            AccessLimitMode = p.AccessLimitMode, MaxAccessCount = p.MaxAccessCount, PageUsername = p.Credential?.Username ?? "", QrReference = p.QrReference
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(SecurePageEditVm vm, CancellationToken ct)
    {
        if (vm.ExpiresAtLocal.HasValue && vm.ValidFromLocal.HasValue && vm.ExpiresAtLocal <= vm.ValidFromLocal)
            ModelState.AddModelError(nameof(vm.ExpiresAtLocal), text["ValidationExpiryAfterStart"]);
        if ((vm.AccessLimitMode is AccessLimitMode.MaximumSuccessfulAccesses or AccessLimitMode.MaximumQrOpens or AccessLimitMode.ExpiryAndSuccessfulAccesses or AccessLimitMode.ExpiryAndQrOpens) && !vm.MaxAccessCount.HasValue)
            ModelState.AddModelError(nameof(vm.MaxAccessCount), text["ValidationMaxAccessRequired"]);
        if (vm.Id == 0 && string.IsNullOrWhiteSpace(vm.PagePassword))
            ModelState.AddModelError(nameof(vm.PagePassword), text["ValidationPasswordRequired"]);

        if (vm.OrganizationId > 0 && !await db.Organizations.AsNoTracking().AnyAsync(x => x.Id == vm.OrganizationId, ct))
            ModelState.AddModelError(nameof(vm.OrganizationId), "الجهة المحددة لم تعد موجودة / The selected organization no longer exists.");

        if (!ModelState.IsValid)
        {
            ModelState.AddModelError("", text["ValidationCorrectFields"]);
            await Lists(ct);
            return View(vm);
        }

        var userId = admin.CurrentUserId;
        SecurePage p;
        PageCredential cred;
        var creating = vm.Id == 0;
        if (creating)
        {
            var raw = tokens.GenerateToken();
            var now = DateTime.UtcNow;
            p = new SecurePage
            {
                OrganizationId = vm.OrganizationId,
                QrReference = $"PENDING-{Guid.NewGuid():N}"[..32],
                PublicTokenHash = TokenService.HashToken(raw),
                ProtectedPublicToken = tokens.Protect(raw),
                CurrentTokenCreatedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedByAdminId = userId
            };
            db.SecurePages.Add(p);
            await db.SaveChangesAsync(ct);
            p.QrReference = $"QR-{p.CreatedAtUtc.Year}-{p.Id:000000}";
            cred = new PageCredential { SecurePageId = p.Id };
            db.PageCredentials.Add(cred);
        }
        else
        {
            var existing = await db.SecurePages.Include(x => x.Credential).SingleOrDefaultAsync(x => x.Id == vm.Id, ct);
            if (existing is null) return NotFound();
            p = existing;
            cred = p.Credential ?? new PageCredential { SecurePageId = p.Id };
            if (p.Credential is null) db.PageCredentials.Add(cred);
        }

        p.OrganizationId = vm.OrganizationId;
        p.TitleArabic = vm.TitleArabic.Trim();
        p.TitleEnglish = vm.TitleEnglish.Trim();
        p.ContentArabicHtml = html.Sanitize(vm.ContentArabicHtml);
        p.ContentEnglishHtml = html.Sanitize(vm.ContentEnglishHtml);
        p.IsActive = vm.IsActive;
        p.ValidFromUtc = vm.ValidFromLocal?.ToUniversalTime();
        p.ExpiresAtUtc = vm.ExpiresAtLocal?.ToUniversalTime();
        p.AccessLimitMode = vm.AccessLimitMode;
        p.MaxAccessCount = vm.MaxAccessCount;
        p.UpdatedAtUtc = DateTime.UtcNow;
        p.LastModifiedByAdminId = userId;
        p.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        cred.Username = vm.PageUsername.Trim();
        cred.UpdatedAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(vm.PagePassword))
            cred.PasswordHash = new PasswordHasher<PageCredential>().HashPassword(cred, vm.PagePassword);

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(creating ? "SECURE_PAGE_CREATE" : "SECURE_PAGE_EDIT", "SecurePage", p.Id.ToString(), p.QrReference, ct);
        return RedirectToAction("Details", "Qr", new { area = "Admin", id = p.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Preview(long id, CancellationToken ct)
    {
        var p = await db.SecurePages.Include(x => x.Organization).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? NotFound() : View(p);
    }

    [HttpPost]
    public async Task<IActionResult> Duplicate(long id, CancellationToken ct)
    {
        var source = await db.SecurePages.Include(x => x.Credential).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (source is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var raw = tokens.GenerateToken();
        var copy = new SecurePage
        {
            OrganizationId = source.OrganizationId,
            QrReference = $"PENDING-{Guid.NewGuid():N}"[..32],
            PublicTokenHash = TokenService.HashToken(raw),
            ProtectedPublicToken = tokens.Protect(raw),
            CurrentTokenCreatedAtUtc = now,
            TitleArabic = source.TitleArabic + " — نسخة",
            TitleEnglish = source.TitleEnglish + " — Copy",
            ContentArabicHtml = source.ContentArabicHtml,
            ContentEnglishHtml = source.ContentEnglishHtml,
            IsActive = false,
            ValidFromUtc = source.ValidFromUtc,
            ExpiresAtUtc = source.ExpiresAtUtc,
            AccessLimitMode = source.AccessLimitMode,
            MaxAccessCount = source.MaxAccessCount,
            CreatedByAdminId = admin.CurrentUserId,
            LastModifiedByAdminId = admin.CurrentUserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.SecurePages.Add(copy);
        await db.SaveChangesAsync(ct);
        copy.QrReference = $"QR-{now.Year}-{copy.Id:000000}";
        if (source.Credential is not null)
            db.PageCredentials.Add(new PageCredential { SecurePageId = copy.Id, Username = source.Credential.Username, PasswordHash = source.Credential.PasswordHash, UpdatedAtUtc = now });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await audit.WriteAsync("SECURE_PAGE_DUPLICATE", "SecurePage", copy.Id.ToString(), $"Copied from {source.QrReference}; copy starts disabled", ct);
        return RedirectToAction(nameof(Edit), new { id = copy.Id });
    }

    [HttpPost]
    public async Task<IActionResult> Toggle(long id, CancellationToken ct)
    {
        var p = await db.SecurePages.FindAsync([id], ct);
        if (p is null) return NotFound();
        p.IsActive = !p.IsActive;
        p.UpdatedAtUtc = DateTime.UtcNow;
        p.LastModifiedByAdminId = admin.CurrentUserId;
        p.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(p.IsActive ? "PAGE_ENABLE" : "PAGE_DISABLE", "SecurePage", id.ToString(), p.QrReference, ct);
        return RedirectToAction("Details", "Qr", new { area = "Admin", id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(long id, string confirmation, CancellationToken ct)
    {
        if (!string.Equals(confirmation, "DELETE", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = text["ConfirmSecurePageDelete"];
            return RedirectToAction(nameof(Index));
        }
        var p = await db.SecurePages.FindAsync([id], ct);
        if (p is null) return NotFound();
        var reference = p.QrReference;
        db.Remove(p);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("SECURE_PAGE_DELETE", "SecurePage", id.ToString(), reference, ct);
        return RedirectToAction(nameof(Index));
    }

    private async Task Lists(CancellationToken ct)
    {
        var organizations = await db.Organizations.Where(x => x.IsActive).OrderBy(x => x.NameEnglish).ToListAsync(ct);
        var displayProperty = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? nameof(Organization.NameArabic) : nameof(Organization.NameEnglish);
        ViewBag.Organizations = new SelectList(organizations, nameof(Organization.Id), displayProperty);
    }

    private static IQueryable<SecurePage> ApplyStatusFilter(IQueryable<SecurePage> q, string f, DateTime now) => f.ToUpperInvariant() switch
    {
        "REVOKED" => q.Where(x => x.RevokedAtUtc != null),
        "DISABLED" => q.Where(x => x.RevokedAtUtc == null && (!x.IsActive || !x.Organization.IsActive)),
        "NOT_STARTED" => q.Where(x => x.RevokedAtUtc == null && x.IsActive && x.Organization.IsActive && x.ValidFromUtc > now),
        "EXPIRED" => q.Where(x => x.RevokedAtUtc == null && x.IsActive && x.Organization.IsActive && x.ExpiresAtUtc <= now),
        "LIMIT_REACHED" => q.Where(x => x.RevokedAtUtc == null && x.IsActive && x.Organization.IsActive && x.MaxAccessCount != null &&
            (((x.AccessLimitMode == AccessLimitMode.MaximumSuccessfulAccesses || x.AccessLimitMode == AccessLimitMode.ExpiryAndSuccessfulAccesses) && x.CurrentSuccessfulAccessCount >= x.MaxAccessCount) ||
             ((x.AccessLimitMode == AccessLimitMode.MaximumQrOpens || x.AccessLimitMode == AccessLimitMode.ExpiryAndQrOpens) && x.CurrentQrOpenCount >= x.MaxAccessCount))),
        "ACTIVE" => q.Where(x => x.RevokedAtUtc == null && x.IsActive && x.Organization.IsActive && (x.ValidFromUtc == null || x.ValidFromUtc <= now) && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now) &&
            !(x.MaxAccessCount != null && (((x.AccessLimitMode == AccessLimitMode.MaximumSuccessfulAccesses || x.AccessLimitMode == AccessLimitMode.ExpiryAndSuccessfulAccesses) && x.CurrentSuccessfulAccessCount >= x.MaxAccessCount) || ((x.AccessLimitMode == AccessLimitMode.MaximumQrOpens || x.AccessLimitMode == AccessLimitMode.ExpiryAndQrOpens) && x.CurrentQrOpenCount >= x.MaxAccessCount)))),
        _ => q
    };
}
