using Microsoft.AspNetCore.Mvc;
namespace SecureQrPortal.Controllers;
[Route("error")]
public sealed class ErrorController : Controller
{
    [Route("forbidden")] public IActionResult Forbidden() { Response.StatusCode=403; return View("Error",("403","Forbidden","You do not have permission to perform this action.")); }
    [Route("500")] public IActionResult ServerError() { Response.StatusCode=500; return View("Error",("500","Unexpected error","The request could not be completed.")); }
    [Route("404")] public IActionResult NotFoundPage() { Response.StatusCode=404; return View("Error",("404","Not found","The requested resource was not found.")); }
}
