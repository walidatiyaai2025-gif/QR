using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Services;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Administrator")]
public sealed class SettingsController(
    AppSettingsService settings,
    SecureMessageSecuritySettingsService security,
    DatabaseSettingsService database,
    BackupService backup,
    AuditService audit,
    AdminIdentityService admin,
    ApplicationDbContext db,
    UiText text) : Controller
{
    [HttpGet]
    public async Task<IActionResult> General(CancellationToken ct)
    {
        var s = await settings.GetAllAsync(ct);
        return View(new GeneralSettingsVm
        {
            ApplicationName = Branding.EnglishName,
            DefaultLanguage = s.GetValueOrDefault("DefaultLanguage", "ar"),
            LoginFooterText = s.GetValueOrDefault("LoginFooterText", ""),
            DefaultQrSize = int.TryParse(s.GetValueOrDefault("DefaultQrSize"), out var q) ? q : 12,
            SessionTimeoutMinutes = int.TryParse(s.GetValueOrDefault("SessionTimeoutMinutes"), out var m) ? m : 20,
            TimeZone = s.GetValueOrDefault("TimeZone", "Asia/Kuwait"),
            ShowExpiryPublicly = bool.TryParse(s.GetValueOrDefault("ShowExpiryPublicly"), out var b) && b
        });
    }

    [HttpPost]
    public async Task<IActionResult> General(GeneralSettingsVm vm, CancellationToken ct)
    {
        // The visual/product identity is intentionally fixed to Al Diwan Al Amiri.
        vm.ApplicationName = Branding.EnglishName;
        ModelState.Remove(nameof(vm.ApplicationName));
        if (!ModelState.IsValid)
        {
            ModelState.AddModelError("", text["ValidationCorrectFields"]);
            return View(vm);
        }
        await settings.SetAsync("ApplicationName", Branding.EnglishName, ct);
        await settings.SetAsync("DefaultLanguage", vm.DefaultLanguage, ct);
        await settings.SetAsync("LoginFooterText", vm.LoginFooterText ?? "", ct);
        await settings.SetAsync("DefaultQrSize", vm.DefaultQrSize.ToString(), ct);
        await settings.SetAsync("SessionTimeoutMinutes", vm.SessionTimeoutMinutes.ToString(), ct);
        await settings.SetAsync("TimeZone", vm.TimeZone, ct);
        await settings.SetAsync("ShowExpiryPublicly", vm.ShowExpiryPublicly.ToString(), ct);
        await audit.WriteAsync("APPLICATION_SETTINGS_CHANGE", "Settings", null, "General settings updated; Diwan branding remains fixed", ct);
        TempData["Success"] = text["SettingsSaved"];
        return RedirectToAction(nameof(General));
    }

    [HttpGet]
    public async Task<IActionResult> Security(CancellationToken ct)
    {
        var state = await security.GetStateAsync(ct);
        return View(new SecuritySettingsVm
        {
            EncryptionEnabled = state.EncryptionEnabled,
            AllowReveal = state.AllowReveal,
            EncryptionSettingHealthy = state.EncryptionSettingHealthy,
            RevealSettingHealthy = state.RevealSettingHealthy
        });
    }

    [HttpPost]
    public async Task<IActionResult> Security(SecuritySettingsVm vm, string command, CancellationToken ct)
    {
        var previous = await security.GetStateAsync(ct);
        if (string.Equals(command, "encryption", StringComparison.OrdinalIgnoreCase))
        {
            if (!vm.EncryptionEnabled && previous.EncryptionEnabled &&
                !string.Equals(vm.Confirmation?.Trim(), "DISABLE", StringComparison.Ordinal))
            {
                TempData["Error"] = "اكتب DISABLE لتأكيد إيقاف إنشاء الرسائل الآمنة الجديدة. / Type DISABLE to confirm disabling new Secure Message creation.";
                return RedirectToAction(nameof(Security));
            }

            if (vm.EncryptionEnabled != previous.EncryptionEnabled)
            {
                await security.SetEncryptionEnabledAsync(vm.EncryptionEnabled, ct);
                await audit.WriteAsync(
                    vm.EncryptionEnabled ? "SECURE_MESSAGE_ENCRYPTION_ENABLED" : "SECURE_MESSAGE_ENCRYPTION_DISABLED",
                    "SecuritySettings",
                    SecureMessageSecuritySettingsService.EnabledKey,
                    $"Previous={previous.EncryptionEnabled};New={vm.EncryptionEnabled}", ct);
            }
        }
        else if (string.Equals(command, "reveal", StringComparison.OrdinalIgnoreCase))
        {
            if (!vm.AllowReveal && previous.AllowReveal &&
                !string.Equals(vm.Confirmation?.Trim(), "BLOCK-REVEAL", StringComparison.Ordinal))
            {
                TempData["Error"] = "اكتب BLOCK-REVEAL لتأكيد إيقاف فتح جميع الرسائل المشفرة مؤقتًا. / Type BLOCK-REVEAL to confirm temporarily blocking Secure Message reveal.";
                return RedirectToAction(nameof(Security));
            }

            if (vm.AllowReveal != previous.AllowReveal)
            {
                await security.SetAllowRevealAsync(vm.AllowReveal, ct);
                await audit.WriteAsync(
                    vm.AllowReveal ? "SECURE_MESSAGE_REVEAL_ENABLED" : "SECURE_MESSAGE_REVEAL_DISABLED",
                    "SecuritySettings",
                    SecureMessageSecuritySettingsService.AllowRevealKey,
                    $"Previous={previous.AllowReveal};New={vm.AllowReveal}", ct);
            }
        }
        else
        {
            return BadRequest();
        }

        TempData["Success"] = "تم حفظ إعدادات أمان الرسائل المشفرة. / Secure Message security settings saved.";
        return RedirectToAction(nameof(Security));
    }

    [HttpGet]
    public IActionResult Database() => View(new DatabaseSettingsVm { CurrentProvider = database.Current.Provider });

    [HttpPost]
    public async Task<IActionResult> Database(DatabaseSettingsVm vm, string command, CancellationToken ct)
    {
        vm.CurrentProvider = database.Current.Provider;
        if (command == "sqlite")
        {
            await database.SaveSqliteAsync(admin.CurrentUserId ?? "unknown", ct);
            await audit.WriteAsync("DATABASE_PROVIDER_SWITCH", "Database", null, "SQLite selected; restart required", ct);
            vm.Message = text["SQLiteSelected"];
            vm.TestOk = true;
            return View(vm);
        }
        if (!ModelState.IsValid)
        {
            ModelState.AddModelError("", text["ValidationCorrectFields"]);
            return View(vm);
        }

        var cs = database.BuildSqlServerConnectionString(vm.Server, vm.Database, vm.AuthenticationMode, vm.Username, vm.Password, vm.Encrypt, vm.TrustServerCertificate, vm.ConnectionTimeout);
        var test = await database.TestSqlServerAsync(cs, ct);
        vm.TestOk = test.ok;
        vm.Message = test.ok
            ? $"{text["DatabaseConnectionSucceeded"]} {test.message}"
            : $"{text["DatabaseConnectionFailed"]} {test.message}";
        if (!test.ok) return View(vm);

        if (command == "initialize")
        {
            await database.InitializeSqlServerAsync(cs, ct);
            vm.Message = text["SqlSchemaInitialized"];
            return View(vm);
        }
        if (command == "save")
        {
            await database.SaveSqlServerAsync(cs, admin.CurrentUserId ?? "unknown", ct);
            await audit.WriteAsync("DATABASE_PROVIDER_SWITCH", "Database", null, "SQL Server selected; protected connection stored; restart required", ct);
            vm.Message = text["SqlConfigSaved"];
            return View(vm);
        }
        return View(vm);
    }

    [HttpGet]
    public IActionResult Backup()
    {
        ViewBag.IsSqlite = db.Database.IsSqlite();
        ViewBag.History = backup.History();
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> CreateBackup(CancellationToken ct)
    {
        var path = await backup.CreateLocalBackupAsync(ct);
        await audit.WriteAsync("SQLITE_BACKUP_CREATE", "Database", null, Path.GetFileName(path), ct);
        TempData["Success"] = text["BackupCreated"];
        return RedirectToAction(nameof(Backup));
    }

    [HttpGet]
    public IActionResult DownloadBackup(string file)
    {
        var safe = Path.GetFileName(file);
        var info = backup.History().FirstOrDefault(x => x.Name == safe);
        return info is null ? NotFound() : PhysicalFile(info.FullName, "application/octet-stream", info.Name);
    }

    [HttpPost]
    public async Task<IActionResult> RestoreBackup(IFormFile? backupFile, string confirmation, CancellationToken ct)
    {
        if (confirmation != "RESTORE" || backupFile is null || backupFile.Length == 0)
        {
            TempData["Error"] = text["RestoreConfirmError"];
            return RedirectToAction(nameof(Backup));
        }
        await backup.StageRestoreAsync(backupFile.OpenReadStream(), ct);
        await audit.WriteAsync("SQLITE_RESTORE_STAGED", "Database", null, backupFile.FileName, ct);
        TempData["Success"] = text["RestoreStaged"];
        return RedirectToAction(nameof(Backup));
    }
}
