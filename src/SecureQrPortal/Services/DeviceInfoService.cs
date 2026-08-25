namespace SecureQrPortal.Services;

public sealed record DeviceInfo(string DeviceType, string Browser);

public sealed class DeviceInfoService
{
    public DeviceInfo Parse(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;
        var device = ua.Contains("Mobile", StringComparison.OrdinalIgnoreCase) || ua.Contains("Android", StringComparison.OrdinalIgnoreCase) || ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "Mobile" : "Desktop/Tablet";
        var browser = ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge" : ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome" : ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox" : ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari" : "Other";
        return new DeviceInfo(device, browser);
    }
}
