using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using SecureQrPortal.Models;
using SecureQrPortal.Security;
using SecureQrPortal.Services;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Controllers;

[Route("q/share")]
public sealed class QrShareController(
    QrShareService shares,
    TokenService tokens,
    IDataProtectionProvider protection) : Controller
{
    private readonly IDataProtector _receiptProtector = protection.CreateProtector("SecureQrPortal.QrShare.Receipt.v2");

    [HttpGet("{token}")]
    public async Task<IActionResult> Open(string token, CancellationToken ct)
    {
        var share = await shares.FindByTokenAsync(token, ct);
        if (share is null) return View("Unavailable");

        if (TryGetValidCookieReceipt(share, out _))
        {
            PrepareSensitiveResponse();
            return View("Reveal", BuildRevealVm(share));
        }

        var now = DateTime.UtcNow;
        var canReveal = share.RevokedAtUtc is null &&
                        share.ExpiresAtUtc > now &&
                        share.CurrentOpenCount < share.MaxOpenCount;
        var ar = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        ViewBag.RevealRequestId = Guid.NewGuid().ToString("N");

        return View("Open", new QrShareLandingVm
        {
            Share = share,
            CanReveal = canReveal,
            Organization = ar ? share.SecurePage.Organization.NameArabic : share.SecurePage.Organization.NameEnglish,
            PageTitle = ar ? share.SecurePage.TitleArabic : share.SecurePage.TitleEnglish
        });
    }

    [HttpPost("{token}/reveal")]
    public async Task<IActionResult> Reveal(string token, string? revealRequestId, CancellationToken ct)
    {
        var existing = await shares.FindByTokenAsync(token, ct);
        if (existing is not null && TryGetValidCookieReceipt(existing, out _))
        {
            PrepareSensitiveResponse();
            return View("Reveal", BuildRevealVm(existing));
        }

        // Old cached landing pages may not contain the idempotency field. They are
        // still allowed once, while all newly-rendered pages carry a stable request id
        // so browser/proxy retries cannot consume the one-time reveal twice.
        revealRequestId = string.IsNullOrWhiteSpace(revealRequestId)
            ? Guid.NewGuid().ToString("N")
            : revealRequestId;

        var result = await shares.RevealAsync(token, revealRequestId, ct);
        if (result is null) return View("Unavailable");

        var receipt = CreateRevealReceipt(result.Share);
        WriteRevealReceiptCookie(result.Share, receipt);

        PrepareSensitiveResponse();
        return View("Reveal", BuildRevealVm(result.Share));
    }

    [HttpGet("{token}/credentials")]
    public async Task<IActionResult> Credentials(string token, string? receipt, CancellationToken ct)
    {
        var share = await shares.FindByTokenAsync(token, ct);
        if (share is null) return View("Unavailable");

        if (!TryValidateRevealReceipt(share, receipt))
        {
            if (!TryGetValidCookieReceipt(share, out receipt))
                return View("Unavailable");
        }

        WriteRevealReceiptCookie(share, receipt!);
        PrepareSensitiveResponse();
        return View("Reveal", BuildRevealVm(share));
    }

    private QrShareRevealVm BuildRevealVm(QrShareLink share)
    {
        var ar = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        var publicToken = tokens.Unprotect(share.SecurePage.ProtectedPublicToken);
        var publicUrl = $"{Request.Scheme}://{Request.Host}/q/{publicToken}";

        return new QrShareRevealVm
        {
            Share = share,
            Organization = ar ? share.SecurePage.Organization.NameArabic : share.SecurePage.Organization.NameEnglish,
            PageTitle = ar ? share.SecurePage.TitleArabic : share.SecurePage.TitleEnglish,
            PublicQrUrl = publicUrl,
            Username = share.Username,
            Password = shares.GetPassword(share)
        };
    }

    private string CreateRevealReceipt(QrShareLink share)
    {
        if (share.AccessWindowEndsAtUtc is not DateTime accessEnd)
            throw new InvalidOperationException("A revealed share must have an access-window deadline.");

        var utcEnd = DateTime.SpecifyKind(accessEnd, DateTimeKind.Utc);
        var payload = $"{share.Id}|{share.TokenHash}|{utcEnd.Ticks}";
        return _receiptProtector.Protect(payload);
    }

    private void WriteRevealReceiptCookie(QrShareLink share, string receipt)
    {
        if (share.AccessWindowEndsAtUtc is not DateTime accessEnd)
            return;

        var utcEnd = DateTime.SpecifyKind(accessEnd, DateTimeKind.Utc);
        Response.Cookies.Append(
            ReceiptCookieName(share.Id),
            receipt,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/q/share",
                Expires = new DateTimeOffset(utcEnd)
            });
    }

    private bool TryGetValidCookieReceipt(QrShareLink share, out string receipt)
    {
        receipt = string.Empty;
        if (!Request.Cookies.TryGetValue(ReceiptCookieName(share.Id), out var candidate) ||
            string.IsNullOrWhiteSpace(candidate) ||
            !TryValidateRevealReceipt(share, candidate))
            return false;

        receipt = candidate;
        return true;
    }

    private bool TryValidateRevealReceipt(QrShareLink share, string? receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt) ||
            share.RevokedAtUtc is not null ||
            share.AccessWindowEndsAtUtc is not DateTime accessEnd ||
            accessEnd <= DateTime.UtcNow)
            return false;

        try
        {
            var payload = _receiptProtector.Unprotect(receipt);
            var parts = payload.Split('|', 3);
            if (parts.Length != 3 ||
                !long.TryParse(parts[0], out var shareId) ||
                !long.TryParse(parts[2], out var expiryTicks))
                return false;

            var expectedTicks = DateTime.SpecifyKind(accessEnd, DateTimeKind.Utc).Ticks;
            return shareId == share.Id &&
                   expiryTicks == expectedTicks &&
                   string.Equals(parts[1], share.TokenHash, StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void PrepareSensitiveResponse()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers["X-QR-Share-Flow"] = "idempotent-v3";
    }

    private static string ReceiptCookieName(long shareId) => $"SecureQrPortal.ShareReceipt.{shareId}";
}
