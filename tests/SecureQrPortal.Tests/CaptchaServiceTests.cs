using Microsoft.Extensions.DependencyInjection;
using SecureQrPortal.Security.Captcha;

namespace SecureQrPortal.Tests;

public sealed class CaptchaServiceTests
{
    private const string KnownAnswer = "ABC234";

    [Fact]
    public void Challenges_are_random_png_only_and_do_not_expose_the_answer()
    {
        using var provider = CreateProvider();
        var captcha = provider.GetRequiredService<ICaptchaService>();

        var first = captcha.IssueChallenge();
        var second = captcha.IssueChallenge();
        var image = captcha.GetImage(first.ChallengeId);

        Assert.NotEqual(first.ChallengeId, second.ChallengeId);
        Assert.Equal(43, first.ChallengeId.Length);
        Assert.NotNull(image);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, image.Bytes[..8]);
        Assert.DoesNotContain(KnownAnswer, System.Text.Encoding.UTF8.GetString(image.Bytes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Correct_answer_is_case_insensitive_single_use_and_hides_the_consumed_image()
    {
        using var provider = CreateProvider();
        var captcha = provider.GetRequiredService<ICaptchaService>();
        var challenge = captcha.IssueChallenge();

        Assert.Equal(CaptchaValidationStatus.Success, captcha.Validate(challenge.ChallengeId, KnownAnswer.ToLowerInvariant()));
        Assert.Equal(CaptchaValidationStatus.Replayed, captcha.Validate(challenge.ChallengeId, KnownAnswer));
        Assert.Null(captcha.GetImage(challenge.ChallengeId));
    }

    [Fact]
    public void Fifth_wrong_answer_enforces_max_attempts_and_consumes_the_challenge()
    {
        using var provider = CreateProvider();
        var captcha = provider.GetRequiredService<ICaptchaService>();
        var challenge = captcha.IssueChallenge();

        for (var attempt = 1; attempt < 5; attempt++)
            Assert.Equal(CaptchaValidationStatus.Invalid, captcha.Validate(challenge.ChallengeId, "ZZZ999"));

        Assert.Equal(CaptchaValidationStatus.MaxAttemptsExceeded, captcha.Validate(challenge.ChallengeId, "ZZZ999"));
        Assert.Equal(CaptchaValidationStatus.Replayed, captcha.Validate(challenge.ChallengeId, KnownAnswer));
    }

    [Fact]
    public void Expired_challenge_is_rejected_and_cannot_be_reused()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
        using var provider = CreateProvider(clock);
        var captcha = provider.GetRequiredService<ICaptchaService>();
        var challenge = captcha.IssueChallenge();

        clock.Advance(TimeSpan.FromMinutes(3));

        Assert.Equal(CaptchaValidationStatus.Expired, captcha.Validate(challenge.ChallengeId, KnownAnswer));
        Assert.Equal(CaptchaValidationStatus.Replayed, captcha.Validate(challenge.ChallengeId, KnownAnswer));
    }

    [Fact]
    public void Refresh_invalidation_rejects_the_old_challenge()
    {
        using var provider = CreateProvider();
        var captcha = provider.GetRequiredService<ICaptchaService>();
        var oldChallenge = captcha.IssueChallenge();

        captcha.Invalidate(oldChallenge.ChallengeId);
        var newChallenge = captcha.IssueChallenge();

        Assert.NotEqual(oldChallenge.ChallengeId, newChallenge.ChallengeId);
        Assert.Equal(CaptchaValidationStatus.Replayed, captcha.Validate(oldChallenge.ChallengeId, KnownAnswer));
        Assert.Equal(CaptchaValidationStatus.Success, captcha.Validate(newChallenge.ChallengeId, KnownAnswer));
    }

    [Fact]
    public void Concurrent_validation_allows_exactly_one_success()
    {
        using var provider = CreateProvider();
        var captcha = provider.GetRequiredService<ICaptchaService>();
        var challenge = captcha.IssueChallenge();
        var results = new CaptchaValidationStatus[12];

        Parallel.For(0, results.Length, index =>
            results[index] = captcha.Validate(challenge.ChallengeId, KnownAnswer));

        Assert.Single(results, x => x == CaptchaValidationStatus.Success);
        Assert.All(results.Where(x => x != CaptchaValidationStatus.Success),
            x => Assert.Equal(CaptchaValidationStatus.Replayed, x));
    }

    [Fact]
    public void Server_state_has_no_plaintext_answer_field()
    {
        var stateType = typeof(ICaptchaService).Assembly.GetType(
            "SecureQrPortal.Security.Captcha.CaptchaChallengeState",
            throwOnError: true)!;

        Assert.DoesNotContain(stateType.GetProperties(), property =>
            property.PropertyType == typeof(string) && property.Name.Contains("Answer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(stateType.GetProperties(), property =>
            property.Name == "AnswerHmac" && property.PropertyType == typeof(byte[]));
    }

    private static ServiceProvider CreateProvider(MutableTimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock ?? new MutableTimeProvider(DateTimeOffset.UtcNow));
        services.AddSingleton<ICaptchaAnswerGenerator>(new FixedCaptchaAnswerGenerator(KnownAnswer));
        services.AddCaptchaSecurity();
        return services.BuildServiceProvider();
    }

    private sealed class FixedCaptchaAnswerGenerator(string answer) : ICaptchaAnswerGenerator
    {
        public char[] Generate(int length)
        {
            Assert.Equal(length, answer.Length);
            return answer.ToCharArray();
        }
    }
}

public sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
    public void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
}
