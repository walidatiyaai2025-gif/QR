using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SecureQrPortal.ViewModels;

public sealed class OrganizationAdminEditVm
{
    public long Id { get; set; }

    [Required, MaxLength(200)]
    public string NameArabic { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string NameEnglish { get; set; } = string.Empty;

    // Allow a human-friendly +965 format at the boundary. The controller stores
    // only the canonical 965xxxxxxxx representation used by mobile login.
    [MaxLength(32)]
    public string? MobileNumber { get; set; }

    public bool IsActive { get; set; } = true;

    [BindNever]
    public bool IsDemo { get; set; }

    [BindNever]
    public List<MobileDeviceAdminVm> Devices { get; set; } = [];

    public bool HasRegisteredMobile => !string.IsNullOrWhiteSpace(MobileNumber);
    public bool HasActiveDevice => Devices.Any(x => x.IsActive);
    public bool HasPushDevice => Devices.Any(x => x.IsActive && x.PushEnabled);
    public bool IsMobileReady => IsActive && HasRegisteredMobile && HasPushDevice;
}

public sealed class OrganizationAdminListItemVm
{
    public long Id { get; init; }
    public required string NameArabic { get; init; }
    public required string NameEnglish { get; init; }
    public string? MobileNumber { get; init; }
    public bool IsActive { get; init; }
    public bool IsDemo { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public int ActiveDeviceCount { get; init; }
    public bool HasPushDevice { get; init; }
    public DateTime? LastSeenAtUtc { get; init; }
    public bool IsMobileReady => IsActive && !string.IsNullOrWhiteSpace(MobileNumber) && HasPushDevice;
}

public sealed class MobileDeviceAdminVm
{
    public required string MaskedDeviceId { get; init; }
    public required string Platform { get; init; }
    public required string AppVersion { get; init; }
    public bool PushEnabled { get; init; }
    public DateTime RegisteredAtUtc { get; init; }
    public DateTime LastSeenAtUtc { get; init; }
    public DateTime? DeactivatedAtUtc { get; init; }
    public bool IsActive => DeactivatedAtUtc is null;
}
