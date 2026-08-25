using SecureQrPortal.Models;

namespace SecureQrPortal.Services;

public static class QrShareMessage
{
    public const int MaxTemplateLength = 2000;

    public const string DefaultTemplate = "دولة الكويت - الديوان الأميري\n{ShareUrl}\nالرابط صالح لمرة واحدة\nيرجى حفظ اسم المستخدم وكلمة المرور لاستخدامها بعد مسح رمز QR\nتستخدم لمرة واحدة";

    public static string NormalizeTemplate(string? template)
    {
        var value = string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template.Trim();
        return value.Length <= MaxTemplateLength ? value : value[..MaxTemplateLength];
    }

    public static string Render(string? template, QrShareLink share, string shareUrl, string qrReference)
    {
        var normalized = NormalizeTemplate(template);
        return normalized
            .Replace("{ShareUrl}", shareUrl, StringComparison.OrdinalIgnoreCase)
            .Replace("{QrReference}", qrReference, StringComparison.OrdinalIgnoreCase)
            .Replace("{ExpiresAt}", StoredUtc(share.ExpiresAtUtc).ToLocalTime().ToString("dd MMM yyyy HH:mm"), StringComparison.OrdinalIgnoreCase)
            .Replace("{SessionMinutes}", share.SessionDurationMinutes.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{RevealCount}", share.MaxOpenCount.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime StoredUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
