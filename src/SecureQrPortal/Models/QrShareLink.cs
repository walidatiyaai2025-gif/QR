using System.ComponentModel.DataAnnotations;

namespace SecureQrPortal.Models;

public sealed class QrShareLink
{
    public long Id { get; set; }
    public long SecurePageId { get; set; }
    public SecurePage SecurePage { get; set; } = null!;

    [MaxLength(64)] public string TokenHash { get; set; } = string.Empty;
    public string ProtectedToken { get; set; } = string.Empty;
    [MaxLength(150)] public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string ProtectedPassword { get; set; } = string.Empty;
    [MaxLength(2000)] public string? MessageTemplate { get; set; }

    public int MaxOpenCount { get; set; } = 1;
    public int CurrentOpenCount { get; set; }
    public int SessionDurationMinutes { get; set; } = 15;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? FirstOpenedAtUtc { get; set; }
    public DateTime? LastOpenedAtUtc { get; set; }
    public DateTime? AccessWindowEndsAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    [MaxLength(64)] public string? LastRevealRequestHash { get; set; }

    [MaxLength(450)] public string? CreatedByAdminId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
