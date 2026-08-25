using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
    private readonly IDataProtector _receiptProtector = protection.CreateProtector("SecureQrPortal.QrShare.Receipt.v1");

    [HttpGet("{token}")]
    public async Task<IActionResult> Open(string token, CancellationToken ct)
    {
        var share = await shares.FindByTokenAsync(token, ct);
        if (share is null) return View("Unavailable");

        if (TryReadRevealReceipt(share, out var resumedPassword))
            return View("Reveal", BuildRevealVm(share, resumedPassword));

        var now = DateTime.UtcNow;
        var canReveal = share.RevokedAtUtc is null &&
                        share.ExpiresAtUtc > now &&
                        share.CurrentOpenCount < share.MaxOpenCount;
        var ar = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

        return View("Open", new QrShareLandingVm
        {
            Share = share,
            CanReveal = canReveal,
            Organization = ar ? share.SecurePage.Organization.NameArabic : share.SecurePage.Organization.NameEnglish,
            PageTitle = ar ? share.SecurePage.TitleArabic : share.SecurePage.TitleEnglish
        });
    }

    [HttpPost("{token}/reveal")]
    public async Task<IActionResult> Reveal(string token, CancellationToken ct)
    {
        var existing = await shares.FindByTokenAsync(token, ct);
        if (existing is not null && TryReadRevealReceipt(existing, out _))
            return RedirectToAction(nameof(Open), new { token });

        var result = await shares.RevealAsync(token, ct);
        if (result is null) return View("Unavailable");

        WriteRevealReceipt(result.Share, result.Password);

        // Post/Redirect/Get: the reveal is counted exactly once, then the same recipient
        // is allowed to reopen the credential screen until the hard access deadline.
        return RedirectToAction(nameof(Open), new { token });
    }

    private QrShareRevealVm BuildRevealVm(QrShareLink share, string password)
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
            Password = password
        };
    }

    private void WriteRevealReceipt(QrShareLink share, string password)
    {
        if (share.AccessWindowEndsAtUtc is not DateTime accessEnd)
            throw new InvalidOperationException("A revealed share must have an access-window deadline.");

        var utcEnd = DateTime.SpecifyKind(accessEnd, DateTimeKind.Utc);
        var encodedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
        var payload = $"{share.Id}|{share.TokenHash}|{utcEnd.Ticks}|{encodedPassword}";
        var protectedPayload = _receiptProtector.Protect(payload);

        Response.Cookies.Append(
            ReceiptCookieName(share.Id),
            protectedPayload,
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

    private bool TryReadRevealReceipt(QrShareLink share, out string password)
    {
        password = string.Empty;

        if (share.RevokedAtUtc is not null ||
            share.AccessWindowEndsAtUtc is not DateTime accessEnd ||
            accessEnd <= DateTime.UtcNow)
            return false;

        if (!Request.Cookies.TryGetValue(ReceiptCookieName(share.Id), out var protectedPayload) ||
            string.IsNullOrWhiteSpace(protectedPayload))
            return false;

        try
        {
            var payload = _receiptProtector.Unprotect(protectedPayload);
            var parts = payload.Split('|', 4);
            if (parts.Length != 4 ||
                !long.TryParse(parts[0], out var shareId) ||
                !long.TryParse(parts[2], out var expiryTicks))
                return false;

            var expectedTicks = DateTime.SpecifyKind(accessEnd, DateTimeKind.Utc).Ticks;
            if (shareId != share.Id ||
                expiryTicks != expectedTicks ||
                !string.Equals(parts[1], share.TokenHash, StringComparison.Ordinal))
                return false;

            password = Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]));
            return true;
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

    private static string ReceiptCookieName(long shareId) => $"SecureQrPortal.ShareReceipt.{shareId}";
}
