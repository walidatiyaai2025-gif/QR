using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Areas.Admin.Controllers;
using SecureQrPortal.Controllers;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Services;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Tests;

public sealed class SecureMessageEncryptionSecurityTests
{
    [Fact]
    public async Task Default_persisted_security_settings_are_on()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Settings.SetAsync(SecureMessageSecuritySettingsService.EnabledKey, "true");
        await f.Settings.SetAsync(SecureMessageSecuritySettingsService.AllowRevealKey, "true");

        var state = await f.Security.GetStateAsync();

        Assert.True(state.EncryptionEnabled);
        Assert.True(state.AllowReveal);
        Assert.True(state.EncryptionSettingHealthy);
        Assert.True(state.RevealSettingHealthy);
    }

    [Fact]
    public async Task Missing_or_corrupt_setting_fails_secure()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Settings.SetAsync(SecureMessageSecuritySettingsService.AllowRevealKey, "not-a-bool");

        var state = await f.Security.GetStateAsync();

        Assert.True(state.EncryptionEnabled); // missing Enabled => encryption remains mandatory
        Assert.False(state.EncryptionSettingHealthy);
        Assert.False(state.AllowReveal); // unreadable reveal permission => fail closed
        Assert.False(state.RevealSettingHealthy);
    }

    [Fact]
    public void Client_contracts_expose_no_encryption_mode_override()
    {
        var clientTypes = new[]
        {
            typeof(SecureMessageAuthenticateRequest),
            typeof(SecureMessageRevealRequest),
            typeof(SecurePageEditVm)
        };

        foreach (var type in clientTypes)
            Assert.DoesNotContain(type.GetProperties(), p =>
                p.Name.Contains("Encrypt", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("EncryptionMode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Enabled_true_stores_authenticated_ciphertext_not_plaintext()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SetSecurityAsync(encryption: true, reveal: true);
        var page = NewPage(42);

        await f.Crypto.EncryptAndStoreAsync(page, "<p>سري للغاية</p>", "<p>top secret</p>");

        Assert.True(f.Crypto.IsEncrypted(page));
        Assert.StartsWith("sm:v1:", page.ContentArabicHtml, StringComparison.Ordinal);
        Assert.StartsWith("sm:v1:", page.ContentEnglishHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("سري للغاية", page.ContentArabicHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("top secret", page.ContentEnglishHtml, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(page.ProtectedContentKey));

        var tampered = page.ContentEnglishHtml;
        page.ContentEnglishHtml = tampered[..^1] + (tampered[^1] == 'A' ? "B" : "A");
        await Assert.ThrowsAnyAsync<CryptographicException>(() => f.Crypto.RevealAsync(page));
    }

    [Fact]
    public async Task Enabled_false_does_not_replace_ciphertext_with_plaintext()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SetSecurityAsync(encryption: true, reveal: true);
        var page = NewPage(43);
        await f.Crypto.EncryptAndStoreAsync(page, "قديم", "old");
        var oldArabicCiphertext = page.ContentArabicHtml;
        var oldEnglishCiphertext = page.ContentEnglishHtml;
        await f.Settings.SetAsync(SecureMessageSecuritySettingsService.EnabledKey, "false");

        await Assert.ThrowsAsync<SecureMessageEncryptionDisabledException>(() =>
            f.Crypto.EncryptAndStoreAsync(page, "نص صريح جديد", "new plaintext"));

        Assert.Equal(oldArabicCiphertext, page.ContentArabicHtml);
        Assert.Equal(oldEnglishCiphertext, page.ContentEnglishHtml);
        Assert.DoesNotContain("نص صريح جديد", page.ContentArabicHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("new plaintext", page.ContentEnglishHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enabled_false_blocks_new_or_replacement_secure_message_writes()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SetSecurityAsync(encryption: false, reveal: true);
        var page = NewPage(44);

        await Assert.ThrowsAsync<SecureMessageEncryptionDisabledException>(() =>
            f.Crypto.EncryptAndStoreAsync(page, "جديد", "new"));

        Assert.Empty(page.ContentArabicHtml);
        Assert.Empty(page.ContentEnglishHtml);
        Assert.Null(page.ProtectedContentKey);
    }

    [Fact]
    public async Task Previously_encrypted_message_remains_encrypted_when_creation_is_disabled()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SetSecurityAsync(encryption: true, reveal: true);
        var page = NewPage(45);
        await f.Crypto.EncryptAndStoreAsync(page, "المحتوى", "content");
        var cipher = page.ContentArabicHtml;

        await f.Settings.SetAsync(SecureMessageSecuritySettingsService.EnabledKey, "false");
        var revealed = await f.Crypto.RevealAsync(page);

        Assert.Equal(cipher, page.ContentArabicHtml);
        Assert.Equal("المحتوى", revealed.ArabicHtml);
        Assert.Equal("content", revealed.EnglishHtml);
    }

    [Fact]
    public async Task Disabling_creation_never_exposes_old_plaintext_in_storage()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SetSecurityAsync(encryption: true, reveal: true);
        var page = NewPage(46);
        await f.Crypto.EncryptAndStoreAsync(page, "old-ar-secret", "old-en-secret");

        await f.Settings.SetAsync(SecureMessageSecuritySettingsService.EnabledKey, "false");

        Assert.DoesNotContain("old-ar-secret", page.ContentArabicHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("old-en-secret", page.ContentEnglishHtml, StringComparison.Ordinal);
        Assert.True(f.Crypto.IsEncrypted(page));
    }

    [Fact]
    public async Task AllowReveal_false_blocks_authorized_decryption_globally()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SetSecurityAsync(encryption: true, reveal: true);
        var page = NewPage(47);
        await f.Crypto.EncryptAndStoreAsync(page, "ar", "en");
        var keyBefore = page.ProtectedContentKey;
        await f.Settings.SetAsync(SecureMessageSecuritySettingsService.AllowRevealKey, "false");

        await Assert.ThrowsAsync<SecureMessageRevealBlockedException>(() => f.Crypto.RevealAsync(page));

        Assert.Equal(keyBefore, page.ProtectedContentKey);
        Assert.Null(page.ContentKeyDestroyedAtUtc);
    }

    [Fact]
    public async Task Reenabling_AllowReveal_restores_normal_authorized_reveal()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SetSecurityAsync(encryption: true, reveal: true);
        var page = NewPage(48);
        await f.Crypto.EncryptAndStoreAsync(page, "مرحبا", "hello");
        await f.Settings.SetAsync(SecureMessageSecuritySettingsService.AllowRevealKey, "false");
        await Assert.ThrowsAsync<SecureMessageRevealBlockedException>(() => f.Crypto.RevealAsync(page));

        await f.Settings.SetAsync(SecureMessageSecuritySettingsService.AllowRevealKey, "true");
        var body = await f.Crypto.RevealAsync(page);

        Assert.Equal("مرحبا", body.ArabicHtml);
        Assert.Equal("hello", body.EnglishHtml);
    }

    [Fact]
    public void Settings_endpoint_requires_Administrator_role()
    {
        var authorize = typeof(SettingsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .ToArray();

        Assert.Contains(authorize, x => x.Roles?.Split(',').Any(r => r.Trim() == "Administrator") == true);
    }

    [Fact]
    public void Security_settings_actions_are_not_anonymous()
    {
        var methods = typeof(SettingsController).GetMethods().Where(x => x.Name == nameof(SettingsController.Security)).ToArray();
        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
            Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true)));
    }

    [Fact]
    public async Task Every_security_setting_change_creates_audit_with_identity_values_action_and_ip()
    {
        await using var f = await Fixture.CreateAsync(withAdministratorHttpContext: true);
        await f.SetSecurityAsync(encryption: true, reveal: true);
        var admin = new SecureMessageSecurityAdministrationService(f.Security, f.Audit);

        Assert.Equal(SecureMessageSecurityChangeStatus.Changed,
            (await admin.SetEncryptionEnabledAsync(false, "DISABLE")).Status);
        Assert.Equal(SecureMessageSecurityChangeStatus.Changed,
            (await admin.SetEncryptionEnabledAsync(true, null)).Status);
        Assert.Equal(SecureMessageSecurityChangeStatus.Changed,
            (await admin.SetAllowRevealAsync(false, "BLOCK-REVEAL")).Status);
        Assert.Equal(SecureMessageSecurityChangeStatus.Changed,
            (await admin.SetAllowRevealAsync(true, null)).Status);

        var logs = await f.Db.AuditLogs.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(4, logs.Count);
        Assert.Equal(new[]
        {
            "SECURE_MESSAGE_ENCRYPTION_DISABLED",
            "SECURE_MESSAGE_ENCRYPTION_ENABLED",
            "SECURE_MESSAGE_REVEAL_DISABLED",
            "SECURE_MESSAGE_REVEAL_ENABLED"
        }, logs.Select(x => x.Action));
        Assert.All(logs, log =>
        {
            Assert.Equal("admin-security-test", log.AdminUserId);
            Assert.Equal("203.0.113.25", log.IpAddress);
            Assert.Contains("Previous=", log.Details, StringComparison.Ordinal);
            Assert.Contains("New=", log.Details, StringComparison.Ordinal);
            Assert.True(log.TimestampUtc > DateTime.UtcNow.AddMinutes(-1));
        });
    }

    [Fact]
    public async Task Audit_entries_contain_no_message_key_ciphertext_password_or_plaintext_secrets()
    {
        await using var f = await Fixture.CreateAsync(withAdministratorHttpContext: true);
        await f.SetSecurityAsync(encryption: true, reveal: true);
        var admin = new SecureMessageSecurityAdministrationService(f.Security, f.Audit);
        await admin.SetEncryptionEnabledAsync(false, "DISABLE");
        await admin.SetAllowRevealAsync(false, "BLOCK-REVEAL");

        var serialized = string.Join("|", await f.Db.AuditLogs.Select(x =>
            (x.Action + ";" + x.EntityType + ";" + x.EntityId + ";" + x.Details)).ToListAsync());

        Assert.DoesNotContain("message-plaintext-secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ciphertext-secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wrapped-key-secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password-secret", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_disable_confirmation_changes_nothing_and_creates_no_audit()
    {
        await using var f = await Fixture.CreateAsync(withAdministratorHttpContext: true);
        await f.SetSecurityAsync(encryption: true, reveal: true);
        var admin = new SecureMessageSecurityAdministrationService(f.Security, f.Audit);

        var encryption = await admin.SetEncryptionEnabledAsync(false, "wrong");
        var reveal = await admin.SetAllowRevealAsync(false, "wrong");

        Assert.Equal(SecureMessageSecurityChangeStatus.ConfirmationRequired, encryption.Status);
        Assert.Equal(SecureMessageSecurityChangeStatus.ConfirmationRequired, reveal.Status);
        Assert.Empty(await f.Db.AuditLogs.ToListAsync());
        var state = await f.Security.GetStateAsync();
        Assert.True(state.EncryptionEnabled);
        Assert.True(state.AllowReveal);
    }

    [Fact]
    public async Task Expiry_or_revocation_destroys_only_message_key_and_preserves_ciphertext()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SetSecurityAsync(encryption: true, reveal: true);
        var org = new Organization { NameArabic = "اختبار", NameEnglish = "Test" };
        f.Db.Organizations.Add(org);
        await f.Db.SaveChangesAsync();
        var page = new SecurePage
        {
            OrganizationId = org.Id,
            QrReference = "QR-TEST-000001",
            PublicTokenHash = "hash-000001",
            ProtectedPublicToken = "protected",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        f.Db.SecurePages.Add(page);
        await f.Db.SaveChangesAsync();
        await f.Crypto.EncryptAndStoreAsync(page, "terminal-ar", "terminal-en");
        await f.Db.SaveChangesAsync();
        var arCipher = page.ContentArabicHtml;
        var enCipher = page.ContentEnglishHtml;

        var processor = new SecureMessageKeyLifecycleProcessor(f.Db, TimeProvider.System);
        Assert.Equal(1, await processor.DestroyTerminalKeysAsync());
        await f.Db.Entry(page).ReloadAsync();

        Assert.Null(page.ProtectedContentKey);
        Assert.NotNull(page.ContentKeyDestroyedAtUtc);
        Assert.Equal(arCipher, page.ContentArabicHtml);
        Assert.Equal(enCipher, page.ContentEnglishHtml);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => f.Crypto.RevealAsync(page));
    }

    private static SecurePage NewPage(long id) => new()
    {
        Id = id,
        QrReference = $"QR-TEST-{id:000000}",
        PublicTokenHash = $"hash-{id}",
        ProtectedPublicToken = "protected"
    };

    private sealed class FixedHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public ApplicationDbContext Db { get; }
        public AppSettingsService Settings { get; }
        public SecureMessageSecuritySettingsService Security { get; }
        public SecureMessageEncryptionService Crypto { get; }
        public AuditService Audit { get; }

        private Fixture(SqliteConnection connection, ApplicationDbContext db, IHttpContextAccessor accessor)
        {
            _connection = connection;
            Db = db;
            Settings = new AppSettingsService(db);
            Security = new SecureMessageSecuritySettingsService(Settings);
            Crypto = new SecureMessageEncryptionService(new EphemeralDataProtectionProvider(), Security);
            Audit = new AuditService(db, accessor);
        }

        public static async Task<Fixture> CreateAsync(bool withAdministratorHttpContext = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options);
            await db.Database.EnsureCreatedAsync();

            IHttpContextAccessor accessor = new FixedHttpContextAccessor();
            if (withAdministratorHttpContext)
            {
                var http = new DefaultHttpContext();
                http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.25");
                http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "admin-security-test"),
                    new Claim(ClaimTypes.Role, "Administrator")
                }, "test"));
                accessor.HttpContext = http;
            }

            return new Fixture(connection, db, accessor);
        }

        public async Task SetSecurityAsync(bool encryption, bool reveal)
        {
            await Settings.SetAsync(SecureMessageSecuritySettingsService.EnabledKey, encryption ? "true" : "false");
            await Settings.SetAsync(SecureMessageSecuritySettingsService.AllowRevealKey, reveal ? "true" : "false");
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

