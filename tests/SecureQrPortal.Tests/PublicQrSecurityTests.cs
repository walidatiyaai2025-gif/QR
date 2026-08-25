using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureQrPortal.Controllers;

namespace SecureQrPortal.Tests;

public sealed class PublicQrSecurityTests
{
    [Fact]
    public void Public_login_route_keeps_rate_limit_and_does_not_bypass_antiforgery()
    {
        var method = typeof(PublicQrController).GetMethod(nameof(PublicQrController.Login));
        Assert.NotNull(method);

        var post = method!.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true)
            .Cast<HttpPostAttribute>()
            .Single();
        Assert.Equal("{token}/login", post.Template);

        Assert.Single(method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true));
        Assert.Empty(method.GetCustomAttributes(typeof(IgnoreAntiforgeryTokenAttribute), inherit: true));
    }
}
