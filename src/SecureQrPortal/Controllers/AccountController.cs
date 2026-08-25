using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecureQrPortal.Models;
using SecureQrPortal.Services;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Controllers;
public sealed class AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, AuditService audit) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl=null) { ViewBag.ReturnUrl=returnUrl; return View(new LoginVm()); }

    [AllowAnonymous, HttpPost, EnableRateLimiting("public-login")]
    public async Task<IActionResult> Login(LoginVm vm, string? returnUrl=null)
    {
        if(!ModelState.IsValid) return View(vm);
        var user=await users.FindByEmailAsync(vm.Email.Trim());
        if(user is null){ ModelState.AddModelError("", "Invalid credentials."); return View(vm); }
        var result=await signIn.PasswordSignInAsync(user,vm.Password,vm.RememberMe,lockoutOnFailure:true);
        if(!result.Succeeded){ ModelState.AddModelError("", result.IsLockedOut?"Account temporarily locked.":"Invalid credentials."); return View(vm); }
        await audit.WriteAsync("LOGIN","AdminUser",user.Id,"Administrator login");
        return LocalRedirect(Url.IsLocalUrl(returnUrl)?returnUrl!:Url.Action("Index","Dashboard",new{area="Admin"})!);
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Logout()
    {
        await audit.WriteAsync("LOGOUT","AdminUser",users.GetUserId(User),"Administrator logout");
        await signIn.SignOutAsync(); return RedirectToAction(nameof(Login));
    }

    [Authorize, HttpGet] public IActionResult ChangePassword()=>View(new ChangePasswordVm());
    [Authorize, HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordVm vm)
    {
        if(!ModelState.IsValid) return View(vm); var user=await users.GetUserAsync(User); if(user is null) return Challenge();
        var result=await users.ChangePasswordAsync(user,vm.CurrentPassword,vm.NewPassword);
        if(!result.Succeeded){ foreach(var e in result.Errors) ModelState.AddModelError("",e.Description); return View(vm); }
        await signIn.RefreshSignInAsync(user); await audit.WriteAsync("PASSWORD_CHANGE","AdminUser",user.Id,"Admin password changed"); TempData["Success"]="Password changed."; return RedirectToAction(nameof(ChangePassword));
    }
}
