using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureQrPortal.Services;
namespace SecureQrPortal.Areas.Admin.Controllers;
[Area("Admin"),Authorize(Roles="Administrator")]
public sealed class DemoController(DemoDataService demo,AuditService audit):Controller
{
 [HttpPost] public async Task<IActionResult> Create(int count=25,CancellationToken ct=default){var n=await demo.CreateAsync(count,ct);await audit.WriteAsync("DEMO_DATA_CREATE","DemoData",null,$"Created {n} demo secure pages",ct);TempData["Success"]=$"Created {n} demo secure pages.";return RedirectToAction("Index","Dashboard");}
 [HttpPost] public async Task<IActionResult> Delete(string confirmation,CancellationToken ct=default){if(confirmation!="DELETE DEMO"){TempData["Error"]="Type DELETE DEMO to confirm.";return RedirectToAction("Index","Dashboard");}var n=await demo.DeleteAsync(ct);await audit.WriteAsync("DEMO_DATA_DELETE","DemoData",null,$"Deleted {n} demo secure pages",ct);TempData["Success"]=$"Deleted {n} demo secure pages.";return RedirectToAction("Index","Dashboard");}
}
