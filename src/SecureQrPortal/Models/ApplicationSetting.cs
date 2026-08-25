using System.ComponentModel.DataAnnotations;

namespace SecureQrPortal.Models;

public sealed class ApplicationSetting
{
    public long Id { get; set; }
    [MaxLength(160)] public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
