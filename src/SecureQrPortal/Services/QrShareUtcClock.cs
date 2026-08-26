namespace SecureQrPortal.Services;

public static class QrShareUtcClock
{
    public static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
