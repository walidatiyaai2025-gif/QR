namespace SecureQrPortal.ViewModels;

public sealed class OrganizationMobileAdminRowVm
{
    public long Id { get; init; }
    public string NameArabic { get; init; } = string.Empty;
    public string NameEnglish { get; init; } = string.Empty;
    public string? MobileNumber { get; init; }
    public bool IsActive { get; init; }
    public bool IsDemo { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public int RegisteredDeviceCount { get; init; }
    public int ActiveDeviceCount { get; init; }
}

public sealed class MobileDeviceAdminVm
{
    public string Platform { get; init; } = string.Empty;
    public string AppVersion { get; init; } = string.Empty;
    public bool PushEnabled { get; init; }
    public DateTime RegisteredAtUtc { get; init; }
    public DateTime LastSeenAtUtc { get; init; }
    public DateTime? DeactivatedAtUtc { get; init; }
    public bool IsActive => !DeactivatedAtUtc.HasValue;
}

public sealed class MobileDeliverySendVm
{
    public long SecurePageId { get; set; }
    public DateTime? ExpiresAtLocal { get; set; }
    public bool ReminderEnabled { get; set; }
    public int? ReminderInterval { get; set; }
    public string ReminderUnit { get; set; } = "Minutes";
}

public sealed class MobileDeliveryHistoryItemVm
{
    public long Id { get; init; }
    public long SecurePageId { get; init; }
    public string QrReference { get; init; } = string.Empty;
    public long OrganizationId { get; init; }
    public string OrganizationArabic { get; init; } = string.Empty;
    public string OrganizationEnglish { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? SentAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public string DeliveryStatus { get; init; } = string.Empty;
    public string? FirebaseStatus { get; init; }
    public bool ReminderEnabled { get; init; }
    public int? ReminderInterval { get; init; }
    public string? ReminderUnit { get; init; }
    public int ReminderCount { get; init; }
    public DateTime? LastReminderAtUtc { get; init; }
    public DateTime? NextReminderAtUtc { get; init; }
    public DateTime? FirstRevealedAtUtc { get; init; }
    public DateTime? RevokedAtUtc { get; init; }
}

public sealed class MobileDeliveryHistoryVm
{
    public IReadOnlyList<MobileDeliveryHistoryItemVm> Items { get; init; } = [];
    public long? OrganizationId { get; init; }
    public long? SecurePageId { get; init; }
    public string? Status { get; init; }
    public bool? Opened { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public int Total { get; init; }
    public string Sort { get; init; } = "created_desc";
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

public sealed class MobileDeliveryDetailsVm
{
    public MobileDeliveryHistoryItemVm Delivery { get; init; } = null!;
    public string SourceStatus { get; init; } = string.Empty;
    public long? RemainingReveals { get; init; }
    public bool UnlimitedReveals { get; init; }
}

public sealed class QrMobileDeliveryPanelVm
{
    public long SecurePageId { get; init; }
    public bool OrganizationActive { get; init; }
    public string? OrganizationMobileNumber { get; init; }
    public int RegisteredDeviceCount { get; init; }
    public int ActiveDeviceCount { get; init; }
    public IReadOnlyList<MobileDeviceAdminVm> Devices { get; init; } = [];
    public MobileDeliveryHistoryItemVm? LatestDelivery { get; init; }
}
