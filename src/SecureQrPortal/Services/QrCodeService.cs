using QRCoder;

namespace SecureQrPortal.Services;

public sealed class QrCodeService
{
    public byte[] CreatePng(string url, int pixelsPerModule = 12)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using var qr = new PngByteQRCode(data);
        return qr.GetGraphic(Math.Clamp(pixelsPerModule, 4, 30));
    }
}
