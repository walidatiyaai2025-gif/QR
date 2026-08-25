using System.ComponentModel.DataAnnotations;

namespace SecureQrPortal.Models;

public sealed class PageCredential
{
    public long Id { get; set; }
    public long SecurePageId { get; set; }
    public SecurePage SecurePage { get; set; } = null!;
    [MaxLength(150)] public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
