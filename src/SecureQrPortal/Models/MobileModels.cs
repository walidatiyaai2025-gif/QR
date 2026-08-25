using System.ComponentModel.DataAnnotations;

namespace SecureQrPortal.Models;

public sealed class MobileOtpChallenge
{
    public long Id { get; set; }
    [MaxLength(64)] public string ChallengeId { get; set; } = string.Empty;
    public long OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    [MaxLength(11)] public string MobileNumber { get; set; } = string.Empty;
    [MaxLength(64)] public string OtpHash { get; set; } = string.Empty;
    public string ProtectedVerificationKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime ResendAvailableAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public bool ProviderSucceeded { get; set; }
    public int? ProviderHttpStatusCode { get; set; }
    [MaxLength(64)] public string? ProviderResultCode { get; set; }
}

public sealed class MobileSession
{
    public long Id { get; set; }
    [MaxLength(64)] public string SessionId { get; set; } = string.Empty;
    public long OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    [MaxLength(64)] public string AccessTokenHash { get; set; } = string.Empty;
    [MaxLength(64)] public string RefreshTokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime AccessExpiresAtUtc { get; set; }
    public DateTime RefreshExpiresAtUtc { get; set; }
    public DateTime? RefreshUsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    [MaxLength(64)] public string? ReplacedBySessionId { get; set; }
}

public sealed class MobileDevice
{
    public long Id { get; set; }
    [MaxLength(128)] public string DeviceId { get; set; } = string.Empty;
    public long OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string FcmTokenProtected { get; set; } = string.Empty;
    [MaxLength(64)] public string FcmTokenHash { get; set; } = string.Empty;
    [MaxLength(32)] public string Platform { get; set; } = string.Empty;
    [MaxLength(64)] public string AppVersion { get; set; } = string.Empty;
    public bool PushEnabled { get; set; } = true;
    public DateTime RegisteredAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime? DeactivatedAtUtc { get; set; }
    [MaxLength(36)] public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class MobileDelivery
{
    public long Id { get; set; }
    public long OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public long SecurePageId { get; set; }
    public SecurePage SecurePage { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    [MaxLength(40)] public string DeliveryStatus { get; set; } = "CREATED";
    [MaxLength(40)] public string? FirebaseStatus { get; set; }
    [MaxLength(200)] public string? FirebaseProviderMessageId { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? FirstRevealedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public bool ReminderEnabled { get; set; }
    public int? ReminderInterval { get; set; }
    [MaxLength(20)] public string? ReminderUnit { get; set; }
    public DateTime? NextReminderAtUtc { get; set; }
    public DateTime? LastReminderAtUtc { get; set; }
    public int ReminderCount { get; set; }
    [MaxLength(36)] public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class MobileRevealGrant
{
    public long Id { get; set; }
    [MaxLength(64)] public string TokenHash { get; set; } = string.Empty;
    public long MobileSessionId { get; set; }
    public MobileSession MobileSession { get; set; } = null!;
    public long MobileDeliveryId { get; set; }
    public MobileDelivery MobileDelivery { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}
