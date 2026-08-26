using System.Security.Claims;
using SecureQrPortal.Data;
using SecureQrPortal.Models;

namespace SecureQrPortal.Services;

public sealed class AuditService(ApplicationDbContext db, IHttpContextAccessor accessor)
{
    public async Task WriteAsync(string action, string entityType, string? entityId = null, string? details = null, CancellationToken ct = default)
    {
        var http = accessor.HttpContext;
        db.AuditLogs.Add(new AuditLog
        {
            AdminUserId = http?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            TimestampUtc = DateTime.UtcNow,
            IpAddress = http?.Connection.RemoteIpAddress?.ToString()
        });
        await db.SaveChangesAsync(ct);
    }
}

