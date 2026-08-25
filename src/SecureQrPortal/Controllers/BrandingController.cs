using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureQrPortal.Services;

namespace SecureQrPortal.Controllers;

[AllowAnonymous]
[Route("branding")]
public sealed class BrandingController : Controller
{
    [HttpGet("diwan-logo")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public IActionResult DiwanLogo()
    {
        Response.Headers.CacheControl = "public,max-age=86400,immutable";
        return File(QrLogoAsset.Bytes, "image/jpeg");
    }
}
