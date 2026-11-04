using QRCoder;

namespace Webignite.Tools;

/// <summary>QR codes as PNG (for the screen and print) or SVG (for the web), with medium error correction.</summary>
public static class QrCodes
{
    public static byte[] Png(string content, int pixelsPerModule)
    {
        using var data = QRCodeGenerator.GenerateQrCode(content, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }

    public static string Svg(string content, int pixelsPerModule)
    {
        using var data = QRCodeGenerator.GenerateQrCode(content, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(pixelsPerModule);
    }
}
