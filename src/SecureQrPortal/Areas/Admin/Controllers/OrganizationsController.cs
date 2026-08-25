using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Services;

namespace SecureQrPortal.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Administrator")]
public sealed class OrganizationsController(ApplicationDbContext db, AuditService audit, IWebHostEnvironment env) : Controller
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
    public async Task<IActionResult> Edit(Organization model, IFormFile? logo, bool removeLogo = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model.NameArabic) || string.IsNullOrWhiteSpace(model.NameEnglish))
            ModelState.AddModelError("", "Arabic and English names are required.");

        if (logo is { Length: > 0 })
        {
            var validation = await ValidateLogoAsync(logo, ct);
            if (validation is not null) ModelState.AddModelError("logo", validation);
        }

        if (!ModelState.IsValid) return View(model);

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
        entity.IsActive = model.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        if (removeLogo)
        {
            DeleteOwnedLogo(entity.LogoPath);
            entity.LogoPath = null;
        }
        if (logo is { Length: > 0 })
        {
            var previous = entity.LogoPath;
            entity.LogoPath = await SaveLogoAsync(logo, ct);
            DeleteOwnedLogo(previous);
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
            TempData["Error"] = "Type DELETE to confirm organization deletion.";
            return RedirectToAction(nameof(Index));
        }

        var organization = await db.Organizations.Include(x => x.SecurePages).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (organization is null) return NotFound();
        if (organization.SecurePages.Count > 0)
        {
            TempData["Error"] = "Organization cannot be deleted while secure pages exist.";
            return RedirectToAction(nameof(Index));
        }

        var oldLogo = organization.LogoPath;
        db.Remove(organization);
        await db.SaveChangesAsync(ct);
        DeleteOwnedLogo(oldLogo);
        await audit.WriteAsync("ORGANIZATION_DELETE", "Organization", id.ToString(), organization.NameEnglish, ct);
        return RedirectToAction(nameof(Index));
    }

    private async Task<string?> ValidateLogoAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length > 3 * 1024 * 1024) return "Logo must be 3 MB or smaller.";
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp")) return "Only PNG, JPG/JPEG and WEBP images are allowed.";

        var header = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), ct);
        var isPng = read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var isJpeg = read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        var isWebp = read >= 12 && EncodingAscii(header, 0, 4) == "RIFF" && EncodingAscii(header, 8, 4) == "WEBP";
        return (ext == ".png" && isPng) || (ext is ".jpg" or ".jpeg" && isJpeg) || (ext == ".webp" && isWebp)
            ? null
            : "The uploaded file content does not match its declared image type.";
    }

    private static string EncodingAscii(byte[] bytes, int offset, int count) => System.Text.Encoding.ASCII.GetString(bytes, offset, count);

    private async Task<string> SaveLogoAsync(IFormFile file, CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var dir = Path.Combine(env.WebRootPath, "uploads", "logos");
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}{ext}";
        await using var fs = System.IO.File.Create(Path.Combine(dir, name));
        await file.CopyToAsync(fs, ct);
        return $"/uploads/logos/{name}";
    }

    private void DeleteOwnedLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/uploads/logos/", StringComparison.OrdinalIgnoreCase)) return;
        var safe = Path.GetFileName(path);
        var full = Path.Combine(env.WebRootPath, "uploads", "logos", safe);
        if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
    }
}
