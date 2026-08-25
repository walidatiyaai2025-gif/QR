using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;
using SecureQrPortal.Services;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Administrator")]
public sealed class QrController(
    ApplicationDbContext db,
    TokenService tokens,
    QrCodeService qr,
    QrStatusService status,
    QrShareService shares,
    SmsGatewayService sms,
    AdminIdentityService admin,
    AuditService audit,
    UserManager<ApplicationUser> users) : Controller
{
    public async Task<IActionResult> Index(
        string? search,
        string? statusFilter,
        long? organizationId,
        string? activity,
        DateTime? createdFrom,
        DateTime? createdTo,
        DateTime? expiryFrom,
        DateTime? expiryTo,
        AccessLimitMode? accessLimitMode,
        int page = 1,
        int pageSize = 20,
        string sort = "created_desc",
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var now = DateTime.UtcNow;
        var query = db.SecurePages.Include(x => x.Organization).AsNoTracking().AsQueryable();

        if (organizationId.HasValue)
            query = query.Where(x => x.OrganizationId == organizationId.Value);
        if (accessLimitMode.HasValue)
            query = query.Where(x => x.AccessLimitMode == accessLimitMode.Value);
        if (createdFrom.HasValue)
            query = query.Where(x => x.CreatedAtUtc >= DateTime.SpecifyKind(createdFrom.Value.Date, DateTimeKind.Utc));
        if (createdTo.HasValue)
            query = query.Where(x => x.CreatedAtUtc < DateTime.SpecifyKind(createdTo.Value.Date.AddDays(1), DateTimeKind.Utc));
        if (expiryFrom.HasValue)
            query = query.Where(x => x.ExpiresAtUtc >= DateTime.SpecifyKind(expiryFrom.Value.Date, DateTimeKind.Utc));
        if (expiryTo.HasValue)
            query = query.Where(x => x.ExpiresAtUtc < DateTime.SpecifyKind(expiryTo.Value.Date.AddDays(1), DateTimeKind.Utc));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (string.IsNullOrWhiteSpace(statusFilter) && Enum.TryParse<QrStatus>(s, true, out var searchedStatus))
            {
                query = ApplyStatusFilter(query, searchedStatus.ToString(), now);
            }
            else
            {
                var tokenCandidate = s.Contains("/q/", StringComparison.OrdinalIgnoreCase)
                    ? s[(s.LastIndexOf("/q/", StringComparison.OrdinalIgnoreCase) + 3)..].Split('?', '#')[0]
                    : s;
                var hash = TokenService.HashToken(tokenCandidate);
                var creatorIds = await users.Users
                    .Where(x => x.DisplayName.Contains(s) || (x.UserName != null && x.UserName.Contains(s)) || (x.Email != null && x.Email.Contains(s)))
                    .Select(x => x.Id)
                    .ToListAsync(ct);

                query = query.Where(x =>
                    x.QrReference.Contains(s) ||
                    x.TitleArabic.Contains(s) ||
                    x.TitleEnglish.Contains(s) ||
                    x.Organization.NameArabic.Contains(s) ||
                    x.Organization.NameEnglish.Contains(s) ||
                    x.PublicTokenHash == hash ||
                    (x.CreatedByAdminId != null && creatorIds.Contains(x.CreatedByAdminId)));
            }
        }

        if (!string.IsNullOrWhiteSpace(activity))
        {
            query = activity switch
            {
                "today" => query.Where(x => x.LastQrScanAtUtc >= now.Date),
                "never" => query.Where(x => x.LastQrScanAtUtc == null),
                "7d" => query.Where(x => x.LastQrScanAtUtc >= now.AddDays(-7)),
                "30d" => query.Where(x => x.LastQrScanAtUtc >= now.AddDays(-30)),
                "expiring" => query.Where(x => x.ExpiresAtUtc > now && x.ExpiresAtUtc <= now.AddDays(7)),
                _ => query
            };
        }

        if (!string.IsNullOrWhiteSpace(statusFilter))
            query = ApplyStatusFilter(query, statusFilter, now);

        query = sort switch
        {
            "ref" => query.OrderBy(x => x.QrReference),
            "org" => query.OrderBy(x => x.Organization.NameEnglish),
            "expiry" => query.OrderBy(x => x.ExpiresAtUtc),
            "scan_desc" => query.OrderByDescending(x => x.LastQrScanAtUtc),
            "updated_desc" => query.OrderByDescending(x => x.UpdatedAtUtc),
            _ => query.OrderByDescending(x => x.CreatedAtUtc)
        };

        var total = await query.CountAsync(ct);
        var pageItems = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var creatorIdsForPage = pageItems.Select(x => x.CreatedByAdminId).OfType<string>().Distinct().ToList();
        var names = await users.Users.Where(x => creatorIdsForPage.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var vm = new QrRegistryVm
        {
            Search = search,
            Status = statusFilter,
            OrganizationId = organizationId,
            Activity = activity,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo,
            ExpiryFrom = expiryFrom,
            ExpiryTo = expiryTo,
            AccessLimitMode = accessLimitMode,
            Page = page,
            PageSize = pageSize,
            Total = total,
            Sort = sort,
            Items = pageItems.Select(x => new QrRegistryItemVm
            {
                Id = x.Id,
                Reference = x.QrReference,
                Organization = x.Organization.NameEnglish,
                PageTitle = x.TitleEnglish,
                Status = status.GetStatus(x),
                PublicUrl = $"{baseUrl}/q/{tokens.Unprotect(x.ProtectedPublicToken)}",
                TokenCreatedAtUtc = x.CurrentTokenCreatedAtUtc,
                CreatedAtUtc = x.CreatedAtUtc,
                ValidFromUtc = x.ValidFromUtc,
                ExpiresAtUtc = x.ExpiresAtUtc,
                AccessLimitMode = x.AccessLimitMode,
                MaxAccessCount = x.MaxAccessCount,
                CurrentSuccessfulAccessCount = x.CurrentSuccessfulAccessCount,
                CurrentQrOpenCount = x.CurrentQrOpenCount,
                SuccessfulLoginCount = x.CurrentSuccessfulLoginCount,
                FailedLoginCount = x.CurrentFailedLoginCount,
                LastQrScanAtUtc = x.LastQrScanAtUtc,
                LastSuccessfulAccessAtUtc = x.LastSuccessfulAccessAtUtc,
                CreatedBy = x.CreatedByAdminId != null && names.TryGetValue(x.CreatedByAdminId, out var n) ? n : "—",
                UpdatedAtUtc = x.UpdatedAtUtc
            }).ToList()
        };

        ViewBag.Organizations = await db.Organizations.AsNoTracking().OrderBy(x => x.NameEnglish).ToListAsync(ct);
        return View(vm);
    }

    public async Task<IActionResult> Details(long id, CancellationToken ct)
    {
        var p = await db.SecurePages.Include(x => x.Organization).Include(x => x.TokenHistory)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();

        var token = tokens.Unprotect(p.ProtectedPublicToken);
        var userIds = new[] { p.CreatedByAdminId, p.LastModifiedByAdminId }.OfType<string>().Distinct().ToList();
        var names = await users.Users.Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var shareRows = await shares.ListForPageAsync(id, ct);
        var shareVms = shareRows.Select(x =>
        {
            var raw = shares.GetRawToken(x);
            var shareUrl = $"{Request.Scheme}://{Request.Host}/q/share/{raw}";
            var template = QrShareMessage.NormalizeTemplate(x.MessageTemplate);
            var message = QrShareMessage.Render(template, x, shareUrl, p.QrReference);
            return new QrShareAdminVm
            {
                Share = x,
                ShareUrl = shareUrl,
                MessageTemplate = template,
                Message = message,
                WhatsAppUrl = $"https://wa.me/?text={Uri.EscapeDataString(message)}",
                EmailUrl = $"mailto:?subject={Uri.EscapeDataString($"Secure QR access {p.QrReference}")}&body={Uri.EscapeDataString(message)}"
            };
        }).ToList();

        return View(new QrDetailsVm
        {
            Page = p,
            Status = status.GetStatus(p),
            PublicUrl = $"{Request.Scheme}://{Request.Host}/q/{token}",
            MaskedToken = tokens.Mask(token),
            RemainingAccesses = QrStatusService.RemainingAccesses(p),
            Timeline = await db.AccessLogs.Where(x => x.SecurePageId == id).OrderByDescending(x => x.TimestampUtc).Take(100).ToListAsync(ct),
            History = p.TokenHistory.OrderByDescending(x => x.RevokedAtUtc).ToList(),
            ShareLinks = shareVms,
            DefaultShareMessageTemplate = QrShareMessage.DefaultTemplate,
            SmsConfigured = sms.IsConfigured,
            CreatedBy = p.CreatedByAdminId != null && names.TryGetValue(p.CreatedByAdminId, out var c) ? c : "—",
            ModifiedBy = p.LastModifiedByAdminId != null && names.TryGetValue(p.LastModifiedByAdminId, out var m) ? m : "—"
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateShare(
        long id,
        int maxOpenCount = 1,
        int linkLifetimeHours = 24,
        int sessionDurationMinutes = 15,
        string pagePassword = "",
        string? messageTemplate = null,
        CancellationToken ct = default)
    {
        var p = await db.SecurePages.Include(x => x.Credential).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        if (status.GetStatus(p) != QrStatus.ACTIVE)
        {
            TempData["Error"] = "Only an ACTIVE QR can receive a new share link.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            var share = await shares.CreateAsync(
                p,
                maxOpenCount,
                linkLifetimeHours,
                sessionDurationMinutes,
                pagePassword,
                messageTemplate,
                admin.CurrentUserId,
                ct);
            await audit.WriteAsync("QR_SHARE_CREATE", "QrShareLink", share.Id.ToString(), $"{p.QrReference}; opens={share.MaxOpenCount}; expires={share.ExpiresAtUtc:O}; session={share.SessionDurationMinutes}m; uses-page-credential=true", ct);
            TempData["Success"] = "Secure share link created using the QR page's existing username and password.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateShareMessage(long id, long shareId, string? messageTemplate, CancellationToken ct = default)
    {
        if (!await shares.UpdateMessageAsync(shareId, id, messageTemplate, ct))
            return NotFound();

        await audit.WriteAsync("QR_SHARE_MESSAGE_UPDATE", "QrShareLink", shareId.ToString(), $"SecurePage={id}", ct);
        TempData["Success"] = "Share message saved.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> SendShareSms(long id, long shareId, string mobile, string? messageTemplate, CancellationToken ct = default)
    {
        var p = await db.SecurePages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();

        var share = await shares.GetForPageAsync(shareId, id, ct);
        if (share is null) return NotFound();
        if (share.RevokedAtUtc.HasValue || StoredUtc(share.ExpiresAtUtc) <= DateTime.UtcNow)
        {
            TempData["Error"] = "This share link is revoked or expired and cannot be sent by SMS.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var template = QrShareMessage.NormalizeTemplate(messageTemplate ?? share.MessageTemplate);
        await shares.UpdateMessageAsync(shareId, id, template, ct);

        var raw = shares.GetRawToken(share);
        var shareUrl = $"{Request.Scheme}://{Request.Host}/q/share/{raw}";
        var message = QrShareMessage.Render(template, share, shareUrl, p.QrReference);
        var result = await sms.SendAsync(mobile, message, ct);

        await audit.WriteAsync(
            result.Success ? "QR_SHARE_SMS_SENT" : "QR_SHARE_SMS_FAILED",
            "QrShareLink",
            shareId.ToString(),
            $"SecurePage={id}; mobile={MaskMobile(result.NormalizedMobile)}; http={result.HttpStatusCode?.ToString() ?? "none"}; response={SafeAudit(result.ResponseText)}",
            ct);

        if (result.Success)
            TempData["Success"] = $"SMS request sent to {MaskMobile(result.NormalizedMobile)}. Gateway response: {result.ResponseText}";
        else
            TempData["Error"] = result.Error ?? $"SMS gateway rejected the request. Response: {result.ResponseText}";

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RevokeShare(long id, long shareId, string confirmation, CancellationToken ct = default)
    {
        if (!string.Equals(confirmation, "REVOKE SHARE", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Share revocation was not confirmed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (await shares.RevokeAsync(shareId, id, ct))
        {
            await audit.WriteAsync("QR_SHARE_REVOKE", "QrShareLink", shareId.ToString(), $"SecurePage={id}", ct);
            TempData["Success"] = "Share link revoked immediately.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Code(long id, int size = 10, CancellationToken ct = default)
    {
        var p = await db.SecurePages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        var url = $"{Request.Scheme}://{Request.Host}/q/{tokens.Unprotect(p.ProtectedPublicToken)}";
        return File(qr.CreatePng(url, Math.Clamp(size, 3, 30)), "image/png");
    }

    [HttpGet]
    public async Task<IActionResult> Download(long id, CancellationToken ct)
    {
        var p = await db.SecurePages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        var url = $"{Request.Scheme}://{Request.Host}/q/{tokens.Unprotect(p.ProtectedPublicToken)}";
        return File(qr.CreatePng(url, 14), "image/png", $"{p.QrReference}.png");
    }

    [HttpGet]
    public async Task<IActionResult> Print(long id, CancellationToken ct)
    {
        var p = await db.SecurePages.Include(x => x.Organization).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? NotFound() : View(p);
    }

    [HttpPost]
    public async Task<IActionResult> Revoke(long id, string confirmation, CancellationToken ct)
    {
        if (!string.Equals(confirmation, "REVOKE", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Type REVOKE to confirm.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var p = await db.SecurePages.FindAsync([id], ct);
        if (p is null) return NotFound();
        p.RevokedAtUtc = DateTime.UtcNow;
        p.UpdatedAtUtc = DateTime.UtcNow;
        p.LastModifiedByAdminId = admin.CurrentUserId;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("QR_REVOKE", "SecurePage", id.ToString(), p.QrReference, ct);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Regenerate(long id, string confirmation, CancellationToken ct)
    {
        if (!string.Equals(confirmation, "REGENERATE", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Type REGENERATE to confirm.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var p = await db.SecurePages.FindAsync([id], ct);
        if (p is null) return NotFound();

        var now = DateTime.UtcNow;
        db.QrTokenHistories.Add(new QrTokenHistory
        {
            SecurePageId = p.Id,
            PreviousTokenHash = p.PublicTokenHash,
            CreatedAtUtc = p.CurrentTokenCreatedAtUtc,
            RevokedAtUtc = now,
            RevokedByAdminId = admin.CurrentUserId,
            RevocationReason = "Administrator regenerated QR token",
            ReplacementTokenCreatedAtUtc = now
        });

        var raw = tokens.GenerateToken();
        p.PublicTokenHash = TokenService.HashToken(raw);
        p.ProtectedPublicToken = tokens.Protect(raw);
        p.CurrentTokenCreatedAtUtc = now;
        p.RevokedAtUtc = null;
        p.UpdatedAtUtc = now;
        p.LastModifiedByAdminId = admin.CurrentUserId;
        p.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await audit.WriteAsync("QR_REGENERATE", "SecurePage", id.ToString(), $"{p.QrReference}; previous token invalidated", ct);
        TempData["Success"] = "QR token regenerated. The previous QR URL is no longer valid.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ResetCounter(long id, string confirmation, CancellationToken ct)
    {
        if (!string.Equals(confirmation, "RESET", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Type RESET to confirm.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var p = await db.SecurePages.FindAsync([id], ct);
        if (p is null) return NotFound();
        p.CurrentSuccessfulAccessCount = 0;
        p.CurrentQrOpenCount = 0;
        p.UpdatedAtUtc = DateTime.UtcNow;
        p.LastModifiedByAdminId = admin.CurrentUserId;
        p.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("QR_COUNTER_RESET", "SecurePage", id.ToString(), p.QrReference, ct);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var rows = await db.SecurePages.Include(x => x.Organization).AsNoTracking().OrderBy(x => x.QrReference).ToListAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("QR Reference,Organization,Page,Status,Token Created,Creation Date,Expiry Date,Access Limit Mode,Maximum Accesses,Current Access Count,QR Open Count,Successful Logins,Failed Logins,Last Scan,Last Access");
        foreach (var p in rows)
        {
            sb.AppendLine(string.Join(',',
                Csv(p.QrReference), Csv(p.Organization.NameEnglish), Csv(p.TitleEnglish), status.GetStatus(p),
                p.CurrentTokenCreatedAtUtc.ToString("O"), p.CreatedAtUtc.ToString("O"), p.ExpiresAtUtc?.ToString("O") ?? "",
                p.AccessLimitMode, p.MaxAccessCount?.ToString() ?? "Unlimited", p.CurrentSuccessfulAccessCount,
                p.CurrentQrOpenCount, p.CurrentSuccessfulLoginCount, p.CurrentFailedLoginCount,
                p.LastQrScanAtUtc?.ToString("O") ?? "", p.LastSuccessfulAccessAtUtc?.ToString("O") ?? ""));
        }
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"qr-registry-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static DateTime StoredUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string MaskMobile(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile)) return "—";
        return mobile.Length <= 4 ? new string('*', mobile.Length) : new string('*', mobile.Length - 4) + mobile[^4..];
    }

    private static string SafeAudit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "empty";
        var clean = value.Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Trim();
        return clean.Length <= 180 ? clean : clean[..180];
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
        "ACTIVE" => q.Where(x => x.RevokedAtUtc == null && x.IsActive && x.Organization.IsActive &&
            (x.ValidFromUtc == null || x.ValidFromUtc <= now) && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now) &&
            !(x.MaxAccessCount != null &&
              (((x.AccessLimitMode == AccessLimitMode.MaximumSuccessfulAccesses || x.AccessLimitMode == AccessLimitMode.ExpiryAndSuccessfulAccesses) && x.CurrentSuccessfulAccessCount >= x.MaxAccessCount) ||
               ((x.AccessLimitMode == AccessLimitMode.MaximumQrOpens || x.AccessLimitMode == AccessLimitMode.ExpiryAndQrOpens) && x.CurrentQrOpenCount >= x.MaxAccessCount)))),
        _ => q
    };
}
