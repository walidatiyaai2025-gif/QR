using System.ComponentModel.DataAnnotations;

namespace SecureQrPortal.Models;

public sealed class QrTokenHistory
{
    public long Id { get; set; }
    public long SecurePageId { get; set; }
    public SecurePage SecurePage { get; set; } = null!;
    [MaxLength(64)] public string PreviousTokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime RevokedAtUtc { get; set; }
    [MaxLength(450)] public string? RevokedByAdminId { get; set; }
    [MaxLength(250)] public string? RevocationReason { get; set; }
    public DateTime? ReplacementTokenCreatedAtUtc { get; set; }
}
