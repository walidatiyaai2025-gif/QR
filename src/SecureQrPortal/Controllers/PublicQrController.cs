using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureQrPortal.Models;
using SecureQrPortal.Security;
using SecureQrPortal.Services;

namespace SecureQrPortal.Controllers;

[Route("q")]
public sealed class PublicQrController(
    SecurePageAccessService access,
    QrStatusService status,
    AppSettingsService settings) : Controller
{
    [HttpGet("{token}")]
    public async Task<IActionResult> Open(string token, CancellationToken ct)
    {
        var page = await access.FindByTokenAsync(token, ct);
        if (page is null)
        {
            await access.AddInvalidTokenLogAsync(HttpContext, ct);
            return View("Invalid");
        }

        var state = await access.RegisterQrOpenAsync(page, HttpContext, ct);
        if (state != QrStatus.ACTIVE) return View("Invalid");

        var hash = TokenService.HashToken(token);
        HttpContext.Session.SetString(OpenPassKey(page.Id, hash), "1");
        return View("Login", page);
    }

    [HttpPost("{token}/login"), EnableRateLimiting("public-login")]
    public async Task<IActionResult> Login(string token, string username, string password, CancellationToken ct)
    {
        var page = await access.FindByTokenAsync(token, ct);
        if (page is null) return View("Invalid");

        var hash = TokenService.HashToken(token);
        var state = status.GetStatus(page);
        if (!CanContinueExistingQrOpen(page, state, hash)) return View("Invalid");

        var verification = await access.VerifyCredentialsWithPolicyAsync(page, username, password, HttpContext, ct);
        if (!verification.Success)
        {
            ViewBag.LoginError = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar"
                ? "بيانات الدخول غير صحيحة أو انتهت صلاحية بيانات المشاركة."
                : "Invalid credentials or the shared access window has expired.";
            return View("Login", page);
        }

        HttpContext.Session.SetString(AuthKey(page.Id), hash);
        HttpContext.Session.Remove(CountedKey(page.Id, hash));
        if (verification.HardExpiresAtUtc.HasValue)
            HttpContext.Session.SetString(HardExpiryKey(page.Id), verification.HardExpiresAtUtc.Value.ToString("O"));
        else
            HttpContext.Session.Remove(HardExpiryKey(page.Id));

        return RedirectToAction(nameof(Content), new { token });
    }

    [HttpGet("{token}/content")]
    public async Task<IActionResult> Content(string token, CancellationToken ct)
    {
        var page = await access.FindByTokenAsync(token, ct);
        if (page is null) return View("Invalid");

        var hash = TokenService.HashToken(token);
        if (HttpContext.Session.GetString(AuthKey(page.Id)) != hash)
            return RedirectToAction(nameof(Open), new { token });

        var hardExpiryRaw = HttpContext.Session.GetString(HardExpiryKey(page.Id));
        DateTime? hardExpiry = null;
        if (!string.IsNullOrWhiteSpace(hardExpiryRaw) && DateTime.TryParse(hardExpiryRaw, null, DateTimeStyles.RoundtripKind, out var parsed))
        {
            hardExpiry = parsed.ToUniversalTime();
            if (DateTime.UtcNow >= hardExpiry.Value)
            {
                ClearAuthSession(page.Id, hash);
                return View("SessionExpired", page);
            }
        }

        var state = status.GetStatus(page);
        var alreadyCounted = HttpContext.Session.GetString(CountedKey(page.Id, hash)) == "1";
        if (!CanContinueAuthenticatedSession(page, state, hash, alreadyCounted)) return View("Invalid");

        if (!alreadyCounted)
        {
            var result = await access.RegisterSuccessfulAccessAsync(page, HttpContext, allowQrOpenLimitSession: true, ct);
            if (result is not (QrStatus.ACTIVE or QrStatus.LIMIT_REACHED)) return View("Invalid");
            HttpContext.Session.SetString(CountedKey(page.Id, hash), "1");
        }

        ViewBag.ShowExpiry = bool.TryParse(await settings.GetAsync("ShowExpiryPublicly", "true", ct), out var show) && show;
        ViewBag.HardSessionExpiresAtUtc = hardExpiry;
        return View(page);
    }

    private void ClearAuthSession(long pageId, string hash)
    {
        HttpContext.Session.Remove(AuthKey(pageId));
        HttpContext.Session.Remove(CountedKey(pageId, hash));
        HttpContext.Session.Remove(OpenPassKey(pageId, hash));
        HttpContext.Session.Remove(HardExpiryKey(pageId));
    }

    private bool CanContinueExistingQrOpen(SecurePage page, QrStatus state, string hash)
    {
        if (state == QrStatus.ACTIVE) return true;
        if (state != QrStatus.LIMIT_REACHED) return false;
        if (page.AccessLimitMode is not (AccessLimitMode.MaximumQrOpens or AccessLimitMode.ExpiryAndQrOpens)) return false;
        return HttpContext.Session.GetString(OpenPassKey(page.Id, hash)) == "1";
    }

    private bool CanContinueAuthenticatedSession(SecurePage page, QrStatus state, string hash, bool alreadyCounted)
    {
        if (state == QrStatus.ACTIVE) return true;
        if (state != QrStatus.LIMIT_REACHED) return false;

        if (page.AccessLimitMode is AccessLimitMode.MaximumQrOpens or AccessLimitMode.ExpiryAndQrOpens)
            return HttpContext.Session.GetString(OpenPassKey(page.Id, hash)) == "1";

        if (page.AccessLimitMode is AccessLimitMode.MaximumSuccessfulAccesses or AccessLimitMode.ExpiryAndSuccessfulAccesses)
            return alreadyCounted;

        return false;
    }

    private static string AuthKey(long pageId) => $"page-auth:{pageId}";
    private static string OpenPassKey(long pageId, string hash) => $"page-open-pass:{pageId}:{hash}";
    private static string CountedKey(long pageId, string hash) => $"page-counted:{pageId}:{hash}";
    private static string HardExpiryKey(long pageId) => $"page-hard-expiry:{pageId}";
}
