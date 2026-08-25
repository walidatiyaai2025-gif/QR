using System.ComponentModel.DataAnnotations;

namespace SecureQrPortal.Models;

public sealed class AuditLog
{
    public long Id { get; set; }
    [MaxLength(450)] public string? AdminUserId { get; set; }
    [MaxLength(120)] public string Action { get; set; } = string.Empty;
    [MaxLength(120)] public string EntityType { get; set; } = string.Empty;
    [MaxLength(120)] public string? EntityId { get; set; }
    [MaxLength(2000)] public string? Details { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(64)] public string? IpAddress { get; set; }
}
