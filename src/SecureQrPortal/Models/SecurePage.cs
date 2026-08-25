using System.ComponentModel.DataAnnotations;

namespace SecureQrPortal.Models;

public sealed class SecurePage
{
    public long Id { get; set; }
    public long OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [MaxLength(32)] public string QrReference { get; set; } = string.Empty;
    [MaxLength(64)] public string PublicTokenHash { get; set; } = string.Empty;
    public string ProtectedPublicToken { get; set; } = string.Empty;
    public DateTime CurrentTokenCreatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(250)] public string TitleArabic { get; set; } = string.Empty;
    [MaxLength(250)] public string TitleEnglish { get; set; } = string.Empty;
    public string ContentArabicHtml { get; set; } = string.Empty;
    public string ContentEnglishHtml { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public AccessLimitMode AccessLimitMode { get; set; } = AccessLimitMode.MaximumSuccessfulAccesses;
    public long? MaxAccessCount { get; set; }
    public long CurrentSuccessfulAccessCount { get; set; }
    public long CurrentQrOpenCount { get; set; }
    public long CurrentSuccessfulLoginCount { get; set; }
    public long CurrentFailedLoginCount { get; set; }
    public DateTime? LastQrScanAtUtc { get; set; }
    public DateTime? LastSuccessfulAccessAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    [MaxLength(450)] public string? CreatedByAdminId { get; set; }
    [MaxLength(450)] public string? LastModifiedByAdminId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDemo { get; set; }
    [MaxLength(36)] public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    public PageCredential? Credential { get; set; }
    public ICollection<AccessLog> AccessLogs { get; set; } = new List<AccessLog>();
    public ICollection<QrTokenHistory> TokenHistory { get; set; } = new List<QrTokenHistory>();
}
