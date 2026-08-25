using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using SecureQrPortal.Models;

namespace SecureQrPortal.Services;

public sealed class AdminIdentityService(UserManager<ApplicationUser> users, IHttpContextAccessor accessor)
{
    public string? CurrentUserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    public async Task<ApplicationUser?> CurrentUserAsync() => CurrentUserId is null ? null : await users.FindByIdAsync(CurrentUserId);
}
