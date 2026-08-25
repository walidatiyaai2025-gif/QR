using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Security;
using SecureQrPortal.Services;

namespace SecureQrPortal.Controllers;

public sealed record MobileApiError(string Code, string MessageArabic, string MessageEnglish);
public sealed record RequestOtpRequest(string? MobileNumber);
public sealed record VerifyOtpRequest(string? ChallengeId, string? Otp);
public sealed record RefreshMobileSessionRequest(string? RefreshToken);
public sealed record RegisterMobileDeviceRequest(string? DeviceId, string? FcmToken, string? Platform, string? AppVersion, bool PushEnabled = true);
public sealed record SecureMessageAuthenticateRequest(string? Username, string? Password);
public sealed record SecureMessageRevealRequest(string? RevealToken);

[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/mobile/auth")]
public sealed class MobileAuthController(
    MobileOtpService otp,
    MobileSessionService sessions,
    AuditService audit) : ControllerBase
{
    [HttpPost("request-otp")]
    [EnableRateLimiting("mobile-otp-request")]
    public async Task<IActionResult> RequestOtp([FromBody] RequestOtpRequest request, CancellationToken ct)
    {
        var result = await otp.RequestAsync(request.MobileNumber, ct);
        return result.Status switch
        {
            MobileOtpRequestStatus.Accepted => Accepted(new
            {
                code = "OTP_REQUEST_ACCEPTED",
                challengeId = result.ChallengeId,
                expiresAtUtc = result.ExpiresAtUtc,
                resendAvailableAtUtc = result.ResendAvailableAtUtc
            }),
            MobileOtpRequestStatus.InvalidMobile => BadRequest(Error("INVALID_MOBILE",
                "رقم الهاتف غير صالح.", "The mobile number format is invalid.")),
            MobileOtpRequestStatus.Cooldown => StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = Error("OTP_RESEND_COOLDOWN", "يرجى الانتظار قبل طلب رمز جديد.", "Please wait before requesting another code."),
                retryAfterSeconds = result.RetryAfterSeconds
            }),
            MobileOtpRequestStatus.RateLimited => StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = Error("OTP_RATE_LIMIT", "تم تجاوز عدد محاولات طلب الرمز مؤقتاً.", "Too many OTP requests. Try again later."),
                retryAfterSeconds = result.RetryAfterSeconds
            }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, Error("API_ERROR", "تعذر إتمام الطلب.", "The request could not be completed."))
        };
    }

    [HttpPost("verify-otp")]
    [EnableRateLimiting("mobile-otp-verify")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request, CancellationToken ct)
    {
        var result = await otp.VerifyAsync(request.ChallengeId, request.Otp, ct);
        return result.Status switch
        {
            MobileOtpVerifyStatus.Success => Ok(SessionResponse(result.Session!)),
            MobileOtpVerifyStatus.Expired => BadRequest(Error("OTP_EXPIRED", "انتهت صلاحية رمز التحقق.", "The verification code has expired.")),
            MobileOtpVerifyStatus.TooManyAttempts => StatusCode(StatusCodes.Status429TooManyRequests,
                Error("OTP_ATTEMPT_LIMIT", "تم تجاوز عدد محاولات التحقق.", "Too many verification attempts.")),
            _ => BadRequest(Error("INVALID_OTP", "رمز التحقق غير صحيح أو غير صالح.", "The verification code is invalid or no longer usable."))
        };
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("mobile-refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshMobileSessionRequest request, CancellationToken ct)
    {
        var refreshed = await sessions.RefreshAsync(request.RefreshToken, ct);
        if (refreshed is null)
        {
            await audit.WriteAsync("MOBILE_AUTH_FAILED", "MobileSession", null, "Refresh rejected.", ct);
            return Unauthorized(Error("SESSION_EXPIRED", "انتهت الجلسة أو تم إلغاؤها.", "The session has expired or was revoked."));
        }
        return Ok(SessionResponse(refreshed));
    }

    private static object SessionResponse(MobileSessionTokens session) => new
    {
        code = "AUTHENTICATED",
        accessToken = session.AccessToken,
        accessExpiresAtUtc = session.AccessExpiresAtUtc,
        refreshToken = session.RefreshToken,
        refreshExpiresAtUtc = session.RefreshExpiresAtUtc,
        sessionId = session.SessionId,
        organization = new
        {
            id = session.OrganizationId,
            nameArabic = session.OrganizationNameArabic,
            nameEnglish = session.OrganizationNameEnglish
        }
    };

    private static MobileApiError Error(string code, string ar, string en) => new(code, ar, en);
}

[ApiController]
[IgnoreAntiforgeryToken]
[Authorize(AuthenticationSchemes = MobileBearerDefaults.Scheme)]
[Route("api/mobile/devices")]
public sealed class MobileDevicesController(MobileDeviceService devices) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterMobileDeviceRequest request, CancellationToken ct)
    {
        if (!MobileClaims.TryGetOrganizationId(User, out var organizationId))
            return Unauthorized(Error("SESSION_EXPIRED", "الجلسة غير صالحة.", "The mobile session is invalid."));

        var result = await devices.RegisterAsync(organizationId, request.DeviceId, request.FcmToken,
            request.Platform, request.AppVersion, request.PushEnabled, ct);
        return result.Status switch
        {
            MobileDeviceRegistrationStatus.Success => Ok(new
            {
                code = "DEVICE_REGISTERED",
                deviceId = result.DeviceDatabaseId,
                registeredAtUtc = result.RegisteredAtUtc,
                lastSeenAtUtc = result.LastSeenAtUtc,
                pushEnabled = result.PushEnabled
            }),
            MobileDeviceRegistrationStatus.Conflict => Conflict(Error("DEVICE_OWNERSHIP_CONFLICT",
                "تسجيل الجهاز مرتبط بجلسة جهة أخرى.", "The device registration conflicts with another organization session.")),
            _ => BadRequest(Error("INVALID_DEVICE", "بيانات الجهاز غير صالحة.", "The device registration data is invalid."))
        };
    }

    private static MobileApiError Error(string code, string ar, string en) => new(code, ar, en);
}

[ApiController]
[IgnoreAntiforgeryToken]
[Authorize(AuthenticationSchemes = MobileBearerDefaults.Scheme)]
[Route("api/mobile/me")]
public sealed class MobileMeController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!MobileClaims.TryGetOrganizationId(User, out var organizationId) ||
            !MobileClaims.TryGetSessionDatabaseId(User, out var sessionDatabaseId))
            return Unauthorized(new MobileApiError("SESSION_EXPIRED", "الجلسة غير صالحة.", "The mobile session is invalid."));

        var organization = await db.Organizations.AsNoTracking()
            .Where(x => x.Id == organizationId && x.IsActive)
            .Select(x => new { x.Id, x.NameArabic, x.NameEnglish })
            .SingleOrDefaultAsync(ct);
        if (organization is null)
            return Unauthorized(new MobileApiError("SESSION_EXPIRED", "الجلسة غير صالحة.", "The mobile session is invalid."));

        var registeredDevices = await db.MobileDevices.AsNoTracking()
            .CountAsync(x => x.OrganizationId == organizationId && x.DeactivatedAtUtc == null, ct);
        var session = await db.MobileSessions.AsNoTracking()
            .Where(x => x.Id == sessionDatabaseId && x.OrganizationId == organizationId)
            .Select(x => new { x.SessionId, x.AccessExpiresAtUtc, x.RefreshExpiresAtUtc })
            .SingleAsync(ct);

        return Ok(new
        {
            organization = new { id = organization.Id, nameArabic = organization.NameArabic, nameEnglish = organization.NameEnglish },
            session,
            registeredDeviceCount = registeredDevices
        });
    }
}

[ApiController]
[IgnoreAntiforgeryToken]
[Authorize(AuthenticationSchemes = MobileBearerDefaults.Scheme)]
[Route("api/mobile/inbox")]
public sealed class MobileInboxController(MobileDeliveryAccessService deliveries) : ControllerBase
{
    private const string HeadingArabic = "لديك رسالة جديدة اضغط هنا لاستعراض الرسالة";
    private const string HeadingEnglish = "You have a new message. Tap here to view it.";

    [HttpGet]
    public async Task<IActionResult> Inbox([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!MobileClaims.TryGetOrganizationId(User, out var organizationId))
            return Unauthorized(Error("SESSION_EXPIRED", "الجلسة غير صالحة.", "The mobile session is invalid."));
        var result = await deliveries.GetInboxAsync(organizationId, page, pageSize, ct);
        return Ok(new
        {
            headingArabic = HeadingArabic,
            headingEnglish = HeadingEnglish,
            result.Page,
            result.PageSize,
            result.TotalCount,
            items = result.Items
        });
    }

    [HttpGet("{deliveryId:long}")]
    public async Task<IActionResult> Details(long deliveryId, CancellationToken ct)
    {
        if (!MobileClaims.TryGetOrganizationId(User, out var organizationId))
            return Unauthorized(Error("SESSION_EXPIRED", "الجلسة غير صالحة.", "The mobile session is invalid."));
        var (_, details) = await deliveries.GetDetailsAsync(organizationId, deliveryId, ct);
        if (details is null) return NotFound(Error("DELIVERY_NOT_FOUND", "الرسالة غير موجودة.", "The delivery was not found."));
        return Ok(new { headingArabic = HeadingArabic, headingEnglish = HeadingEnglish, delivery = details });
    }

    [HttpPost("{deliveryId:long}/authenticate")]
    [EnableRateLimiting("mobile-secure-auth")]
    public async Task<IActionResult> Authenticate(long deliveryId, [FromBody] SecureMessageAuthenticateRequest request, CancellationToken ct)
    {
        if (!MobileClaims.TryGetOrganizationId(User, out var organizationId) ||
            !MobileClaims.TryGetSessionDatabaseId(User, out var mobileSessionId))
            return Unauthorized(Error("SESSION_EXPIRED", "الجلسة غير صالحة.", "The mobile session is invalid."));

        var result = await deliveries.AuthenticateAsync(organizationId, mobileSessionId, deliveryId,
            request.Username, request.Password, HttpContext, ct);
        if (result.Status == MobileDeliveryAccessStatus.Success)
            return Ok(new { code = "SECURE_AUTHENTICATED", revealToken = result.RevealToken, revealExpiresAtUtc = result.RevealExpiresAtUtc });
        return DeliveryError(result.Status);
    }

    [HttpPost("{deliveryId:long}/reveal")]
    public async Task<IActionResult> Reveal(long deliveryId, [FromBody] SecureMessageRevealRequest request, CancellationToken ct)
    {
        if (!MobileClaims.TryGetOrganizationId(User, out var organizationId) ||
            !MobileClaims.TryGetSessionDatabaseId(User, out var mobileSessionId))
            return Unauthorized(Error("SESSION_EXPIRED", "الجلسة غير صالحة.", "The mobile session is invalid."));

        var result = await deliveries.RevealAsync(organizationId, mobileSessionId, deliveryId,
            request.RevealToken, HttpContext, ct);
        if (result.Status == MobileDeliveryAccessStatus.Success)
            return Ok(new
            {
                code = "SECURE_MESSAGE_REVEALED",
                headingArabic = HeadingArabic,
                headingEnglish = HeadingEnglish,
                contentArabicHtml = result.ContentArabicHtml,
                contentEnglishHtml = result.ContentEnglishHtml,
                sentAtUtc = result.SentAtUtc,
                expiresAtUtc = result.ExpiresAtUtc,
                remainingReveals = result.RemainingReveals,
                firstRevealedAtUtc = result.FirstRevealedAtUtc,
                attachments = Array.Empty<object>()
            });
        return DeliveryError(result.Status);
    }

    private IActionResult DeliveryError(MobileDeliveryAccessStatus status) => status switch
    {
        MobileDeliveryAccessStatus.NotFound => NotFound(Error("DELIVERY_NOT_FOUND", "الرسالة غير موجودة.", "The delivery was not found.")),
        MobileDeliveryAccessStatus.InvalidCredentials => StatusCode(StatusCodes.Status403Forbidden,
            Error("INVALID_SECURE_CREDENTIALS", "اسم المستخدم أو كلمة المرور غير صحيحة.", "The secure username or password is invalid.")),
        MobileDeliveryAccessStatus.InvalidRevealGrant => StatusCode(StatusCodes.Status403Forbidden,
            Error("INVALID_REVEAL_GRANT", "انتهت صلاحية تصريح عرض الرسالة أو تم استخدامه.", "The reveal authorization is invalid, expired, or already used.")),
        MobileDeliveryAccessStatus.Expired => StatusCode(StatusCodes.Status410Gone,
            Error("DELIVERY_EXPIRED", "انتهت صلاحية الرسالة.", "The delivery has expired.")),
        MobileDeliveryAccessStatus.Revoked => StatusCode(StatusCodes.Status410Gone,
            Error("DELIVERY_REVOKED", "تم إلغاء الرسالة.", "The delivery was revoked.")),
        MobileDeliveryAccessStatus.Disabled => StatusCode(StatusCodes.Status403Forbidden,
            Error("DELIVERY_DISABLED", "الرسالة غير متاحة.", "The delivery is disabled.")),
        MobileDeliveryAccessStatus.NotStarted => Conflict(Error("DELIVERY_NOT_STARTED", "الرسالة غير متاحة بعد.", "The delivery is not active yet.")),
        MobileDeliveryAccessStatus.LimitReached => Conflict(Error("REVEAL_LIMIT_REACHED", "تم الوصول إلى الحد الأقصى لعرض الرسالة.", "The reveal limit has been reached.")),
        _ => StatusCode(StatusCodes.Status500InternalServerError, Error("API_ERROR", "تعذر إتمام الطلب.", "The request could not be completed."))
    };

    private static MobileApiError Error(string code, string ar, string en) => new(code, ar, en);
}
