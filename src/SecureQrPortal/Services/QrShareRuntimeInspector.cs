using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using SecureQrPortal.Models;

namespace SecureQrPortal.Services;

public sealed class QrShareRuntimeInspector(IWebHostEnvironment environment)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private const long MaxLogBytes = 5 * 1024 * 1024;

    public string LogFilePath => Path.Combine(environment.ContentRootPath, "logs", "qr-share-runtime-inspector.txt");

    public async Task<string> CaptureAsync(
        HttpContext context,
        string stage,
        string? rawToken,
        QrShareLink? share,
        string? revealRequestId = null,
        string? outcome = null,
        string? note = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tokenFingerprint = Fingerprint(rawToken);
        var requestFingerprint = Fingerprint(revealRequestId);
        var receiptPresent = share is not null && context.Request.Cookies.ContainsKey($"SecureQrPortal.ShareReceipt.{share.Id}");
        var userAgent = Clean(context.Request.Headers.UserAgent.ToString(), 180);

        var snapshot = string.Join(" | ", new[]
        {
            $"utc={now:O}",
            $"pid={Environment.ProcessId}",
            $"trace={Clean(context.TraceIdentifier, 80)}",
            $"stage={Clean(stage, 80)}",
            $"method={context.Request.Method}",
            $"https={context.Request.IsHttps}",
            $"host={Clean(context.Request.Host.Value, 120)}",
            $"tokenFp={tokenFingerprint}",
            $"shareId={(share?.Id.ToString() ?? "null")}",
            $"pageId={(share?.SecurePageId.ToString() ?? "null")}",
            $"count={(share is null ? "null" : $"{share.CurrentOpenCount}/{share.MaxOpenCount}")}",
            $"expiresUtc={FormatStoredUtc(share?.ExpiresAtUtc)}",
            $"revokedUtc={FormatStoredUtc(share?.RevokedAtUtc)}",
            $"windowEndsUtc={FormatStoredUtc(share?.AccessWindowEndsAtUtc)}",
            $"expiresKind={(share is null ? "null" : share.ExpiresAtUtc.Kind.ToString())}",
            $"windowKind={(share?.AccessWindowEndsAtUtc?.Kind.ToString() ?? "null")}",
            $"lastRevealHash={(string.IsNullOrWhiteSpace(share?.LastRevealRequestHash) ? "null" : share!.LastRevealRequestHash![..Math.Min(16, share.LastRevealRequestHash.Length)])}",
            $"revealReqFp={requestFingerprint}",
            $"receiptCookie={receiptPresent}",
            $"outcome={Clean(outcome, 120)}",
            $"note={Clean(note, 260)}",
            $"ua={userAgent}"
        });

        await Gate.WaitAsync(ct);
        try
        {
            try
            {
                var directory = Path.GetDirectoryName(LogFilePath)!;
                Directory.CreateDirectory(directory);

                if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length >= MaxLogBytes)
                {
                    var archived = Path.Combine(directory, "qr-share-runtime-inspector.previous.txt");
                    if (File.Exists(archived)) File.Delete(archived);
                    File.Move(LogFilePath, archived);
                }

                await File.AppendAllTextAsync(LogFilePath, snapshot + Environment.NewLine, Encoding.UTF8, ct);
                return snapshot + " | logWrite=OK";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return snapshot + $" | logWrite=FAILED:{ex.GetType().Name}";
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string FormatStoredUtc(DateTime? value)
    {
        if (value is not DateTime date) return "null";
        var utc = date.Kind == DateTimeKind.Utc ? date : DateTime.SpecifyKind(date, DateTimeKind.Utc);
        return utc.ToString("O");
    }

    private static string Fingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "null";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..16];
    }

    private static string Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return "null";
        var cleaned = value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/').Trim();
        return cleaned.Length <= max ? cleaned : cleaned[..max];
    }
}
