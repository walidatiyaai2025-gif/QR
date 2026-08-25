using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Security;

namespace SecureQrPortal.Services;

public sealed record FirebaseOutboundMessage(
    long DeviceId,
    string Token,
    IReadOnlyDictionary<string, string> Data,
    string NotificationTitle,
    string NotificationBody);

public sealed record FirebaseTargetSendResult(
    long DeviceId,
    bool Accepted,
    string? MessageId = null,
    string? ErrorCode = null,
    bool TokenInvalid = false);

public sealed record FirebaseBatchSendResult(
    bool ProviderAvailable,
    string ProviderStatus,
    IReadOnlyList<FirebaseTargetSendResult> Targets);

public interface IFirebaseMessagingClient
{
    Task<FirebaseBatchSendResult> SendAsync(
        IReadOnlyList<FirebaseOutboundMessage> messages,
        CancellationToken ct = default);
}

public sealed class FirebaseAdminMessagingClient(
    IConfiguration configuration,
    ILogger<FirebaseAdminMessagingClient> logger) : IFirebaseMessagingClient
{
    private readonly object sync = new();
    private FirebaseApp? firebaseApp;
    private FirebaseMessaging? messaging;
    private bool initializationAttempted;
    private string initializationStatus = "FIREBASE_NOT_INITIALIZED";

    public async Task<FirebaseBatchSendResult> SendAsync(
        IReadOnlyList<FirebaseOutboundMessage> messages,
        CancellationToken ct = default)
    {
        if (messages.Count == 0)
            return new(false, "NO_REGISTERED_DEVICE", Array.Empty<FirebaseTargetSendResult>());

        var client = ResolveMessaging();
        if (client is null)
            return new(false, initializationStatus, messages
                .Select(x => new FirebaseTargetSendResult(x.DeviceId, false, ErrorCode: initializationStatus))
                .ToList());

        var results = new List<FirebaseTargetSendResult>(messages.Count);
        foreach (var outbound in messages)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
#pragma warning disable CS0618 // Existing mobile clients register FCM registration tokens; FirebaseAdmin 3.6 keeps Token compatibility.
                var message = new Message
                {
                    Token = outbound.Token,
                    Notification = new Notification
                    {
                        Title = outbound.NotificationTitle,
                        Body = outbound.NotificationBody
                    },
                    Data = outbound.Data
                };
#pragma warning restore CS0618
                var messageId = await client.SendAsync(message, ct);
                results.Add(new FirebaseTargetSendResult(outbound.DeviceId, true, messageId));
            }
            catch (FirebaseMessagingException ex)
            {
                var invalid = ex.MessagingErrorCode == MessagingErrorCode.Unregistered;
                var code = ex.MessagingErrorCode?.ToString().ToUpperInvariant()
                    ?? ex.ErrorCode.ToString().ToUpperInvariant();
                results.Add(new FirebaseTargetSendResult(outbound.DeviceId, false, ErrorCode: code, TokenInvalid: invalid));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Firebase send failed with {ExceptionType}.", ex.GetType().Name);
                results.Add(new FirebaseTargetSendResult(outbound.DeviceId, false, ErrorCode: "FIREBASE_SEND_ERROR"));
            }
        }

        var accepted = results.Count(x => x.Accepted);
        var status = accepted == messages.Count
            ? "FCM_ACCEPTED"
            : accepted > 0 ? "FCM_PARTIAL_ACCEPTED" : "FCM_REJECTED";
        return new(true, status, results);
    }

    private FirebaseMessaging? ResolveMessaging()
    {
        if (messaging is not null) return messaging;
        lock (sync)
        {
            if (messaging is not null) return messaging;
            if (initializationAttempted) return null;
            initializationAttempted = true;

            if (!configuration.GetValue("Firebase:Enabled", false))
            {
                initializationStatus = "FIREBASE_DISABLED";
                return null;
            }

            try
            {
                var credentialsPath = configuration["Firebase:CredentialsPath"]?.Trim();
                var credential = string.IsNullOrWhiteSpace(credentialsPath)
                    ? GoogleCredential.GetApplicationDefault()
                    : GoogleCredential.FromFile(credentialsPath);
                var projectId = configuration["Firebase:ProjectId"]?.Trim();
                var options = new AppOptions { Credential = credential };
                if (!string.IsNullOrWhiteSpace(projectId)) options.ProjectId = projectId;

                firebaseApp = FirebaseApp.Create(options, $"da-secure-mobile-{Guid.NewGuid():N}");
                messaging = FirebaseMessaging.GetMessaging(firebaseApp);
                initializationStatus = "FIREBASE_READY";
                return messaging;
            }
            catch (Exception ex)
            {
                initializationStatus = "FIREBASE_CREDENTIALS_UNAVAILABLE";
                logger.LogWarning("Firebase Admin initialization failed with {ExceptionType}.", ex.GetType().Name);
                return null;
            }
        }
    }
}

public sealed class FirebaseMobilePushDispatchService(
    ApplicationDbContext db,
    MobileSecretProtector secrets,
    MobileTokenService tokens,
    IFirebaseMessagingClient firebase,
    AuditService audit,
    TimeProvider timeProvider) : IMobilePushDispatchService
{
    public const string FixedHeadingArabic = "لديك رسالة جديدة اضغط هنا لاستعراض الرسالة";
    public const string FixedHeadingEnglish = "You have a new message. Tap here to view it.";

    public async Task<MobilePushDispatchResult> DispatchAsync(
        MobilePushDispatchRequest request,
        CancellationToken ct = default)
    {
        var delivery = await db.MobileDeliveries.AsNoTracking()
            .Where(x => x.Id == request.DeliveryId)
            .Select(x => new
            {
                x.Id,
                x.OrganizationId,
                x.RevokedAtUtc,
                x.FirstRevealedAtUtc,
                x.ExpiresAtUtc
            })
            .SingleOrDefaultAsync(ct);
        if (delivery is null)
            return new(false, "DELIVERY_NOT_FOUND", ErrorCode: "DELIVERY_NOT_FOUND");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (delivery.RevokedAtUtc.HasValue || delivery.FirstRevealedAtUtc.HasValue ||
            (delivery.ExpiresAtUtc.HasValue && delivery.ExpiresAtUtc.Value <= now))
            return new(false, "DELIVERY_NOT_ELIGIBLE", ErrorCode: "DELIVERY_NOT_ELIGIBLE");

        var devices = await db.MobileDevices
            .Where(x => x.OrganizationId == delivery.OrganizationId &&
                        x.DeactivatedAtUtc == null && x.PushEnabled)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        if (devices.Count == 0)
            return new(false, "NO_REGISTERED_DEVICE", ErrorCode: "NO_REGISTERED_DEVICE");

        var messages = new List<FirebaseOutboundMessage>(devices.Count);
        var localTokenFailures = new List<long>();
        foreach (var device in devices)
        {
            var fcmToken = secrets.UnprotectFcmToken(device.FcmTokenProtected);
            if (string.IsNullOrWhiteSpace(fcmToken))
            {
                RetireDevice(device, now);
                localTokenFailures.Add(device.Id);
                continue;
            }

            messages.Add(new FirebaseOutboundMessage(
                device.Id,
                fcmToken,
                new Dictionary<string, string>
                {
                    ["deliveryId"] = delivery.Id.ToString(),
                    ["notificationCategory"] = "secure_delivery",
                    ["version"] = "1"
                },
                "DA Secure",
                FixedHeadingArabic));
        }

        if (localTokenFailures.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            foreach (var deviceId in localTokenFailures)
                await audit.WriteAsync("MOBILE_DEVICE_TOKEN_UNREADABLE", "MobileDevice", deviceId.ToString(),
                    $"OrganizationId={delivery.OrganizationId}", ct);
        }

        if (messages.Count == 0)
            return new(false, "NO_USABLE_DEVICE_TOKEN", ErrorCode: "NO_USABLE_DEVICE_TOKEN");

        var result = await firebase.SendAsync(messages, ct);
        var byId = devices.ToDictionary(x => x.Id);
        var invalidated = 0;
        foreach (var target in result.Targets.Where(x => x.TokenInvalid))
        {
            if (!byId.TryGetValue(target.DeviceId, out var device) || device.DeactivatedAtUtc.HasValue) continue;
            RetireDevice(device, now);
            invalidated++;
        }

        if (invalidated > 0)
        {
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync("MOBILE_DEVICE_TOKENS_RETIRED", "MobileDelivery", delivery.Id.ToString(),
                $"OrganizationId={delivery.OrganizationId};Count={invalidated}", ct);
        }

        var accepted = result.Targets.Where(x => x.Accepted).ToList();
        var firstMessageId = accepted.Select(x => x.MessageId).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        await audit.WriteAsync("MOBILE_FCM_DISPATCH_RESULT", "MobileDelivery", delivery.Id.ToString(),
            $"ProviderStatus={result.ProviderStatus};Targets={result.Targets.Count};Accepted={accepted.Count};Invalidated={invalidated}", ct);

        if (!result.ProviderAvailable)
            return new(false, result.ProviderStatus, ErrorCode: result.ProviderStatus);
        if (accepted.Count == 0)
        {
            var error = result.Targets.Select(x => x.ErrorCode).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? "FCM_REJECTED";
            return new(false, result.ProviderStatus, ErrorCode: error);
        }

        return new(true, result.ProviderStatus, firstMessageId);
    }

    private void RetireDevice(SecureQrPortal.Models.MobileDevice device, DateTime now)
    {
        device.PushEnabled = false;
        device.DeactivatedAtUtc = now;
        device.FcmTokenProtected = string.Empty;
        device.FcmTokenHash = tokens.HashToken($"retired:{device.Id}:{tokens.GenerateToken()}");
        device.ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }
}
