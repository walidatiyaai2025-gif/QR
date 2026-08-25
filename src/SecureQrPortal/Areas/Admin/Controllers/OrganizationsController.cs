using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;
using SecureQrPortal.Services;

namespace SecureQrPortal.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Administrator")]
public sealed class OrganizationsController(ApplicationDbContext db, AuditService audit, IWebHostEnvironment env, UiText text) : Controller
{
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var query = db.Organizations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(o => o.NameArabic.Contains(q) || o.NameEnglish.Contains(q));
        return View(await query.OrderBy(o => o.NameEnglish).ToListAsync(ct));
    }

    [HttpGet]
    public IActionResult Create() => View("Edit", new Organization());

    [HttpGet]
    public async Task<IActionResult> Edit(long id, CancellationToken ct)
    {
        var organization = await db.Organizations.FindAsync([id], ct);
        return organization is null ? NotFound() : View(organization);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Organization model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model.NameArabic) || string.IsNullOrWhiteSpace(model.NameEnglish))
            ModelState.AddModelError("", text["ValidationNamesRequired"]);

        string? normalizedMobile = null;
        if (!string.IsNullOrWhiteSpace(model.MobileNumber))
        {
            normalizedMobile = MobileNumberNormalizer.NormalizeKuwait(model.MobileNumber);
            if (normalizedMobile is null)
            {
                ModelState.AddModelError(nameof(Organization.MobileNumber),
                    "رقم الجوال يجب أن يكون رقمًا كويتيًا صالحًا / Enter a valid Kuwait mobile number.");
            }
            else
            {
                model.MobileNumber = normalizedMobile;
                var duplicate = await db.Organizations.AsNoTracking()
                    .AnyAsync(x => x.Id != model.Id && x.MobileNumber == normalizedMobile, ct);
                if (duplicate)
                {
                    ModelState.AddModelError(nameof(Organization.MobileNumber),
                        "رقم الجوال مرتبط بجهة أخرى / This mobile number is already assigned to another organization.");
                }
            }
        }
        else
        {
            model.MobileNumber = null;
        }

        if (!ModelState.IsValid)
        {
            ModelState.AddModelError("", text["ValidationCorrectFields"]);
            return View(model);
        }

        Organization entity;
        if (model.Id == 0)
        {
            entity = new Organization { CreatedAtUtc = DateTime.UtcNow };
            db.Organizations.Add(entity);
        }
        else
        {
            entity = await db.Organizations.FindAsync([model.Id], ct) ?? throw new InvalidOperationException("Organization not found");
        }

        entity.NameArabic = model.NameArabic.Trim();
        entity.NameEnglish = model.NameEnglish.Trim();
        entity.MobileNumber = normalizedMobile;
        entity.IsActive = model.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        // Organization logos are no longer part of product branding. The organization
        // name remains available as contextual text above its QR, while the visual
        // identity is always Al Diwan Al Amiri.
        if (!string.IsNullOrWhiteSpace(entity.LogoPath))
        {
            DeleteOwnedLogo(entity.LogoPath);
            entity.LogoPath = null;
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(model.Id == 0 ? "ORGANIZATION_CREATE" : "ORGANIZATION_EDIT", "Organization", entity.Id.ToString(), entity.NameEnglish, ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(long id, string confirmation, CancellationToken ct)
    {
        if (!string.Equals(confirmation, "DELETE", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = text["ConfirmOrganizationDelete"];
            return RedirectToAction(nameof(Index));
        }

        var organization = await db.Organizations.Include(x => x.SecurePages).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (organization is null) return NotFound();
        if (organization.SecurePages.Count > 0)
        {
            TempData["Error"] = text["OrganizationHasPages"];
            return RedirectToAction(nameof(Index));
        }

        var oldLogo = organization.LogoPath;
        db.Remove(organization);
        await db.SaveChangesAsync(ct);
        DeleteOwnedLogo(oldLogo);
        await audit.WriteAsync("ORGANIZATION_DELETE", "Organization", id.ToString(), organization.NameEnglish, ct);
        return RedirectToAction(nameof(Index));
    }

    private void DeleteOwnedLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/uploads/logos/", StringComparison.OrdinalIgnoreCase)) return;
        var safe = Path.GetFileName(path);
        var full = Path.Combine(env.WebRootPath, "uploads", "logos", safe);
        if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
    }
}
