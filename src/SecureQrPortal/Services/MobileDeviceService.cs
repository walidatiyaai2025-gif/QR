using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;

namespace SecureQrPortal.Services;

public enum MobileDeviceRegistrationStatus
{
    Success,
    Invalid,
    Conflict
}

public sealed record MobileDeviceRegistrationResult(
    MobileDeviceRegistrationStatus Status,
    long? DeviceDatabaseId = null,
    DateTime? RegisteredAtUtc = null,
    DateTime? LastSeenAtUtc = null,
    bool PushEnabled = false);

public sealed class MobileDeviceService(
    ApplicationDbContext db,
    MobileSecretProtector secrets,
    MobileTokenService tokens,
    AuditService audit,
    TimeProvider timeProvider)
{
    public async Task<MobileDeviceRegistrationResult> RegisterAsync(
        long organizationId,
        string? deviceId,
        string? fcmToken,
        string? platform,
        string? appVersion,
        bool pushEnabled,
        CancellationToken ct = default)
    {
        deviceId = deviceId?.Trim();
        fcmToken = fcmToken?.Trim();
        platform = platform?.Trim().ToLowerInvariant();
        appVersion = appVersion?.Trim();
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 128 ||
            string.IsNullOrWhiteSpace(fcmToken) || fcmToken.Length > 4096 ||
            string.IsNullOrWhiteSpace(platform) || platform.Length > 32 ||
            string.IsNullOrWhiteSpace(appVersion) || appVersion.Length > 64)
            return new(MobileDeviceRegistrationStatus.Invalid);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var tokenHash = tokens.HashToken(fcmToken);
        var byDevice = await db.MobileDevices.SingleOrDefaultAsync(x => x.DeviceId == deviceId, ct);
        if (byDevice is not null && byDevice.OrganizationId != organizationId)
            return new(MobileDeviceRegistrationStatus.Conflict);

        var tokenOwner = await db.MobileDevices.SingleOrDefaultAsync(x => x.FcmTokenHash == tokenHash, ct);
        if (tokenOwner is not null && tokenOwner.OrganizationId != organizationId)
            return new(MobileDeviceRegistrationStatus.Conflict);

        if (tokenOwner is not null && byDevice is not null && tokenOwner.Id != byDevice.Id)
        {
            tokenOwner.DeactivatedAtUtc = now;
            tokenOwner.PushEnabled = false;
            tokenOwner.FcmTokenHash = tokens.HashToken($"retired:{tokenOwner.Id}:{tokens.GenerateToken()}");
            tokenOwner.FcmTokenProtected = string.Empty;
            tokenOwner.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        }

        var device = byDevice ?? new MobileDevice
        {
            DeviceId = deviceId,
            OrganizationId = organizationId,
            RegisteredAtUtc = now
        };
        device.FcmTokenHash = tokenHash;
        device.FcmTokenProtected = secrets.ProtectFcmToken(fcmToken);
        device.Platform = platform;
        device.AppVersion = appVersion;
        device.PushEnabled = pushEnabled;
        device.LastSeenAtUtc = now;
        device.DeactivatedAtUtc = null;
        device.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        if (byDevice is null) db.MobileDevices.Add(device);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("MOBILE_DEVICE_REGISTERED", "MobileDevice", device.Id.ToString(),
            $"OrganizationId={organizationId};Platform={platform};PushEnabled={pushEnabled}", ct);
        return new(MobileDeviceRegistrationStatus.Success, device.Id, device.RegisteredAtUtc, device.LastSeenAtUtc, device.PushEnabled);
    }
}
