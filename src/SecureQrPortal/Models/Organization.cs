using System.ComponentModel.DataAnnotations;

namespace SecureQrPortal.Models;

public sealed class Organization
{
    public long Id { get; set; }
    [MaxLength(200)] public string NameArabic { get; set; } = string.Empty;
    [MaxLength(200)] public string NameEnglish { get; set; } = string.Empty;
    [MaxLength(400)] public string? LogoPath { get; set; }
    [MaxLength(11)] public string? MobileNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDemo { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<SecurePage> SecurePages { get; set; } = new List<SecurePage>();
}
