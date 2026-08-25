using System.ComponentModel.DataAnnotations;
using SecureQrPortal.Models;

namespace SecureQrPortal.ViewModels;

public sealed class SecurePageEditVm
{
    public long Id { get; set; }
    [Required] public long OrganizationId { get; set; }
    [Required,MaxLength(250)] public string TitleArabic { get; set; }="";
    [Required,MaxLength(250)] public string TitleEnglish { get; set; }="";
    public string ContentArabicHtml { get; set; }="";
    public string ContentEnglishHtml { get; set; }="";
    public bool IsActive { get; set; } = true;
    public DateTime? ValidFromLocal { get; set; }
    public DateTime? ExpiresAtLocal { get; set; }
    public AccessLimitMode AccessLimitMode { get; set; } = AccessLimitMode.MaximumSuccessfulAccesses;
    [Range(1,long.MaxValue)] public long? MaxAccessCount { get; set; } = 100;
    [Required,MaxLength(150)] public string PageUsername { get; set; }="";
    [DataType(DataType.Password)] public string? PagePassword { get; set; }
    public string? QrReference { get; set; }
}

public sealed class QrRegistryItemVm
{
    public long Id { get; set; }
    public string Reference { get; set; } = "";
    public string Organization { get; set; } = "";
    public string PageTitle { get; set; } = "";
    public QrStatus Status { get; set; }
    public string PublicUrl { get; set; } = "";
    public DateTime TokenCreatedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public AccessLimitMode AccessLimitMode { get; set; }
    public long? MaxAccessCount { get; set; }
    public long CurrentSuccessfulAccessCount { get; set; }
    public long CurrentQrOpenCount { get; set; }
    public long SuccessfulLoginCount { get; set; }
    public long FailedLoginCount { get; set; }
    public DateTime? LastQrScanAtUtc { get; set; }
    public DateTime? LastSuccessfulAccessAtUtc { get; set; }
    public string CreatedBy { get; set; } = "—";
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class QrRegistryVm
{
    public List<QrRegistryItemVm> Items { get; set; } = [];
    public string? Search { get; set; }
    public string? Status { get; set; }
    public long? OrganizationId { get; set; }
    public string? Activity { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
    public DateTime? ExpiryFrom { get; set; }
    public DateTime? ExpiryTo { get; set; }
    public AccessLimitMode? AccessLimitMode { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int Total { get; set; }
    public string Sort { get; set; } = "created_desc";
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

public sealed class QrDetailsVm
{
    public SecurePage Page { get; set; } = null!;
    public QrStatus Status { get; set; }
    public string PublicUrl { get; set; } = "";
    public string MaskedToken { get; set; } = "";
    public long? RemainingAccesses { get; set; }
    public List<AccessLog> Timeline { get; set; } = [];
    public List<QrTokenHistory> History { get; set; } = [];
    public string CreatedBy { get; set; } = "—";
    public string ModifiedBy { get; set; } = "—";
}

public sealed class SecurePageIndexVm
{
    public List<SecurePage> Items { get; set; } = [];
    public string? Search { get; set; }
    public string? Status { get; set; }
    public long? OrganizationId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int Total { get; set; }
    public string Sort { get; set; } = "created_desc";
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}
