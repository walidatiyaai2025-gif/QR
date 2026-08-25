namespace SecureQrPortal.Services;

public sealed record MobilePushDispatchRequest(long DeliveryId);

public sealed record MobilePushDispatchResult(
    bool ProviderAccepted,
    string ProviderStatus,
    string? ProviderMessageId = null,
    string? ErrorCode = null);

public interface IMobilePushDispatchService
{
    Task<MobilePushDispatchResult> DispatchAsync(MobilePushDispatchRequest request, CancellationToken ct = default);
}

/// <summary>
/// Worker #4 fail-closed seam. Worker #3 may replace this registration with the
/// real Firebase implementation. It must never synthesize provider acceptance.
/// </summary>
public sealed class UnavailableMobilePushDispatchService : IMobilePushDispatchService
{
    public Task<MobilePushDispatchResult> DispatchAsync(MobilePushDispatchRequest request, CancellationToken ct = default) =>
        Task.FromResult(new MobilePushDispatchResult(
            ProviderAccepted: false,
            ProviderStatus: "PROVIDER_UNAVAILABLE",
            ErrorCode: "PROVIDER_UNAVAILABLE"));
}
