namespace SecureQrPortal.Services;

public enum SecureMessageSecurityChangeStatus
{
    Changed,
    NoChange,
    ConfirmationRequired
}

public sealed record SecureMessageSecurityChangeResult(
    SecureMessageSecurityChangeStatus Status,
    bool PreviousValue,
    bool NewValue);

public sealed class SecureMessageSecurityAdministrationService(
    SecureMessageSecuritySettingsService security,
    AuditService audit)
{
    public async Task<SecureMessageSecurityChangeResult> SetEncryptionEnabledAsync(
        bool enabled,
        string? confirmation,
        CancellationToken ct = default)
    {
        var previous = await security.GetStateAsync(ct);
        if (!enabled && previous.EncryptionEnabled &&
            !string.Equals(confirmation?.Trim(), "DISABLE", StringComparison.Ordinal))
            return new(SecureMessageSecurityChangeStatus.ConfirmationRequired, previous.EncryptionEnabled, enabled);

        if (enabled == previous.EncryptionEnabled)
            return new(SecureMessageSecurityChangeStatus.NoChange, previous.EncryptionEnabled, enabled);

        await security.SetEncryptionEnabledAsync(enabled, ct);
        await audit.WriteAsync(
            enabled ? "SECURE_MESSAGE_ENCRYPTION_ENABLED" : "SECURE_MESSAGE_ENCRYPTION_DISABLED",
            "SecuritySettings",
            SecureMessageSecuritySettingsService.EnabledKey,
            $"Previous={previous.EncryptionEnabled};New={enabled}", ct);
        return new(SecureMessageSecurityChangeStatus.Changed, previous.EncryptionEnabled, enabled);
    }

    public async Task<SecureMessageSecurityChangeResult> SetAllowRevealAsync(
        bool enabled,
        string? confirmation,
        CancellationToken ct = default)
    {
        var previous = await security.GetStateAsync(ct);
        if (!enabled && previous.AllowReveal &&
            !string.Equals(confirmation?.Trim(), "BLOCK-REVEAL", StringComparison.Ordinal))
            return new(SecureMessageSecurityChangeStatus.ConfirmationRequired, previous.AllowReveal, enabled);

        if (enabled == previous.AllowReveal)
            return new(SecureMessageSecurityChangeStatus.NoChange, previous.AllowReveal, enabled);

        await security.SetAllowRevealAsync(enabled, ct);
        await audit.WriteAsync(
            enabled ? "SECURE_MESSAGE_REVEAL_ENABLED" : "SECURE_MESSAGE_REVEAL_DISABLED",
            "SecuritySettings",
            SecureMessageSecuritySettingsService.AllowRevealKey,
            $"Previous={previous.AllowReveal};New={enabled}", ct);
        return new(SecureMessageSecurityChangeStatus.Changed, previous.AllowReveal, enabled);
    }
}
