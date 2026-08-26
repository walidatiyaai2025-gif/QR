using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;

namespace SecureQrPortal.Services;

public sealed record SecureMessageSecurityState(
    bool EncryptionEnabled,
    bool AllowReveal,
    bool EncryptionSettingHealthy,
    bool RevealSettingHealthy);

public sealed class SecureMessageEncryptionDisabledException : InvalidOperationException
{
    public SecureMessageEncryptionDisabledException() : base("Secure Message encryption is disabled by system settings.") { }
}

public sealed class SecureMessageRevealBlockedException : InvalidOperationException
{
    public SecureMessageRevealBlockedException() : base("Secure Message reveal is blocked by system settings.") { }
}

public sealed class SecureMessageSecuritySettingsService(AppSettingsService settings)
{
    public const string EnabledKey = "SecureMessageEncryption.Enabled";
    public const string AllowRevealKey = "SecureMessageEncryption.AllowReveal";

    public async Task<SecureMessageSecurityState> GetStateAsync(CancellationToken ct = default)
    {
        var all = await settings.GetAllAsync(ct);
        var encryption = ParseStrict(all, EnabledKey, failSecureValue: true);
        var reveal = ParseStrict(all, AllowRevealKey, failSecureValue: false);
        return new SecureMessageSecurityState(encryption.Value, reveal.Value, encryption.Healthy, reveal.Healthy);
    }

    public Task SetEncryptionEnabledAsync(bool enabled, CancellationToken ct = default) =>
        settings.SetAsync(EnabledKey, enabled ? "true" : "false", ct);

    public Task SetAllowRevealAsync(bool enabled, CancellationToken ct = default) =>
        settings.SetAsync(AllowRevealKey, enabled ? "true" : "false", ct);

    private static (bool Value, bool Healthy) ParseStrict(
        IReadOnlyDictionary<string, string> values,
        string key,
        bool failSecureValue)
    {
        if (!values.TryGetValue(key, out var raw) || !bool.TryParse(raw, out var parsed))
            return (failSecureValue, false);
        return (parsed, true);
    }
}

public sealed record SecureMessageBody(string ArabicHtml, string EnglishHtml);

public sealed class SecureMessageEncryptionService(
    IDataProtectionProvider dataProtection,
    SecureMessageSecuritySettingsService security)
{
    public const int CurrentVersion = 1;
    private const int DataKeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const string EnvelopePrefix = "sm:v1:";
    private readonly IDataProtector _keyProtector = dataProtection.CreateProtector("SecureQrPortal.SecureMessage.DataKey.v1");

    public async Task EncryptAndStoreAsync(
        SecurePage page,
        string arabicHtml,
        string englishHtml,
        CancellationToken ct = default)
    {
        var state = await security.GetStateAsync(ct);
        if (!state.EncryptionEnabled)
            throw new SecureMessageEncryptionDisabledException();

        EncryptAndStoreCore(page, arabicHtml, englishHtml);
    }

    public void EncryptLegacyPlaintextForMigration(SecurePage page, string arabicHtml, string englishHtml)
    {
        if (page.ContentEncryptionVersion != 0 ||
            !string.IsNullOrWhiteSpace(page.ProtectedContentKey) ||
            LooksEncrypted(page.ContentArabicHtml) ||
            LooksEncrypted(page.ContentEnglishHtml))
            throw new CryptographicException($"Secure Message {page.Id} has an inconsistent legacy encryption state.");

        EncryptAndStoreCore(page, arabicHtml, englishHtml);
    }

    public async Task<SecureMessageBody> RevealAsync(SecurePage page, CancellationToken ct = default)
    {
        var state = await security.GetStateAsync(ct);
        if (!state.AllowReveal)
            throw new SecureMessageRevealBlockedException();
        return DecryptCore(page);
    }

    public bool IsEncrypted(SecurePage page) =>
        page.ContentEncryptionVersion == CurrentVersion &&
        !string.IsNullOrWhiteSpace(page.ProtectedContentKey) &&
        LooksEncrypted(page.ContentArabicHtml) &&
        LooksEncrypted(page.ContentEnglishHtml);

    private void EncryptAndStoreCore(SecurePage page, string arabicHtml, string englishHtml)
    {
        if (page.Id <= 0)
            throw new InvalidOperationException("Secure Message must have a persistent server identity before encryption.");

        var key = RandomNumberGenerator.GetBytes(DataKeySize);
        try
        {
            page.ContentArabicHtml = EncryptBody(key, arabicHtml ?? string.Empty, Aad(page.Id, "ar"));
            page.ContentEnglishHtml = EncryptBody(key, englishHtml ?? string.Empty, Aad(page.Id, "en"));
            page.ProtectedContentKey = Convert.ToBase64String(_keyProtector.Protect(key));
            page.ContentEncryptionVersion = CurrentVersion;
            page.ContentKeyDestroyedAtUtc = null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private SecureMessageBody DecryptCore(SecurePage page)
    {
        if (page.ContentKeyDestroyedAtUtc.HasValue || string.IsNullOrWhiteSpace(page.ProtectedContentKey))
            throw new CryptographicException("Secure Message data key is unavailable.");
        if (page.ContentEncryptionVersion != CurrentVersion ||
            !LooksEncrypted(page.ContentArabicHtml) ||
            !LooksEncrypted(page.ContentEnglishHtml))
            throw new CryptographicException("Secure Message ciphertext envelope is invalid or unsupported.");

        byte[] key;
        try
        {
            key = _keyProtector.Unprotect(Convert.FromBase64String(page.ProtectedContentKey));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new CryptographicException("Secure Message data key could not be unwrapped.", ex);
        }

        try
        {
            return new SecureMessageBody(
                DecryptBody(key, page.ContentArabicHtml, Aad(page.Id, "ar")),
                DecryptBody(key, page.ContentEnglishHtml, Aad(page.Id, "en")));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static string EncryptBody(byte[] key, string plaintext, string aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, ciphertext, tag, Encoding.UTF8.GetBytes(aad));
        CryptographicOperations.ZeroMemory(plain);
        return $"{EnvelopePrefix}{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(ciphertext)}";
    }

    private static string DecryptBody(byte[] key, string envelope, string aad)
    {
        var parts = envelope.Split(':', 5);
        if (parts.Length != 5 || parts[0] != "sm" || parts[1] != "v1")
            throw new CryptographicException("Invalid Secure Message ciphertext envelope.");

        try
        {
            var nonce = Convert.FromBase64String(parts[2]);
            var tag = Convert.FromBase64String(parts[3]);
            var ciphertext = Convert.FromBase64String(parts[4]);
            if (nonce.Length != NonceSize || tag.Length != TagSize)
                throw new CryptographicException("Invalid Secure Message ciphertext envelope.");

            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(aad));
            try { return Encoding.UTF8.GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Invalid Secure Message ciphertext encoding.", ex);
        }
    }

    private static string Aad(long pageId, string language) =>
        $"SecureQrPortal|SecureMessage|{pageId}|{language}|v1";

    private static bool LooksEncrypted(string? value) =>
        value?.StartsWith(EnvelopePrefix, StringComparison.Ordinal) == true;
}

public sealed class SecureMessageKeyLifecycleProcessor(
    ApplicationDbContext db,
    TimeProvider timeProvider)
{
    public async Task<int> DestroyTerminalKeysAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return await db.SecurePages
            .Where(x => x.ProtectedContentKey != null &&
                        (x.RevokedAtUtc != null || (x.ExpiresAtUtc != null && x.ExpiresAtUtc <= now)))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.ProtectedContentKey, (string?)null)
                .SetProperty(x => x.ContentKeyDestroyedAtUtc, now), ct);
    }
}

public sealed class SecureMessageKeyLifecycleBackgroundService(
    IServiceScopeFactory scopes,
    ILogger<SecureMessageKeyLifecycleBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<SecureMessageKeyLifecycleProcessor>();
                await processor.DestroyTerminalKeysAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Secure Message terminal key lifecycle processing failed.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
