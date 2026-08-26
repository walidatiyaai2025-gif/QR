using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Models;
using SecureQrPortal.Services;

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

            await EnsureSettingAsync(db, "ApplicationName", "Secure QR Portal");
            await EnsureSettingAsync(db, "DefaultLanguage", "ar");
            await EnsureSettingAsync(db, "LoginFooterText", "Secure access • Authorized users only");
            await EnsureSettingAsync(db, "DefaultQrSize", "12");
            await EnsureSettingAsync(db, "SessionTimeoutMinutes", "20");
            await EnsureSettingAsync(db, "TimeZone", "Asia/Kuwait");
            await EnsureSettingAsync(db, "ShowExpiryPublicly", "true");
            await EnsureSettingAsync(db, SecureMessageSecuritySettingsService.EnabledKey, "true");
            await EnsureSettingAsync(db, SecureMessageSecuritySettingsService.AllowRevealKey, "true");
            await db.SaveChangesAsync();

            // One-time secure migration for pre-feature rows. This runs after the
            // schema migration and before the app starts accepting traffic. A
            // cryptographic failure aborts startup rather than serving plaintext.
            var crypto = scope.ServiceProvider.GetRequiredService<SecureMessageEncryptionService>();
            var legacyPages = await db.SecurePages
                .Where(x => x.ContentEncryptionVersion == 0)
                .ToListAsync();
            foreach (var page in legacyPages)
                crypto.EncryptLegacyPlaintextForMigration(page, page.ContentArabicHtml, page.ContentEnglishHtml);
            if (legacyPages.Count > 0)
            {
                await db.SaveChangesAsync();
                logger.LogInformation("Encrypted {Count} legacy Secure Message rows during secure startup migration.", legacyPages.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Database/security initialization failed. Application will remain unavailable until the issue is corrected.");
            throw;
        }
    }

    private static async Task EnsureSettingAsync(ApplicationDbContext db, string key, string value)
    {
        if (!await db.ApplicationSettings.AnyAsync(x => x.Key == key))
            db.ApplicationSettings.Add(new ApplicationSetting { Key = key, Value = value, UpdatedAtUtc = DateTime.UtcNow });
    }
}
