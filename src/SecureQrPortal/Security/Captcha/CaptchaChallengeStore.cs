using System.Collections.Concurrent;

namespace SecureQrPortal.Security.Captcha;

internal sealed class CaptchaChallengeState
{
    public required byte[] AnswerHmac { get; init; }
    public required byte[] ImageBytes { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
    public required DateTimeOffset RetainUntilUtc { get; init; }
    public int FailedAttempts { get; set; }
    public bool Consumed { get; set; }
    public object SyncRoot { get; } = new();
}

internal interface ICaptchaChallengeStore
{
    bool TryAdd(string challengeId, CaptchaChallengeState challenge);
    bool TryGet(string challengeId, out CaptchaChallengeState? challenge);
    void RemoveRetired(DateTimeOffset now);
}

internal sealed class InMemoryCaptchaChallengeStore : ICaptchaChallengeStore
{
    private readonly ConcurrentDictionary<string, CaptchaChallengeState> _challenges =
        new(StringComparer.Ordinal);

    public bool TryAdd(string challengeId, CaptchaChallengeState challenge) =>
        _challenges.TryAdd(challengeId, challenge);

    public bool TryGet(string challengeId, out CaptchaChallengeState? challenge) =>
        _challenges.TryGetValue(challengeId, out challenge);

    public void RemoveRetired(DateTimeOffset now)
    {
        foreach (var entry in _challenges)
        {
            if (entry.Value.RetainUntilUtc <= now)
                _challenges.TryRemove(entry.Key, out _);
        }
    }
}
