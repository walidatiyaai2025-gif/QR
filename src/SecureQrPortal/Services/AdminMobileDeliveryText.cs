using SecureQrPortal.Models;

namespace SecureQrPortal.Services;

public static class AdminMobileDeliveryText
{
    public static string DeliveryStatus(string? value, bool arabic) => value switch
    {
        "CREATED" => arabic ? "تم الإنشاء" : "Created",
        "PROVIDER_ACCEPTED" => arabic ? "تم قبول الإرسال من المزود" : "Provider accepted",
        "SEND_FAILED" => arabic ? "فشل الإرسال" : "Send failed",
        "REVEALED" => arabic ? "تم استعراض الرسالة" : "Revealed",
        "REVOKED" => arabic ? "ملغي" : "Revoked",
        null or "" => "—",
        _ => arabic ? "حالة إرسال غير معروفة" : "Unknown delivery status"
    };

    public static string SecurePageStatus(string? value, bool arabic) => value switch
    {
        "ACTIVE" => arabic ? "نشط" : "Active",
        "NOT_STARTED" => arabic ? "لم يبدأ" : "Not started",
        "EXPIRED" => arabic ? "منتهي" : "Expired",
        "DISABLED" => arabic ? "معطل" : "Disabled",
        "REVOKED" => arabic ? "ملغي" : "Revoked",
        "LIMIT_REACHED" => arabic ? "تم بلوغ الحد" : "Limit reached",
        null or "" => "—",
        _ => arabic ? "حالة صفحة غير معروفة" : "Unknown page status"
    };

    public static string SecurePageStatus(QrStatus value, bool arabic) =>
        SecurePageStatus(value.ToString(), arabic);

    public static string AccessLimitMode(AccessLimitMode value, bool arabic) => value switch
    {
        SecureQrPortal.Models.AccessLimitMode.ExpiryDateOnly => arabic ? "تاريخ انتهاء فقط" : "Expiry date only",
        SecureQrPortal.Models.AccessLimitMode.MaximumSuccessfulAccesses => arabic ? "الحد الأقصى لعمليات الوصول الناجحة" : "Maximum successful accesses",
        SecureQrPortal.Models.AccessLimitMode.MaximumQrOpens => arabic ? "الحد الأقصى لمرات فتح QR" : "Maximum QR opens",
        SecureQrPortal.Models.AccessLimitMode.ExpiryAndSuccessfulAccesses => arabic ? "انتهاء + وصول ناجح" : "Expiry + successful accesses",
        SecureQrPortal.Models.AccessLimitMode.ExpiryAndQrOpens => arabic ? "انتهاء + فتح QR" : "Expiry + QR opens",
        _ => arabic ? "سياسة وصول غير معروفة" : "Unknown access policy"
    };

    public static string ReminderUnit(string? value, bool arabic) => value switch
    {
        "Minutes" => arabic ? "دقائق" : "Minutes",
        "Hours" => arabic ? "ساعات" : "Hours",
        null or "" => "—",
        _ => arabic ? "وحدة تذكير غير معروفة" : "Unknown reminder unit"
    };
}
