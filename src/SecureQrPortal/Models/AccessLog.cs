using System.ComponentModel.DataAnnotations;

namespace SecureQrPortal.Models;

public sealed class AccessLog
{
    public long Id { get; set; }
    public long? SecurePageId { get; set; }
    public SecurePage? SecurePage { get; set; }
    [MaxLength(60)] public string EventType { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(64)] public string? IpAddress { get; set; }
    [MaxLength(700)] public string? UserAgent { get; set; }
    [MaxLength(80)] public string? DeviceType { get; set; }
    [MaxLength(80)] public string? Browser { get; set; }
    [MaxLength(80)] public string? Country { get; set; }
    public bool WasSuccessful { get; set; }
    [MaxLength(250)] public string? FailureReasonInternal { get; set; }
}
