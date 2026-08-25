using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Models;
using SecureQrPortal.ViewModels;
namespace SecureQrPortal.Controllers;
[AllowAnonymous]
public sealed class SetupController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : Controller
{
    private async Task<bool> RequiredAsync()=>!await users.Users.AnyAsync();
    [HttpGet] public async Task<IActionResult> Index(){ if(!await RequiredAsync()) return RedirectToAction("Login","Account"); return View(new SetupVm()); }
    [HttpPost]
    public async Task<IActionResult> Index(SetupVm vm)
    {
        if(!await RequiredAsync()) return RedirectToAction("Login","Account"); if(!ModelState.IsValid) return View(vm);
        var user=new ApplicationUser{UserName=vm.Email.Trim(),Email=vm.Email.Trim(),DisplayName=vm.DisplayName.Trim(),EmailConfirmed=true,CreatedAtUtc=DateTime.UtcNow};
        var result=await users.CreateAsync(user,vm.Password); if(result.Succeeded) result=await users.AddToRoleAsync(user,"Administrator");
        if(!result.Succeeded){ foreach(var e in result.Errors) ModelState.AddModelError("",e.Description); return View(vm); }
        await signIn.SignInAsync(user,false); return RedirectToAction("Index","Dashboard",new{area="Admin"});
    }
}
