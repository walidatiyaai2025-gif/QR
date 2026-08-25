namespace SecureQrPortal.Security.Captcha;

public sealed record CaptchaChallenge(string ChallengeId, DateTimeOffset ExpiresAtUtc);

public sealed record CaptchaImage(byte[] Bytes, DateTimeOffset ExpiresAtUtc);

public enum CaptchaValidationStatus
{
    Success,
    Invalid,
    Expired,
    Replayed,
    MaxAttemptsExceeded,
    NotFound
}

public interface ICaptchaService
{
    CaptchaChallenge IssueChallenge();
    CaptchaImage? GetImage(string challengeId);
    CaptchaValidationStatus Validate(string? challengeId, string? answer);
    void Invalidate(string? challengeId);
}

public interface ICaptchaAnswerGenerator
{
    char[] Generate(int length);
}
