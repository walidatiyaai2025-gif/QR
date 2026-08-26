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
public sealed class QrShareController : Controller
{
    private readonly QrShareService shares;
    private readonly TokenService tokens;
    private readonly QrShareRuntimeInspector inspector;
    private readonly TimeProvider clock;
    private readonly IDataProtector _receiptProtector;

    public QrShareController(
        QrShareService shares,
        TokenService tokens,
        IDataProtectionProvider protection,
        QrShareRuntimeInspector inspector)
        : this(shares, tokens, protection, inspector, TimeProvider.System)
    {
    }

    public QrShareController(
        QrShareService shares,
        TokenService tokens,
        IDataProtectionProvider protection,
        QrShareRuntimeInspector inspector,
        TimeProvider clock)
    {
        this.shares = shares;
        this.tokens = tokens;
        this.inspector = inspector;
        this.clock = clock;
        _receiptProtector = protection.CreateProtector("SecureQrPortal.QrShare.Receipt.v2");
    }

    [HttpGet("{token}")]
    public async Task<IActionResult> Open(string token, bool inspect = false, CancellationToken ct = default)
    {
        var share = await shares.FindByTokenAsync(token, ct);
        var snapshot = await CaptureAsync("OPEN_GET_LOOKUP", token, share, null, share is null ? "TOKEN_NOT_FOUND" : "TOKEN_FOUND");
        AttachInspector(inspect, snapshot);
        if (share is null) return View("Unavailable");

        if (TryGetValidCookieReceipt(share, out _))
        {
            snapshot = await CaptureAsync("OPEN_GET_COOKIE_RESUME", token, share, null, "VALID_RECEIPT");
            AttachInspector(inspect, snapshot);
            PrepareSensitiveResponse();
            return View("Reveal", BuildRevealVm(share));
        }

        var now = QrShareTime.UtcNow(clock);
        var canReveal = QrShareTime.CanReveal(share, now);
        var ar = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        ViewBag.RevealRequestId = Guid.NewGuid().ToString("N");
        ViewBag.RuntimeInspectorEnabled = inspect;

        snapshot = await CaptureAsync(
            "OPEN_GET_RENDER",
            token,
            share,
            null,
            canReveal ? "CAN_REVEAL" : BlockReason(share));
        AttachInspector(inspect, snapshot);

        return View("Open", new QrShareLandingVm
        {
            Share = share,
            CanReveal = canReveal,
            Organization = ar ? share.SecurePage.Organization.NameArabic : share.SecurePage.Organization.NameEnglish,
            PageTitle = ar ? share.SecurePage.TitleArabic : share.SecurePage.TitleEnglish
        });
    }

    [HttpPost("{token}/reveal")]
    public async Task<IActionResult> Reveal(
        string token,
        string? revealRequestId,
        bool inspect = false,
        CancellationToken ct = default)
    {
        var existing = await shares.FindByTokenAsync(token, ct);
        var beforeCount = existing?.CurrentOpenCount;
        var snapshot = await CaptureAsync(
            "REVEAL_POST_START",
            token,
            existing,
            revealRequestId,
            existing is null ? "TOKEN_NOT_FOUND" : "REQUEST_RECEIVED");
        AttachInspector(inspect, snapshot);

        if (existing is not null && TryGetValidCookieReceipt(existing, out _))
        {
            snapshot = await CaptureAsync("REVEAL_POST_COOKIE_RESUME", token, existing, revealRequestId, "VALID_RECEIPT");
            AttachInspector(inspect, snapshot);
            PrepareSensitiveResponse();
            return View("Reveal", BuildRevealVm(existing));
        }

        revealRequestId = string.IsNullOrWhiteSpace(revealRequestId)
            ? Guid.NewGuid().ToString("N")
            : revealRequestId;

        var result = await shares.RevealAsync(token, revealRequestId, ct);
        if (result is null)
        {
            var after = await shares.FindByTokenAsync(token, ct);
            snapshot = await CaptureAsync(
                "REVEAL_POST_REJECTED",
                token,
                after,
                revealRequestId,
                after is null ? "TOKEN_NOT_FOUND" : BlockReason(after),
                $"beforeCount={(beforeCount?.ToString() ?? "null")}; afterCount={(after?.CurrentOpenCount.ToString() ?? "null")}");
            AttachInspector(inspect, snapshot);
            return View("Unavailable");
        }

        var receipt = CreateRevealReceipt(result.Share);
        WriteRevealReceiptCookie(result.Share, receipt);

        snapshot = await CaptureAsync(
            "REVEAL_POST_SUCCESS",
            token,
            result.Share,
            revealRequestId,
            "CREDENTIALS_RENDERED",
            $"beforeCount={(beforeCount?.ToString() ?? "null")}; afterCount={result.Share.CurrentOpenCount}");
        AttachInspector(inspect, snapshot);
        PrepareSensitiveResponse();
        return View("Reveal", BuildRevealVm(result.Share));
    }

    [HttpGet("{token}/credentials")]
    public async Task<IActionResult> Credentials(
        string token,
        string? receipt,
        bool inspect = false,
        CancellationToken ct = default)
    {
        var share = await shares.FindByTokenAsync(token, ct);
        var snapshot = await CaptureAsync(
            "CREDENTIALS_GET_START",
            token,
            share,
            null,
            share is null ? "TOKEN_NOT_FOUND" : "REQUEST_RECEIVED");
        AttachInspector(inspect, snapshot);
        if (share is null) return View("Unavailable");

        if (!TryValidateRevealReceipt(share, receipt))
        {
            if (!TryGetValidCookieReceipt(share, out receipt))
            {
                snapshot = await CaptureAsync("CREDENTIALS_GET_REJECTED", token, share, null, "NO_VALID_RECEIPT");
                AttachInspector(inspect, snapshot);
                return View("Unavailable");
            }
        }

        WriteRevealReceiptCookie(share, receipt!);
        snapshot = await CaptureAsync("CREDENTIALS_GET_SUCCESS", token, share, null, "CREDENTIALS_RENDERED");
        AttachInspector(inspect, snapshot);
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

        var utcEnd = QrShareTime.FromStorage(accessEnd);
        var payload = $"{share.Id}|{share.TokenHash}|{utcEnd.Ticks}";
        return _receiptProtector.Protect(payload);
    }

    private void WriteRevealReceiptCookie(QrShareLink share, string receipt)
    {
        if (share.AccessWindowEndsAtUtc is not DateTime accessEnd)
            return;

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
                Expires = QrShareTime.ToCookieExpiry(accessEnd)
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
            share.AccessWindowEndsAtUtc is not DateTime accessEnd)
            return false;

        var utcEnd = QrShareTime.FromStorage(accessEnd);
        if (utcEnd <= QrShareTime.UtcNow(clock))
            return false;

        try
        {
            var payload = _receiptProtector.Unprotect(receipt);
            var parts = payload.Split('|', 3);
            if (parts.Length != 3 ||
                !long.TryParse(parts[0], out var shareId) ||
                !long.TryParse(parts[2], out var expiryTicks))
                return false;

            return shareId == share.Id &&
                   expiryTicks == utcEnd.Ticks &&
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

    private async Task<string> CaptureAsync(
        string stage,
        string? token,
        QrShareLink? share,
        string? revealRequestId,
        string? outcome,
        string? note = null) =>
        await inspector.CaptureAsync(
            HttpContext,
            stage,
            token,
            share,
            revealRequestId,
            outcome,
            note,
            CancellationToken.None);

    private void AttachInspector(bool enabled, string snapshot)
    {
        ViewBag.RuntimeInspectorEnabled = enabled;
        if (!enabled) return;
        ViewBag.RuntimeInspector = snapshot;
        ViewBag.RuntimeInspectorLogPath = inspector.LogFilePath;
    }

    private string BlockReason(QrShareLink share) =>
        QrShareTime.BlockReason(share, QrShareTime.UtcNow(clock));

    private void PrepareSensitiveResponse()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers["X-QR-Share-Flow"] = "runtime-inspector-v1";
    }

    private static string ReceiptCookieName(long shareId) => $"SecureQrPortal.ShareReceipt.{shareId}";
}
