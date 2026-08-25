using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Models;

namespace SecureQrPortal.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            await db.Database.MigrateAsync();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roleManager.RoleExistsAsync("Administrator"))
                await roleManager.CreateAsync(new IdentityRole("Administrator"));

            if (!await db.ApplicationSettings.AnyAsync())
            {
                db.ApplicationSettings.AddRange(
                    new ApplicationSetting { Key = "ApplicationName", Value = "Secure QR Portal" },
                    new ApplicationSetting { Key = "DefaultLanguage", Value = "ar" },
                    new ApplicationSetting { Key = "LoginFooterText", Value = "Secure access • Authorized users only" },
                    new ApplicationSetting { Key = "DefaultQrSize", Value = "12" },
                    new ApplicationSetting { Key = "SessionTimeoutMinutes", Value = "20" },
                    new ApplicationSetting { Key = "TimeZone", Value = "Asia/Kuwait" },
                    new ApplicationSetting { Key = "ShowExpiryPublicly", Value = "true" });
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Database initialization failed. Application will remain unavailable until the database issue is corrected.");
            throw;
        }
    }
}
