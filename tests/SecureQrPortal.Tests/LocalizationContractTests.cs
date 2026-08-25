using System.Globalization;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class LocalizationContractTests
{
    [Fact]
    public void ArabicCultureUsesCriticalLocalizedAdminTerminology()
    {
        WithCulture("ar-KW", () =>
        {
            var text = new UiText();

            Assert.Equal("لوحة التحكم", text["Dashboard"]);
            Assert.Equal("الجهات", text["Organizations"]);
            Assert.Equal("الصفحات الآمنة", text["SecurePages"]);
            Assert.Equal("سجل رموز QR", text["QrRegistry"]);
            Assert.Equal("سجل الوصول", text["AccessLogs"]);
            Assert.Equal("الإعدادات", text["Settings"]);
            Assert.Equal("اسم المستخدم", text["Username"]);
            Assert.Equal("كلمة المرور", text["Password"]);
            Assert.Equal("نشط", text["ACTIVE"]);
            Assert.Equal("منتهي", text["EXPIRED"]);
            Assert.Equal("ملغي", text["REVOKED"]);
            Assert.Equal("تم بلوغ الحد", text["LIMIT_REACHED"]);
        });
    }

    [Fact]
    public void EnglishCultureUsesExpectedAdminTerminology()
    {
        WithCulture("en-US", () =>
        {
            var text = new UiText();

            Assert.Equal("Dashboard", text["Dashboard"]);
            Assert.Equal("Organizations", text["Organizations"]);
            Assert.Equal("Secure Pages", text["SecurePages"]);
            Assert.Equal("QR Code Registry", text["QrRegistry"]);
            Assert.Equal("Access Logs", text["AccessLogs"]);
            Assert.Equal("Settings", text["Settings"]);
            Assert.Equal("Username", text["Username"]);
            Assert.Equal("Password", text["Password"]);
            Assert.Equal("Active", text["ACTIVE"]);
            Assert.Equal("Expired", text["EXPIRED"]);
            Assert.Equal("Revoked", text["REVOKED"]);
            Assert.Equal("Limit reached", text["LIMIT_REACHED"]);
        });
    }

    private static void WithCulture(string name, Action assertion)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            assertion();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
