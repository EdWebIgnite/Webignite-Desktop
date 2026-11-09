namespace Webignite.Core.Mail;

public enum PreviewKind
{
    None,
    Pdf,
    Picture,
}

/// <summary>Which attachments open inside the app rather than in another program: PDFs and pictures, not too big.</summary>
public static class AttachmentPreview
{
    /// <summary>Files that run as programs. They can be saved, but the app never starts them.</summary>
    private static readonly HashSet<string> Programs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".scr", ".pif", ".msi", ".msp", ".js", ".jse", ".vbs", ".vbe", ".wsf", ".wsh",
        ".ps1", ".psm1", ".hta", ".jar", ".lnk", ".reg", ".cpl", ".dll", ".iso", ".img", ".vhd", ".vhdx", ".appx", ".msix",
        ".url", ".chm", ".msc", ".application", ".appref-ms", ".xll", ".settingcontent-ms", ".diagcab", ".wsc", ".sct", ".scf",
        ".inf", ".gadget", ".mht", ".mhtml", ".vb", ".ws", ".psd1", ".ps1xml", ".msh", ".appinstaller", ".library-ms", ".search-ms",
    };

    /// <summary>Whether an attachment would run as a program if opened. A security rule: such files are only saved.</summary>
    public static bool IsProgram(string fileName) => Programs.Contains(Path.GetExtension(fileName.Trim().TrimEnd('.')));

    /// <summary>Bigger than this opens in another program, so a huge scan doesn't stall the app.</summary>
    public const long MaxSize = 30 * 1024 * 1024;

    /// <summary>PDFs show at most this many pages; the rest open in another program.</summary>
    public const int MaxPages = 30;

    private static readonly HashSet<string> Pictures = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    public static PreviewKind Kind(string fileName, string? contentType, long size)
    {
        if (size > MaxSize)
            return PreviewKind.None;
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return PreviewKind.Pdf;
        if (Pictures.Contains(extension) || (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true && !contentType.Contains("svg", StringComparison.OrdinalIgnoreCase)))
            return PreviewKind.Picture;
        return PreviewKind.None;
    }
}
