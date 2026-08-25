using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureQrPortal.Security;
using SecureQrPortal.Services;

namespace SecureQrPortal.Controllers;

[ApiController]
[IgnoreAntiforgeryToken]
[Authorize(AuthenticationSchemes = MobileBearerDefaults.Scheme)]
[Route("api/mobile/auth")]
public sealed class MobileSessionController(
    MobileSessionService sessions,
    AuditService audit) : ControllerBase
{
    [HttpPost("logout")]
    [EnableRateLimiting("mobile-refresh")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!MobileClaims.TryGetSessionDatabaseId(User, out var sessionDatabaseId) ||
            !MobileClaims.TryGetOrganizationId(User, out var organizationId))
        {
            return Unauthorized(new MobileApiError(
                "SESSION_EXPIRED",
                "الجلسة غير صالحة.",
                "The mobile session is invalid."));
        }

        await sessions.RevokeAsync(sessionDatabaseId, ct);
        await audit.WriteAsync(
            "MOBILE_AUTH_LOGOUT",
            "MobileSession",
            sessionDatabaseId.ToString(),
            $"OrganizationId={organizationId}",
            ct);
        return NoContent();
    }
}
