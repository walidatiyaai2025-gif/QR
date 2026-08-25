using Microsoft.AspNetCore.Authorization;
using SecureQrPortal.Areas.Admin.Controllers;

namespace SecureQrPortal.Tests;

public sealed class AdminSecurityTests
{
    [Theory]
    [InlineData(typeof(DashboardController))]
    [InlineData(typeof(OrganizationsController))]
    [InlineData(typeof(SecurePagesController))]
    [InlineData(typeof(QrController))]
    [InlineData(typeof(LogsController))]
    [InlineData(typeof(SettingsController))]
    public void Admin_controllers_require_administrator_role(Type controllerType)
    {
        var authorize = controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().ToArray();
        Assert.Contains(authorize, x => x.Roles?.Split(',').Any(r => r.Trim() == "Administrator") == true);
    }
}
