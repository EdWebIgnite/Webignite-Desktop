using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Webignite.Core.Files;

namespace Webignite.Desktop.Files;

/// <summary>
/// Offers Webignite to Windows for pictures, or stops offering it, by the entries in <see cref="PictureAssociation"/>.
/// Only the signed-in user's part of the registry is touched.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PictureDefault
{
    public static bool IsOffered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{PictureAssociation.ProgId}\shell\open\command");
        return key?.GetValue(null) is string command && Environment.ProcessPath is { } exe
            && command == PictureAssociation.Command(PictureAssociation.Launcher(exe, File.Exists));
    }

    public static void Offer()
    {
        if (Environment.ProcessPath is not { } exe)
            throw new InvalidOperationException("Couldn't tell where Webignite is installed.");
        foreach (var entry in PictureAssociation.Entries(PictureAssociation.Launcher(exe, File.Exists)))
        {
            using var key = Registry.CurrentUser.CreateSubKey(entry.Key, writable: true);
            key.SetValue(entry.Name ?? "", entry.Value);
        }
        Changed();
    }

    public static void StopOffering()
    {
        foreach (var path in PictureAssociation.KeysToRemove)
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        foreach (var entry in PictureAssociation.ValuesToRemove)
        {
            using var key = Registry.CurrentUser.OpenSubKey(entry.Key, writable: true);
            key?.DeleteValue(entry.Name ?? "", throwOnMissingValue: false);
        }
        Changed();
    }

    // Tells Explorer the file kinds changed, so "Open with" shows it straight away.
    private static void Changed() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
