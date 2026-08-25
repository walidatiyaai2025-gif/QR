using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SecureQrPortal.Security.Captcha;

internal interface ICaptchaImageRenderer
{
    byte[] Render(char[] answer);
}

internal sealed class CaptchaImageRenderer : ICaptchaImageRenderer
{
    private const int Width = 224;
    private const int Height = 76;

    private static readonly IReadOnlyDictionary<char, byte[]> Glyphs = new Dictionary<char, byte[]>
    {
        ['2'] = [14, 17, 1, 2, 4, 8, 31],
        ['3'] = [30, 1, 1, 14, 1, 1, 30],
        ['4'] = [18, 18, 18, 31, 2, 2, 2],
        ['5'] = [31, 16, 16, 30, 1, 1, 30],
        ['6'] = [14, 16, 16, 30, 17, 17, 14],
        ['7'] = [31, 1, 2, 4, 8, 8, 8],
        ['8'] = [14, 17, 17, 14, 17, 17, 14],
        ['9'] = [14, 17, 17, 15, 1, 1, 14],
        ['A'] = [14, 17, 17, 31, 17, 17, 17],
        ['B'] = [30, 17, 17, 30, 17, 17, 30],
        ['C'] = [15, 16, 16, 16, 16, 16, 15],
        ['D'] = [30, 17, 17, 17, 17, 17, 30],
        ['E'] = [31, 16, 16, 30, 16, 16, 31],
        ['F'] = [31, 16, 16, 30, 16, 16, 16],
        ['G'] = [14, 17, 16, 23, 17, 17, 14],
        ['H'] = [17, 17, 17, 31, 17, 17, 17],
        ['J'] = [7, 2, 2, 2, 2, 18, 12],
        ['K'] = [17, 18, 20, 24, 20, 18, 17],
        ['L'] = [16, 16, 16, 16, 16, 16, 31],
        ['M'] = [17, 27, 21, 21, 17, 17, 17],
        ['N'] = [17, 25, 21, 19, 17, 17, 17],
        ['P'] = [30, 17, 17, 30, 16, 16, 16],
        ['Q'] = [14, 17, 17, 17, 21, 18, 13],
        ['R'] = [30, 17, 17, 30, 20, 18, 17],
        ['S'] = [15, 16, 16, 14, 1, 1, 30],
        ['T'] = [31, 4, 4, 4, 4, 4, 4],
        ['U'] = [17, 17, 17, 17, 17, 17, 14],
        ['V'] = [17, 17, 17, 17, 17, 10, 4],
        ['W'] = [17, 17, 17, 17, 21, 21, 10],
        ['X'] = [17, 17, 10, 4, 10, 17, 17],
        ['Y'] = [17, 17, 10, 4, 4, 4, 4],
        ['Z'] = [31, 1, 2, 4, 8, 16, 31]
    };

    public byte[] Render(char[] answer)
    {
        using var image = new Image<Rgba32>(Width, Height, new Rgba32(7, 15, 25));

        for (var i = 0; i < 7; i++)
        {
            var color = i % 2 == 0
                ? new Rgba32(201, 164, 92, 115)
                : new Rgba32(100, 134, 170, 105);
            DrawLine(
                image,
                RandomNumberGenerator.GetInt32(Width),
                RandomNumberGenerator.GetInt32(Height),
                RandomNumberGenerator.GetInt32(Width),
                RandomNumberGenerator.GetInt32(Height),
                color);
        }

        var startX = 14;
        for (var index = 0; index < answer.Length; index++)
        {
            var glyph = Glyphs[answer[index]];
            var scale = RandomNumberGenerator.GetInt32(4, 6);
            var x = startX + (index * 34) + RandomNumberGenerator.GetInt32(-2, 3);
            var y = RandomNumberGenerator.GetInt32(8, 18);
            var shear = RandomNumberGenerator.GetInt32(-1, 2);
            var color = index % 2 == 0
                ? new Rgba32(245, 243, 238)
                : new Rgba32(226, 196, 125);

            for (var row = 0; row < 7; row++)
            {
                for (var column = 0; column < 5; column++)
                {
                    if ((glyph[row] & (1 << (4 - column))) == 0)
                        continue;

                    var pixelX = x + (column * scale) + ((row - 3) * shear);
                    var pixelY = y + (row * scale);
                    FillBlock(image, pixelX, pixelY, scale - 1, scale - 1, color);
                }
            }
        }

        for (var i = 0; i < 620; i++)
        {
            var x = RandomNumberGenerator.GetInt32(Width);
            var y = RandomNumberGenerator.GetInt32(Height);
            image[x, y] = i % 3 == 0
                ? new Rgba32(226, 196, 125, 150)
                : new Rgba32(126, 145, 166, 105);
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static void FillBlock(Image<Rgba32> image, int x, int y, int width, int height, Rgba32 color)
    {
        for (var offsetY = 0; offsetY < height; offsetY++)
        {
            for (var offsetX = 0; offsetX < width; offsetX++)
            {
                var targetX = x + offsetX;
                var targetY = y + offsetY;
                if (targetX >= 0 && targetX < Width && targetY >= 0 && targetY < Height)
                    image[targetX, targetY] = color;
            }
        }
    }

    private static void DrawLine(Image<Rgba32> image, int x0, int y0, int x1, int y1, Rgba32 color)
    {
        var deltaX = Math.Abs(x1 - x0);
        var stepX = x0 < x1 ? 1 : -1;
        var deltaY = -Math.Abs(y1 - y0);
        var stepY = y0 < y1 ? 1 : -1;
        var error = deltaX + deltaY;

        while (true)
        {
            FillBlock(image, x0, y0, 2, 2, color);
            if (x0 == x1 && y0 == y1)
                break;

            var doubledError = 2 * error;
            if (doubledError >= deltaY)
            {
                error += deltaY;
                x0 += stepX;
            }

            if (doubledError <= deltaX)
            {
                error += deltaX;
                y0 += stepY;
            }
        }
    }
}
