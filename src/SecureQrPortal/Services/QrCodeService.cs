using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SecureQrPortal.Services;

public sealed class QrCodeService
{
    public byte[] CreatePng(string url, int pixelsPerModule = 12)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.H);
        using var qr = new PngByteQRCode(data);

        var rawQr = qr.GetGraphic(Math.Clamp(pixelsPerModule, 4, 30));
        using var qrImage = Image.Load<Rgba32>(rawQr);
        using var logo = Image.Load<Rgba32>(QrLogoAsset.Bytes);

        // Keep the complete center badge small enough for dependable mobile scanning.
        // ECC H provides the redundancy required for a branded QR center mark.
        var logoSize = Math.Max(36, (int)Math.Round(qrImage.Width * 0.17));
        var padding = Math.Max(4, (int)Math.Round(qrImage.Width * 0.02));
        var badgeSize = logoSize + (padding * 2);

        logo.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(logoSize, logoSize),
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.Lanczos3
        }));

        using var badge = new Image<Rgba32>(badgeSize, badgeSize, Color.White);
        var logoX = (badgeSize - logo.Width) / 2;
        var logoY = (badgeSize - logo.Height) / 2;
        badge.Mutate(x => x.DrawImage(logo, new Point(logoX, logoY), 1f));

        var x = (qrImage.Width - badgeSize) / 2;
        var y = (qrImage.Height - badgeSize) / 2;
        qrImage.Mutate(ctx => ctx.DrawImage(badge, new Point(x, y), 1f));

        using var output = new MemoryStream();
        qrImage.SaveAsPng(output);
        return output.ToArray();
    }
}
