using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using QRCoder;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

// QR codes for links, Wi-Fi, email and phone numbers, saved as PNG for print or SVG for the web.
public sealed partial class ToolsViewModel
{
    public IReadOnlyList<QrChoice> QrKinds { get; } =
    [
        new(QrKind.Link, "Web address"), new(QrKind.Wifi, "Wi-Fi"), new(QrKind.Email, "Email"),
        new(QrKind.Phone, "Phone"), new(QrKind.Text, "Plain text"),
    ];

    public IReadOnlyList<WifiChoice> WifiKinds { get; } =
        [new(WifiSecurity.Wpa, "WPA or WPA2 (most networks)"), new(WifiSecurity.Wep, "WEP (old)"), new(WifiSecurity.Open, "Open, no password")];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWifi), nameof(QrPlaceholder))]
    public partial QrChoice? QrKindChoice { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WifiNeedsPassword))]
    public partial WifiChoice? WifiKindChoice { get; set; }

    [ObservableProperty] public partial string QrInput { get; set; } = "";
    [ObservableProperty] public partial string WifiPassword { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQr))]
    public partial Bitmap? QrImage { get; set; }

    [ObservableProperty] public partial string QrMessage { get; set; } = "";

    public bool HasQr => QrImage is not null;
    public bool IsWifi => QrKindChoice?.Kind == QrKind.Wifi;
    public bool WifiNeedsPassword => IsWifi && WifiKindChoice?.Security != WifiSecurity.Open;

    public string QrPlaceholder => QrKindChoice?.Kind switch
    {
        QrKind.Wifi => "Network name, e.g. KarooCoffee-Guest",
        QrKind.Email => "e.g. hello@karoocoffee.co.za",
        QrKind.Phone => "e.g. 082 555 0142",
        QrKind.Text => "Anything, e.g. a voucher code",
        _ => "e.g. karoocoffee.co.za/menu",
    };

    /// <summary>What the code says, or null when something is missing.</summary>
    public string? QrContent { get; private set; }

    partial void OnQrKindChoiceChanged(QrChoice? value) => MakeQr();
    partial void OnWifiKindChoiceChanged(WifiChoice? value) => MakeQr();
    partial void OnQrInputChanged(string value) => MakeQr();
    partial void OnWifiPasswordChanged(string value) => MakeQr();

    private void MakeQr()
    {
        var kind = QrKindChoice?.Kind ?? QrKind.Link;
        var security = WifiKindChoice?.Security ?? WifiSecurity.Wpa;
        var old = QrImage;
        if (QrText.Problem(kind, QrInput, WifiPassword, security) is { } problem)
        {
            QrContent = null;
            QrImage = null;
            QrMessage = QrInput.Length > 0 ? problem : "";
        }
        else
        {
            QrContent = QrText.Build(kind, QrInput, WifiPassword, security);
            using var data = QRCodeGenerator.GenerateQrCode(QrContent, QRCodeGenerator.ECCLevel.M);
            using var stream = new MemoryStream(new PngByteQRCode(data).GetGraphic(8));
            QrImage = new Bitmap(stream);
            QrMessage = kind == QrKind.Wifi ? "Scanning it joins the network. Test it with your phone before printing." : "Test it with your phone before printing.";
        }
        old?.Dispose();
    }

    /// <summary>A large PNG, sharp enough for print.</summary>
    public byte[]? QrPng()
    {
        if (QrContent is null)
            return null;
        using var data = QRCodeGenerator.GenerateQrCode(QrContent, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(24);
    }

    /// <summary>An SVG, for websites and for printers that want artwork that scales.</summary>
    public string? QrSvg()
    {
        if (QrContent is null)
            return null;
        using var data = QRCodeGenerator.GenerateQrCode(QrContent, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(10);
    }

    public void QrSaved(string name) => QrMessage = $"Saved {name}.";
}

public sealed record QrChoice(QrKind Kind, string Label)
{
    public override string ToString() => Label;
}

public sealed record WifiChoice(WifiSecurity Security, string Label)
{
    public override string ToString() => Label;
}
