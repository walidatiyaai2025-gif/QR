using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureQrPortal.Models;
using SecureQrPortal.Security.Captcha;
using SecureQrPortal.Services;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Controllers;
public sealed class AccountController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    AuditService audit,
    ICaptchaService captcha) : Controller
{
    private static readonly ApplicationUser DummyUser = new();
    private static readonly PasswordHasher<ApplicationUser> DummyPasswordHasher = new();
    private static readonly string DummyPasswordHash = DummyPasswordHasher.HashPassword(
        DummyUser,
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

    [AllowAnonymous, HttpGet, EnableRateLimiting("admin-captcha-generation")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View(NewLoginModel());
    }

    [AllowAnonymous, HttpGet("account/captcha/{challengeId}"), EnableRateLimiting("admin-captcha-image")]
    public IActionResult CaptchaImage(string challengeId)
    {
        var image = captcha.GetImage(challengeId);
        if (image is null)
            return NotFound();

        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        return File(image.Bytes, "image/png");
    }

    [AllowAnonymous, HttpPost("account/captcha/refresh"), ValidateAntiForgeryToken,
     EnableRateLimiting("admin-captcha-generation")]
    public async Task<IActionResult> RefreshCaptcha(string? challengeId, string? returnUrl = null)
    {
        captcha.Invalidate(challengeId);
        var challenge = captcha.IssueChallenge();
        await audit.WriteAsync("CAPTCHA_REFRESHED", "AdminLogin", details: "Administrator login CAPTCHA refreshed");

        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            return Json(new
            {
                challengeId = challenge.ChallengeId,
                imageUrl = Url.Action(nameof(CaptchaImage), new { challengeId = challenge.ChallengeId }),
                expiresAtUtc = challenge.ExpiresAtUtc
            });
        }

        ViewBag.ReturnUrl = returnUrl;
        return View(nameof(Login), new LoginVm { CaptchaChallengeId = challenge.ChallengeId });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("admin-login")]
    public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            captcha.Invalidate(vm.CaptchaChallengeId);
            await audit.WriteAsync("CAPTCHA_FAILED", "AdminLogin", details: "Administrator login request validation failed");
            return LoginFailure(vm, captchaFieldError: true);
        }

        var captchaResult = captcha.Validate(vm.CaptchaChallengeId, vm.CaptchaAnswer);
        if (captchaResult != CaptchaValidationStatus.Success)
        {
            await AuditCaptchaFailureAsync(captchaResult);
            await audit.WriteAsync("ADMIN_LOGIN_FAILED", "AdminUser", details: "Administrator sign-in rejected");
            return LoginFailure(vm, captchaFieldError: true);
        }

        var user = await users.FindByEmailAsync(vm.Email.Trim());
        if (user is null)
        {
            _ = DummyPasswordHasher.VerifyHashedPassword(DummyUser, DummyPasswordHash, vm.Password);
            await audit.WriteAsync("ADMIN_LOGIN_FAILED", "AdminUser", details: "Administrator sign-in rejected");
            return LoginFailure(vm);
        }

        var result = await signIn.PasswordSignInAsync(user, vm.Password, vm.RememberMe, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            var classification = result.IsLockedOut ? "Administrator sign-in rejected (locked out)" : "Administrator sign-in rejected";
            await audit.WriteAsync("ADMIN_LOGIN_FAILED", "AdminUser", user.Id, classification);
            return LoginFailure(vm);
        }

        captcha.Invalidate(vm.CaptchaChallengeId);
        await audit.WriteAsync("ADMIN_LOGIN_SUCCESS", "AdminUser", user.Id, "Administrator login succeeded");
        return LocalRedirect(Url.IsLocalUrl(returnUrl)
            ? returnUrl!
            : Url.Action("Index", "Dashboard", new { area = "Admin" })!);
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Logout()
    {
        await audit.WriteAsync("LOGOUT","AdminUser",users.GetUserId(User),"Administrator logout");
        await signIn.SignOutAsync(); return RedirectToAction(nameof(Login));
    }

    [Authorize, HttpGet] public IActionResult ChangePassword()=>View(new ChangePasswordVm());
    [Authorize, HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordVm vm)
    {
        if (!ModelState.IsValid)
        {
            ModelState.AddModelError("", AdminAccountText.ChangePasswordValidation(IsArabic));
            return View(vm);
        }

        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var result = await users.ChangePasswordAsync(user, vm.CurrentPassword, vm.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError("", AdminAccountText.ChangePasswordError(error.Code, IsArabic));
            return View(vm);
        }

        await signIn.RefreshSignInAsync(user);
        await audit.WriteAsync("PASSWORD_CHANGE", "AdminUser", user.Id, "Admin password changed");
        TempData["Success"] = AdminAccountText.PasswordChanged(IsArabic);
        return RedirectToAction(nameof(ChangePassword));
    }

    private LoginVm NewLoginModel(LoginVm? source = null)
    {
        var challenge = captcha.IssueChallenge();
        return new LoginVm
        {
            Email = source?.Email ?? "",
            RememberMe = source?.RememberMe ?? false,
            CaptchaChallengeId = challenge.ChallengeId
        };
    }

    private IActionResult LoginFailure(LoginVm source, bool captchaFieldError = false)
    {
        captcha.Invalidate(source.CaptchaChallengeId);
        var model = NewLoginModel(source);
        var message = IsArabic
            ? "تعذر تسجيل الدخول. تحقق من البيانات ورمز التحقق وحاول مرة أخرى."
            : "Unable to sign in. Check your details and verification code, then try again.";

        ModelState.Clear();
        ModelState.AddModelError(captchaFieldError ? nameof(LoginVm.CaptchaAnswer) : string.Empty, message);
        return View(nameof(Login), model);
    }

    private async Task AuditCaptchaFailureAsync(CaptchaValidationStatus status)
    {
        var (action, details) = status switch
        {
            CaptchaValidationStatus.Expired => ("CAPTCHA_EXPIRED", "Administrator login CAPTCHA expired"),
            CaptchaValidationStatus.Replayed or CaptchaValidationStatus.NotFound =>
                ("CAPTCHA_REPLAYED", "Administrator login CAPTCHA was missing, invalidated, or replayed"),
            CaptchaValidationStatus.MaxAttemptsExceeded =>
                ("CAPTCHA_FAILED", "Administrator login CAPTCHA maximum attempts reached"),
            _ => ("CAPTCHA_FAILED", "Administrator login CAPTCHA rejected")
        };

        await audit.WriteAsync(action, "AdminLogin", details: details);
    }

    private static bool IsArabic => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
}
