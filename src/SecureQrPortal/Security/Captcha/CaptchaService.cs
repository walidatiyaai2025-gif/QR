using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SecureQrPortal.Security.Captcha;

internal sealed class SecureCaptchaAnswerGenerator : ICaptchaAnswerGenerator
{
    internal const string AllowedCharacters = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public char[] Generate(int length)
    {
        var answer = new char[length];
        for (var i = 0; i < answer.Length; i++)
            answer[i] = AllowedCharacters[RandomNumberGenerator.GetInt32(AllowedCharacters.Length)];
        return answer;
    }
}

internal sealed class CaptchaService(
    ICaptchaChallengeStore store,
    ICaptchaAnswerGenerator answerGenerator,
    ICaptchaImageRenderer imageRenderer,
    TimeProvider timeProvider) : ICaptchaService, IDisposable
{
    private const int AnswerLength = 6;
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan TombstoneLifetime = TimeSpan.FromMinutes(5);
    private readonly byte[] _hmacKey = RandomNumberGenerator.GetBytes(32);

    public CaptchaChallenge IssueChallenge()
    {
        var now = timeProvider.GetUtcNow();
        store.RemoveRetired(now);

        var answer = answerGenerator.Generate(AnswerLength);
        if (answer.Length != AnswerLength || answer.Any(x => !SecureCaptchaAnswerGenerator.AllowedCharacters.Contains(x)))
            throw new InvalidOperationException("The CAPTCHA answer generator returned an invalid challenge.");

        try
        {
            var expiresAt = now.Add(Lifetime);
            var state = new CaptchaChallengeState
            {
                AnswerHmac = ComputeHmac(answer),
                ImageBytes = imageRenderer.Render(answer),
                ExpiresAtUtc = expiresAt,
                RetainUntilUtc = expiresAt.Add(TombstoneLifetime)
            };

            string challengeId;
            do
            {
                challengeId = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            } while (!store.TryAdd(challengeId, state));

            return new CaptchaChallenge(challengeId, expiresAt);
        }
        finally
        {
            Array.Clear(answer);
        }
    }

    public CaptchaImage? GetImage(string challengeId)
    {
        if (!TryGetState(challengeId, out var state))
            return null;

        lock (state.SyncRoot)
        {
            if (state.Consumed || timeProvider.GetUtcNow() >= state.ExpiresAtUtc)
                return null;

            return new CaptchaImage((byte[])state.ImageBytes.Clone(), state.ExpiresAtUtc);
        }
    }

    public CaptchaValidationStatus Validate(string? challengeId, string? answer)
    {
        if (!TryGetState(challengeId, out var state))
            return CaptchaValidationStatus.NotFound;

        lock (state.SyncRoot)
        {
            if (state.Consumed)
                return CaptchaValidationStatus.Replayed;

            if (timeProvider.GetUtcNow() >= state.ExpiresAtUtc)
            {
                state.Consumed = true;
                return CaptchaValidationStatus.Expired;
            }

            if (state.FailedAttempts >= MaxFailedAttempts)
            {
                state.Consumed = true;
                return CaptchaValidationStatus.MaxAttemptsExceeded;
            }

            var candidateHmac = ComputeNormalizedHmac(answer);
            var matches = CryptographicOperations.FixedTimeEquals(candidateHmac, state.AnswerHmac);
            CryptographicOperations.ZeroMemory(candidateHmac);

            if (matches)
            {
                state.Consumed = true;
                return CaptchaValidationStatus.Success;
            }

            state.FailedAttempts++;
            if (state.FailedAttempts >= MaxFailedAttempts)
            {
                state.Consumed = true;
                return CaptchaValidationStatus.MaxAttemptsExceeded;
            }

            return CaptchaValidationStatus.Invalid;
        }
    }

    public void Invalidate(string? challengeId)
    {
        if (!TryGetState(challengeId, out var state))
            return;

        lock (state.SyncRoot)
            state.Consumed = true;
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(_hmacKey);

    private bool TryGetState(string? challengeId, out CaptchaChallengeState state)
    {
        state = null!;
        return !string.IsNullOrWhiteSpace(challengeId) &&
               challengeId.Length <= 128 &&
               store.TryGet(challengeId, out var stored) &&
               (state = stored!) is not null;
    }

    private byte[] ComputeHmac(ReadOnlySpan<char> answer)
    {
        Span<byte> bytes = stackalloc byte[AnswerLength];
        for (var i = 0; i < AnswerLength; i++)
            bytes[i] = (byte)answer[i];
        return HMACSHA256.HashData(_hmacKey, bytes);
    }

    private byte[] ComputeNormalizedHmac(string? answer)
    {
        Span<char> normalized = stackalloc char[AnswerLength];
        normalized.Fill('\0');
        var trimmed = answer?.Trim();

        if (trimmed?.Length == AnswerLength)
        {
            for (var i = 0; i < trimmed.Length; i++)
            {
                var character = char.ToUpperInvariant(trimmed[i]);
                normalized[i] = SecureCaptchaAnswerGenerator.AllowedCharacters.Contains(character)
                    ? character
                    : '\0';
            }
        }

        return ComputeHmac(normalized);
    }
}

public static class CaptchaServiceCollectionExtensions
{
    public static IServiceCollection AddCaptchaSecurity(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICaptchaAnswerGenerator, SecureCaptchaAnswerGenerator>();
        services.TryAddSingleton<ICaptchaImageRenderer, CaptchaImageRenderer>();
        services.TryAddSingleton<ICaptchaChallengeStore, InMemoryCaptchaChallengeStore>();
        services.TryAddSingleton<ICaptchaService, CaptchaService>();
        return services;
    }
}
