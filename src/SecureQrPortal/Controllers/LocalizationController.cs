using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
namespace SecureQrPortal.Controllers;
public sealed class LocalizationController : Controller
{
    [HttpGet]
    public IActionResult Switch(string culture, string? returnUrl = null)
    {
        culture = culture == "en" ? "en" : "ar";
        Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName, CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)), new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax });
        return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "/" : returnUrl);
    }
}
