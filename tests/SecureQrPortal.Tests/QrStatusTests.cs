using SecureQrPortal.Models;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class QrStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);
    private readonly QrStatusService _service = new(new FixedTimeProvider(Now));

    [Fact]
    public void Valid_token_state_is_active() => Assert.Equal(QrStatus.ACTIVE, _service.GetStatus(Page()));

    [Fact]
    public void Disabled_page_is_disabled() { var p = Page(); p.IsActive = false; Assert.Equal(QrStatus.DISABLED, _service.GetStatus(p)); }

    [Fact]
    public void Disabled_organization_disables_qr() { var p = Page(); p.Organization.IsActive = false; Assert.Equal(QrStatus.DISABLED, _service.GetStatus(p)); }

    [Fact]
    public void Expired_page_is_expired() { var p = Page(); p.ExpiresAtUtc = Now.UtcDateTime.AddSeconds(-1); Assert.Equal(QrStatus.EXPIRED, _service.GetStatus(p)); }

    [Fact]
    public void Future_page_is_not_started() { var p = Page(); p.ValidFromUtc = Now.UtcDateTime.AddMinutes(1); Assert.Equal(QrStatus.NOT_STARTED, _service.GetStatus(p)); }

    [Fact]
    public void Revocation_has_highest_precedence() { var p = Page(); p.IsActive = false; p.RevokedAtUtc = Now.UtcDateTime; Assert.Equal(QrStatus.REVOKED, _service.GetStatus(p)); }

    [Theory]
    [InlineData(AccessLimitMode.MaximumSuccessfulAccesses)]
    [InlineData(AccessLimitMode.ExpiryAndSuccessfulAccesses)]
    public void Successful_access_limit_is_authoritative(AccessLimitMode mode)
    {
        var p = Page(); p.AccessLimitMode = mode; p.MaxAccessCount = 3; p.CurrentSuccessfulAccessCount = 3;
        Assert.Equal(QrStatus.LIMIT_REACHED, _service.GetStatus(p));
    }

    [Theory]
    [InlineData(AccessLimitMode.MaximumQrOpens)]
    [InlineData(AccessLimitMode.ExpiryAndQrOpens)]
    public void Qr_open_limit_is_authoritative(AccessLimitMode mode)
    {
        var p = Page(); p.AccessLimitMode = mode; p.MaxAccessCount = 3; p.CurrentQrOpenCount = 3;
        Assert.Equal(QrStatus.LIMIT_REACHED, _service.GetStatus(p));
    }

    private static SecurePage Page() => new()
    {
        IsActive = true,
        Organization = new Organization { IsActive = true },
        ValidFromUtc = Now.UtcDateTime.AddDays(-1),
        ExpiresAtUtc = Now.UtcDateTime.AddDays(1),
        AccessLimitMode = AccessLimitMode.MaximumSuccessfulAccesses,
        MaxAccessCount = 10
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
