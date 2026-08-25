using System.Globalization;

namespace SecureQrPortal.Services;

public sealed class UiText
{
    private static readonly Dictionary<string,string> Ar = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dashboard"]="لوحة التحكم", ["Organizations"]="الجهات", ["SecurePages"]="الصفحات الآمنة", ["QrRegistry"]="سجل رموز QR", ["AccessLogs"]="سجل الوصول", ["AuditLog"]="سجل التدقيق", ["Settings"]="الإعدادات", ["Logout"]="تسجيل الخروج", ["Search"]="بحث", ["Create"]="إنشاء", ["Edit"]="تعديل", ["Delete"]="حذف", ["Actions"]="الإجراءات", ["Status"]="الحالة", ["Organization"]="الجهة", ["Page"]="الصفحة", ["CreatedAt"]="تاريخ الإنشاء", ["Expiry"]="الانتهاء", ["Save"]="حفظ", ["Cancel"]="إلغاء", ["Details"]="التفاصيل", ["Language"]="اللغة", ["Login"]="دخول", ["Username"]="اسم المستخدم", ["Password"]="كلمة المرور", ["InvalidLink"]="الرابط غير صالح", ["InvalidLinkText"]="هذا الرابط غير متاح أو انتهت صلاحيته.", ["Back"]="رجوع", ["QRManagement"]="إدارة QR", ["Database"]="قاعدة البيانات", ["Backup"]="النسخ الاحتياطي", ["General"]="عام", ["DemoData"]="بيانات تجريبية"
    };
    public string this[string key] => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" && Ar.TryGetValue(key, out var v) ? v : key;
}
