using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using SecureQrPortal.Security;
using SecureQrPortal.Services;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Controllers;

[Route("q/share")]
public sealed class QrShareController(QrShareService shares, TokenService tokens) : Controller
{
    [HttpGet("{token}")]
    public async Task<IActionResult> Open(string token, CancellationToken ct)
    {
        var share = await shares.FindByTokenAsync(token, ct);
        if (share is null) return View("Unavailable");

        var now = DateTime.UtcNow;
        var canReveal = share.RevokedAtUtc is null && share.ExpiresAtUtc > now && share.CurrentOpenCount < share.MaxOpenCount;
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
        var result = await shares.RevealAsync(token, ct);
        if (result is null) return View("Unavailable");

        var share = result.Share;
        var ar = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        var publicToken = tokens.Unprotect(share.SecurePage.ProtectedPublicToken);
        var publicUrl = $"{Request.Scheme}://{Request.Host}/q/{publicToken}";

        return View("Reveal", new QrShareRevealVm
        {
            Share = share,
            Organization = ar ? share.SecurePage.Organization.NameArabic : share.SecurePage.Organization.NameEnglish,
            PageTitle = ar ? share.SecurePage.TitleArabic : share.SecurePage.TitleEnglish,
            PublicQrUrl = publicUrl,
            Username = share.Username,
            Password = result.Password
        });
    }
}
