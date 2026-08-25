using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;

namespace SecureQrPortal.Services;

public sealed class SecurePageAccessService(ApplicationDbContext db, TokenService tokens, QrStatusService status, DeviceInfoService devices)
{
    public async Task<SecurePage?> FindByTokenAsync(string token, CancellationToken ct = default)
    {
        var hash = TokenService.HashToken(token);
        return await db.SecurePages.Include(x => x.Organization).Include(x => x.Credential).SingleOrDefaultAsync(x => x.PublicTokenHash == hash, ct);
    }

    public async Task<QrStatus?> RegisterQrOpenAsync(SecurePage page, HttpContext http, CancellationToken ct = default)
    {
        var current = status.GetStatus(page);
        if (current != QrStatus.ACTIVE) { await LogStatusAsync(page, current, http, ct); return current; }

        if ((page.AccessLimitMode is AccessLimitMode.MaximumQrOpens or AccessLimitMode.ExpiryAndQrOpens) && page.MaxAccessCount.HasValue)
        {
            var max = page.MaxAccessCount.Value;
            var affected = await db.SecurePages.Where(x => x.Id == page.Id && x.CurrentQrOpenCount < max)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentQrOpenCount, x => x.CurrentQrOpenCount + 1)
                    .SetProperty(x => x.LastQrScanAtUtc, DateTime.UtcNow), ct);
            if (affected == 0) { await AddLogAsync(page.Id, AccessEventType.LIMIT_REACHED, false, "QR open limit reached", http, ct); return QrStatus.LIMIT_REACHED; }
        }
        else
        {
            await db.SecurePages.Where(x => x.Id == page.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CurrentQrOpenCount, x => x.CurrentQrOpenCount + 1)
                .SetProperty(x => x.LastQrScanAtUtc, DateTime.UtcNow), ct);
        }
        await AddLogAsync(page.Id, AccessEventType.QR_OPEN, true, null, http, ct);
        return QrStatus.ACTIVE;
    }

    public async Task<bool> VerifyCredentialsAsync(SecurePage page, string username, string password, HttpContext http, CancellationToken ct = default)
    {
        var credentialState = status.GetStatus(page);
        var qrOpenLimitSessionMayProceed = credentialState == QrStatus.LIMIT_REACHED && (page.AccessLimitMode is AccessLimitMode.MaximumQrOpens or AccessLimitMode.ExpiryAndQrOpens);
        if ((credentialState != QrStatus.ACTIVE && !qrOpenLimitSessionMayProceed) || page.Credential is null) return false;
        var okUser = string.Equals(page.Credential.Username, username.Trim(), StringComparison.OrdinalIgnoreCase);
        var hasher = new PasswordHasher<PageCredential>();
        var okPassword = okUser && hasher.VerifyHashedPassword(page.Credential, page.Credential.PasswordHash, password) != PasswordVerificationResult.Failed;
        if (okPassword)
        {
            await db.SecurePages.Where(x => x.Id == page.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentSuccessfulLoginCount, x => x.CurrentSuccessfulLoginCount + 1), ct);
            await AddLogAsync(page.Id, AccessEventType.LOGIN_SUCCESS, true, null, http, ct);
            return true;
        }
        await db.SecurePages.Where(x => x.Id == page.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentFailedLoginCount, x => x.CurrentFailedLoginCount + 1), ct);
        await AddLogAsync(page.Id, AccessEventType.LOGIN_FAILURE, false, "Invalid page credential", http, ct);
        return false;
    }

    public async Task<QrStatus> RegisterSuccessfulAccessAsync(SecurePage page, HttpContext http, CancellationToken ct = default)
    {
        var current = status.GetStatus(page);
        if (current != QrStatus.ACTIVE) return current;
        if ((page.AccessLimitMode is AccessLimitMode.MaximumSuccessfulAccesses or AccessLimitMode.ExpiryAndSuccessfulAccesses) && page.MaxAccessCount.HasValue)
        {
            var max = page.MaxAccessCount.Value;
            var affected = await db.SecurePages.Where(x => x.Id == page.Id && x.CurrentSuccessfulAccessCount < max)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentSuccessfulAccessCount, x => x.CurrentSuccessfulAccessCount + 1)
                    .SetProperty(x => x.LastSuccessfulAccessAtUtc, DateTime.UtcNow), ct);
            if (affected == 0) { await AddLogAsync(page.Id, AccessEventType.LIMIT_REACHED, false, "Successful access limit reached", http, ct); return QrStatus.LIMIT_REACHED; }
        }
        else
        {
            await db.SecurePages.Where(x => x.Id == page.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CurrentSuccessfulAccessCount, x => x.CurrentSuccessfulAccessCount + 1)
                .SetProperty(x => x.LastSuccessfulAccessAtUtc, DateTime.UtcNow), ct);
        }
        await AddLogAsync(page.Id, AccessEventType.PAGE_VIEW, true, null, http, ct);
        return QrStatus.ACTIVE;
    }

    private async Task LogStatusAsync(SecurePage page, QrStatus state, HttpContext http, CancellationToken ct)
    {
        var type = state switch { QrStatus.EXPIRED => AccessEventType.TOKEN_EXPIRED, QrStatus.REVOKED => AccessEventType.TOKEN_REVOKED, QrStatus.LIMIT_REACHED => AccessEventType.LIMIT_REACHED, QrStatus.NOT_STARTED => AccessEventType.TOKEN_NOT_STARTED, QrStatus.DISABLED => AccessEventType.TOKEN_DISABLED, _ => AccessEventType.TOKEN_INVALID };
        await AddLogAsync(page.Id, type, false, state.ToString(), http, ct);
    }

    public async Task AddInvalidTokenLogAsync(HttpContext http, CancellationToken ct = default) => await AddLogAsync(null, AccessEventType.TOKEN_INVALID, false, "Unknown token", http, ct);

    private async Task AddLogAsync(long? pageId, AccessEventType type, bool success, string? reason, HttpContext http, CancellationToken ct)
    {
        var ua = http.Request.Headers.UserAgent.ToString(); var d = devices.Parse(ua);
        db.AccessLogs.Add(new AccessLog { SecurePageId = pageId, EventType = type.ToString(), TimestampUtc = DateTime.UtcNow, IpAddress = http.Connection.RemoteIpAddress?.ToString(), UserAgent = ua, DeviceType = d.DeviceType, Browser = d.Browser, WasSuccessful = success, FailureReasonInternal = reason });
        await db.SaveChangesAsync(ct);
    }
}
