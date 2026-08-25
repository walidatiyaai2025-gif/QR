using Microsoft.AspNetCore.Mvc;
namespace SecureQrPortal.Controllers;
public sealed class HomeController : Controller
{
    public IActionResult Index() => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index","Dashboard",new{area="Admin"}) : RedirectToAction("Login","Account");
}
