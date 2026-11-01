using System.Text;

namespace Webignite.Core.Tools;

public enum QrKind { Link, Text, Wifi, Email, Phone }

public enum WifiSecurity { Wpa, Wep, Open }

/// <summary>What goes into a QR code. Phones read Wi-Fi, mail and phone codes in these standard forms.</summary>
public static class QrText
{
    public static string? Problem(QrKind kind, string text, string? wifiPassword = null, WifiSecurity security = WifiSecurity.Wpa)
    {
        if (string.IsNullOrWhiteSpace(text))
            return kind switch
            {
                QrKind.Link => "Type the web address.",
                QrKind.Wifi => "Type the network name.",
                QrKind.Email => "Type the email address.",
                QrKind.Phone => "Type the phone number.",
                _ => "Type what the code should say.",
            };
        if (kind == QrKind.Wifi && security != WifiSecurity.Open && string.IsNullOrEmpty(wifiPassword))
            return "Type the Wi-Fi password, or choose an open network.";
        if (kind == QrKind.Email && !text.Contains('@'))
            return "That doesn't look like an email address.";
        if (Build(kind, text, wifiPassword, security).Length > 1200)
            return "That's too long for a code a phone can read easily.";
        return null;
    }

    public static string Build(QrKind kind, string text, string? wifiPassword = null, WifiSecurity security = WifiSecurity.Wpa)
    {
        text = text.Trim();
        return kind switch
        {
            QrKind.Link => text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text,
            QrKind.Wifi => $"WIFI:T:{(security switch { WifiSecurity.Wep => "WEP", WifiSecurity.Open => "nopass", _ => "WPA" })};S:{Escape(text)};"
                + (security == WifiSecurity.Open ? "" : $"P:{Escape(wifiPassword ?? "")};") + ";",
            QrKind.Email => "mailto:" + text,
            QrKind.Phone => "tel:" + Phone(text),
            _ => text,
        };
    }

    // The Wi-Fi format uses ; , : " and \ as markers, so they're escaped with a backslash.
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '\\' or ';' or ',' or ':' or '"')
                builder.Append('\\');
            builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>A South African number in international form, so it dials from any phone: 082 555 0142 becomes +27825550142.</summary>
    public static string Phone(string text)
    {
        var digits = new string(text.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (digits.StartsWith('+'))
            return digits;
        if (digits.StartsWith("27", StringComparison.Ordinal) && digits.Length == 11)
            return "+" + digits;
        if (digits.StartsWith('0') && digits.Length == 10)
            return "+27" + digits[1..];
        return digits;
    }
}
