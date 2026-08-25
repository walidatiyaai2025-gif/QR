using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;
using SecureQrPortal.Services;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Administrator")]
public sealed class OrganizationsController(ApplicationDbContext db, AuditService audit, IWebHostEnvironment env, UiText text) : Controller
{
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var query = db.Organizations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            var normalizedMobile = MobileNumberNormalizer.NormalizeKuwait(q);
            query = query.Where(o => o.NameArabic.Contains(q) || o.NameEnglish.Contains(q)
                || (normalizedMobile != null && o.MobileNumber == normalizedMobile));
        }

        var organizations = await query.OrderBy(o => o.NameEnglish)
            .Select(o => new
            {
                o.Id,
                o.NameArabic,
                o.NameEnglish,
                o.MobileNumber,
                o.IsActive,
                o.IsDemo,
                o.CreatedAtUtc
            })
            .ToListAsync(ct);
        var organizationIds = organizations.Select(x => x.Id).ToArray();
        var devices = await db.MobileDevices.AsNoTracking()
            .Where(x => organizationIds.Contains(x.OrganizationId) && x.DeactivatedAtUtc == null)
            .Select(x => new { x.OrganizationId, x.PushEnabled, x.LastSeenAtUtc })
            .ToListAsync(ct);
        var devicesByOrganization = devices.ToLookup(x => x.OrganizationId);

        return View(organizations.Select(o =>
        {
            var organizationDevices = devicesByOrganization[o.Id];
            return new OrganizationAdminListItemVm
            {
                Id = o.Id,
                NameArabic = o.NameArabic,
                NameEnglish = o.NameEnglish,
                MobileNumber = FormatMobileNumber(o.MobileNumber),
                IsActive = o.IsActive,
                IsDemo = o.IsDemo,
                CreatedAtUtc = o.CreatedAtUtc,
                ActiveDeviceCount = organizationDevices.Count(),
                HasPushDevice = organizationDevices.Any(x => x.PushEnabled),
                LastSeenAtUtc = organizationDevices.Select(x => (DateTime?)x.LastSeenAtUtc).Max()
            };
        }).ToList());
    }

    [HttpGet]
    public IActionResult Create() => View("Edit", new OrganizationAdminEditVm());

    [HttpGet]
    public async Task<IActionResult> Edit(long id, CancellationToken ct)
    {
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (organization is null) return NotFound();

        var model = new OrganizationAdminEditVm
        {
            Id = organization.Id,
            NameArabic = organization.NameArabic,
            NameEnglish = organization.NameEnglish,
            MobileNumber = FormatMobileNumber(organization.MobileNumber),
            IsActive = organization.IsActive,
            IsDemo = organization.IsDemo
        };
        await PopulateDevicesAsync(model, ct);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(OrganizationAdminEditVm model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model.NameArabic) || string.IsNullOrWhiteSpace(model.NameEnglish))
            ModelState.AddModelError("", text["ValidationNamesRequired"]);

        string? normalizedMobile = null;
        if (!string.IsNullOrWhiteSpace(model.MobileNumber))
        {
            normalizedMobile = MobileNumberNormalizer.NormalizeKuwait(model.MobileNumber);
            if (normalizedMobile is null)
            {
                ModelState.AddModelError(nameof(model.MobileNumber),
                    "رقم الجوال يجب أن يكون رقمًا كويتيًا صالحًا / Enter a valid Kuwait mobile number.");
            }
            else
            {
                var duplicate = await db.Organizations.AsNoTracking()
                    .AnyAsync(x => x.Id != model.Id && x.MobileNumber == normalizedMobile, ct);
                if (duplicate)
                {
                    ModelState.AddModelError(nameof(model.MobileNumber),
                        "رقم الجوال مرتبط بجهة أخرى / This mobile number is already assigned to another organization.");
                }
            }
        }
        else
        {
            normalizedMobile = null;
        }

        if (!ModelState.IsValid)
        {
            ModelState.AddModelError("", text["ValidationCorrectFields"]);
            if (model.Id != 0) await PopulateDevicesAsync(model, ct);
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
            var existing = await db.Organizations.FindAsync([model.Id], ct);
            if (existing is null) return NotFound();
            entity = existing;
        }

        var previousMobile = entity.MobileNumber;
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
        if (!string.Equals(previousMobile, entity.MobileNumber, StringComparison.Ordinal))
        {
            var change = $"registeredMobile:{DescribeMobileChange(previousMobile)}->{DescribeMobileChange(entity.MobileNumber)}";
            await audit.WriteAsync("MOBILE_ORGANIZATION_NUMBER_CHANGED", "Organization", entity.Id.ToString(), change, ct);
        }
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

    private async Task PopulateDevicesAsync(OrganizationAdminEditVm model, CancellationToken ct)
    {
        var devices = await db.MobileDevices.AsNoTracking()
            .Where(x => x.OrganizationId == model.Id)
            .OrderByDescending(x => x.LastSeenAtUtc)
            .Select(x => new
            {
                x.DeviceId,
                x.Platform,
                x.AppVersion,
                x.PushEnabled,
                x.RegisteredAtUtc,
                x.LastSeenAtUtc,
                x.DeactivatedAtUtc
            })
            .ToListAsync(ct);

        model.Devices = devices.Select(x => new MobileDeviceAdminVm
        {
            MaskedDeviceId = MaskDeviceId(x.DeviceId),
            Platform = x.Platform,
            AppVersion = x.AppVersion,
            PushEnabled = x.PushEnabled,
            RegisteredAtUtc = x.RegisteredAtUtc,
            LastSeenAtUtc = x.LastSeenAtUtc,
            DeactivatedAtUtc = x.DeactivatedAtUtc
        }).ToList();
    }

    private static string? FormatMobileNumber(string? number)
        => number is { Length: 11 } && number.StartsWith("965", StringComparison.Ordinal)
            ? $"+965 {number[3..7]} {number[7..11]}"
            : number;

    private static string MaskDeviceId(string deviceId)
        => deviceId.Length <= 10 ? deviceId : $"{deviceId[..6]}…{deviceId[^4..]}";

    private static string DescribeMobileChange(string? number)
        => string.IsNullOrWhiteSpace(number) ? "not-configured" : $"configured-{number[^4..]}";
}
