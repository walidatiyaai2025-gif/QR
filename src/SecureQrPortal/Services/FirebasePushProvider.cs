using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace SecureQrPortal.Services;

public static class MobilePushConstants
{
    public const string ArabicBody = "لديك رسالة جديدة اضغط هنا لاستعراض الرسالة";
    public const string EnglishBody = "You have a new message. Tap here to view it.";
    public const string InitialCategory = "delivery";
    public const string ReminderCategory = "reminder";
    public const string PayloadVersion = "1";
}

public sealed class FirebasePushOptions
{
    public string ProjectId { get; set; } = "daqr-a4a71";
    public string? CredentialPath { get; set; }
    public int ProviderTimeoutSeconds { get; set; } = 15;
    public int MaxTransientRetries { get; set; } = 2;
    public int RetryBaseMilliseconds { get; set; } = 500;
    public int ReminderScanSeconds { get; set; } = 30;
    public int LeaseSeconds { get; set; } = 120;
}

public enum FirebasePushOutcome
{
    Accepted,
    ProviderUnavailable,
    CredentialFailure,
    InvalidToken,
    Failed,
    Indeterminate
}

public sealed record FirebasePushEnvelope(
    long DeliveryId,
    string Category,
    string Version = MobilePushConstants.PayloadVersion);

public sealed record FirebasePushProviderResult(
    FirebasePushOutcome Outcome,
    string ProviderStatus,
    string? ProviderMessageId = null,
    string? ErrorCode = null,
    bool PermanentFailure = false)
{
    public bool Accepted => Outcome == FirebasePushOutcome.Accepted;
    public bool Retryable => Outcome is FirebasePushOutcome.ProviderUnavailable;
}

public sealed record FirebasePushHealth(string Status, string DetailCode);

public interface IFirebasePushProvider
{
    Task<FirebasePushProviderResult> SendAsync(string fcmToken, FirebasePushEnvelope envelope, CancellationToken ct = default);
    Task<FirebasePushHealth> CheckHealthAsync(CancellationToken ct = default);
}

public sealed class FirebaseAdminPushProvider(
    IOptions<FirebasePushOptions> configuredOptions,
    IWebHostEnvironment environment,
    ILogger<FirebaseAdminPushProvider> logger) : IFirebasePushProvider
{
    private const string FirebaseAppName = "DA-Secure-Server";
    private readonly FirebasePushOptions options = configuredOptions.Value;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private FirebaseMessaging? messaging;
    private FirebasePushHealth health = new("UNINITIALIZED", "NOT_CHECKED");
    private bool initializationAttempted;

    public async Task<FirebasePushProviderResult> SendAsync(
        string fcmToken,
        FirebasePushEnvelope envelope,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fcmToken))
            return new(FirebasePushOutcome.InvalidToken, "INVALID_TOKEN", ErrorCode: "EMPTY_TOKEN", PermanentFailure: true);
        if (envelope.DeliveryId <= 0 ||
            (envelope.Category != MobilePushConstants.InitialCategory && envelope.Category != MobilePushConstants.ReminderCategory) ||
            envelope.Version != MobilePushConstants.PayloadVersion)
            return new(FirebasePushOutcome.Failed, "SEND_FAILED", ErrorCode: "INVALID_SAFE_PAYLOAD", PermanentFailure: true);

        var initialized = await EnsureInitializedAsync(ct);
        if (initialized is null)
            return new(
                health.Status == "CREDENTIAL_FAILURE" ? FirebasePushOutcome.CredentialFailure : FirebasePushOutcome.ProviderUnavailable,
                "PROVIDER_UNAVAILABLE",
                ErrorCode: health.DetailCode,
                PermanentFailure: health.Status == "CREDENTIAL_FAILURE");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.ProviderTimeoutSeconds, 5, 60)));
        try
        {
            var messageId = await initialized.SendAsync(BuildMessage(fcmToken, envelope), timeout.Token);
            return new(FirebasePushOutcome.Accepted, "PROVIDER_ACCEPTED", SafeMessageId(messageId));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(FirebasePushOutcome.ProviderUnavailable, "PROVIDER_UNAVAILABLE", ErrorCode: "PROVIDER_TIMEOUT");
        }
        catch (FirebaseMessagingException ex)
        {
            return MapMessagingException(ex);
        }
        catch (ArgumentException)
        {
            return new(FirebasePushOutcome.Failed, "SEND_FAILED", ErrorCode: "INVALID_MESSAGE", PermanentFailure: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Firebase send unavailable ({ExceptionType}).", ex.GetType().Name);
            return new(FirebasePushOutcome.ProviderUnavailable, "PROVIDER_UNAVAILABLE", ErrorCode: "PROVIDER_ERROR");
        }
    }

    public async Task<FirebasePushHealth> CheckHealthAsync(CancellationToken ct = default)
    {
        _ = await EnsureInitializedAsync(ct);
        return health;
    }

    private async Task<FirebaseMessaging?> EnsureInitializedAsync(CancellationToken ct)
    {
        if (initializationAttempted) return messaging;
        await initializationGate.WaitAsync(ct);
        try
        {
            if (initializationAttempted) return messaging;
            initializationAttempted = true;

            var projectId = options.ProjectId?.Trim();
            if (string.IsNullOrWhiteSpace(projectId))
            {
                health = new("CREDENTIAL_FAILURE", "PROJECT_ID_MISSING");
                return null;
            }

            GoogleCredential credential;
            var configuredPath = options.CredentialPath?.Trim();
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                var fullPath = Path.IsPathRooted(configuredPath)
                    ? configuredPath
                    : Path.GetFullPath(configuredPath, environment.ContentRootPath);
                if (!File.Exists(fullPath))
                {
                    health = new("CREDENTIAL_FAILURE", "CREDENTIAL_FILE_NOT_FOUND");
                    return null;
                }
                credential = CredentialFactory
                    .FromFile<ServiceAccountCredential>(fullPath)
                    .ToGoogleCredential();
            }
            else
            {
                credential = await GoogleCredential.GetApplicationDefaultAsync(ct);
            }

            var app = FirebaseApp.Create(new AppOptions
            {
                Credential = credential,
                ProjectId = projectId
            }, FirebaseAppName);
            messaging = FirebaseMessaging.GetMessaging(app);
            health = new("READY", "FIREBASE_ADMIN_READY");
            return messaging;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Firebase Admin credential bootstrap unavailable ({ExceptionType}).", ex.GetType().Name);
            health = new("CREDENTIAL_FAILURE", "CREDENTIAL_UNAVAILABLE");
            messaging = null;
            return null;
        }
        finally
        {
            initializationGate.Release();
        }
    }

    internal static Message BuildMessage(string fcmToken, FirebasePushEnvelope envelope)
    {
#pragma warning disable CS0618 // FirebaseAdmin 3.6 still exposes the registration-token Token property for single-device sends.
        return new Message
        {
            Token = fcmToken,
            Notification = new Notification
            {
                Body = MobilePushConstants.ArabicBody
            },
            Data = new Dictionary<string, string>
            {
                ["deliveryId"] = envelope.DeliveryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["category"] = envelope.Category,
                ["version"] = envelope.Version
            }
        };
#pragma warning restore CS0618
    }

    private static FirebasePushProviderResult MapMessagingException(FirebaseMessagingException ex)
    {
        return ex.MessagingErrorCode switch
        {
            MessagingErrorCode.Unregistered =>
                new(FirebasePushOutcome.InvalidToken, "INVALID_TOKEN", ErrorCode: "UNREGISTERED", PermanentFailure: true),
            MessagingErrorCode.SenderIdMismatch =>
                new(FirebasePushOutcome.InvalidToken, "INVALID_TOKEN", ErrorCode: "SENDER_ID_MISMATCH", PermanentFailure: true),
            MessagingErrorCode.InvalidArgument =>
                new(FirebasePushOutcome.Failed, "SEND_FAILED", ErrorCode: "INVALID_ARGUMENT", PermanentFailure: true),
            MessagingErrorCode.ThirdPartyAuthError =>
                new(FirebasePushOutcome.CredentialFailure, "PROVIDER_UNAVAILABLE", ErrorCode: "THIRD_PARTY_AUTH_ERROR", PermanentFailure: true),
            MessagingErrorCode.Unavailable =>
                new(FirebasePushOutcome.ProviderUnavailable, "PROVIDER_UNAVAILABLE", ErrorCode: "UNAVAILABLE"),
            MessagingErrorCode.Internal =>
                new(FirebasePushOutcome.ProviderUnavailable, "PROVIDER_UNAVAILABLE", ErrorCode: "INTERNAL"),
            MessagingErrorCode.QuotaExceeded =>
                new(FirebasePushOutcome.ProviderUnavailable, "PROVIDER_UNAVAILABLE", ErrorCode: "QUOTA_EXCEEDED"),
            _ => new(FirebasePushOutcome.Failed, "SEND_FAILED", ErrorCode: "FCM_ERROR")
        };
    }

    private static string? SafeMessageId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed[..Math.Min(trimmed.Length, 200)];
    }
}
