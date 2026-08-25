namespace SecureQrPortal;

public static class Branding
{
    public const string ArabicName = "الديوان الأميري";
    public const string EnglishName = "Al Diwan Al Amiri";
    public const string LogoPath = "/branding/diwan-logo";

    public static string Name(bool arabic) => arabic ? ArabicName : EnglishName;
}
