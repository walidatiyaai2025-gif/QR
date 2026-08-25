using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Services;

namespace SecureQrPortal.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Administrator")]
public sealed class QrShareDispatchController(
    ApplicationDbContext db,
    QrShareService shares,
    AuditService audit) : Controller
{
    [HttpPost]
    public async Task<IActionResult> Dispatch(long id, long shareId, string channel, CancellationToken ct = default)
    {
        var page = await db.SecurePages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (page is null) return NotFound();

        var share = await shares.GetForPageAsync(shareId, id, ct);
        if (share is null) return NotFound();

        var now = DateTime.UtcNow;
        if (share.RevokedAtUtc.HasValue || StoredUtc(share.ExpiresAtUtc) <= now)
        {
            TempData["Error"] = IsArabic
                ? "رابط المشاركة ملغي أو منتهي ولا يمكن إرساله."
                : "This share link is revoked or expired and cannot be sent.";
            return RedirectToAction("Details", "Qr", new { id });
        }

        var raw = shares.GetRawToken(share);
        var shareUrl = $"{Request.Scheme}://{Request.Host}/q/share/{raw}";
        var message = QrShareMessage.Render(share.MessageTemplate, share, shareUrl, page.QrReference);
        var redactedMessage = RedactMessage(message, shareUrl);

        if (string.Equals(channel, "whatsapp", StringComparison.OrdinalIgnoreCase))
        {
            await audit.WriteAsync(
                "QR_SHARE_WHATSAPP_HANDOFF",
                "QrShareLink",
                shareId.ToString(),
                $"SecurePage={id}; message={redactedMessage}",
                ct);

            return Redirect($"https://wa.me/?text={Uri.EscapeDataString(message)}");
        }

        if (string.Equals(channel, "email", StringComparison.OrdinalIgnoreCase))
        {
            await audit.WriteAsync(
                "QR_SHARE_EMAIL_HANDOFF",
                "QrShareLink",
                shareId.ToString(),
                $"SecurePage={id}; message={redactedMessage}",
                ct);

            var subject = Uri.EscapeDataString($"Secure QR access {page.QrReference}");
            var body = Uri.EscapeDataString(message);
            return Redirect($"mailto:?subject={subject}&body={body}");
        }

        return BadRequest();
    }

    [HttpPost]
    public async Task<IActionResult> TrackCopy(long id, long shareId, CancellationToken ct = default)
    {
        var share = await shares.GetForPageAsync(shareId, id, ct);
        if (share is null) return NotFound();

        var raw = shares.GetRawToken(share);
        var shareUrl = $"{Request.Scheme}://{Request.Host}/q/share/{raw}";
        var message = QrShareMessage.Render(share.MessageTemplate, share, shareUrl, string.Empty);

        await audit.WriteAsync(
            "QR_SHARE_LINK_COPIED",
            "QrShareLink",
            shareId.ToString(),
            $"SecurePage={id}; message={RedactMessage(message, shareUrl)}",
            ct);

        return NoContent();
    }

    private static bool IsArabic =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    private static DateTime StoredUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string RedactMessage(string message, string shareUrl)
    {
        var redacted = message.Replace(shareUrl, "[SECURE_SHARE_LINK]", StringComparison.OrdinalIgnoreCase)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(';', ',')
            .Trim();
        return redacted.Length <= 900 ? redacted : redacted[..900];
    }
}
