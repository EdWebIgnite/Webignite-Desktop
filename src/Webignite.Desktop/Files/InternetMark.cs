namespace Webignite.Desktop.Files;

/// <summary>
/// Marks a file as having come from the internet, as browsers and Outlook do (the Zone.Identifier stream), so
/// Windows treats a saved attachment with care: Office opens it in Protected View and blocks its macros, and
/// SmartScreen checks a program before it runs.
/// </summary>
public static class InternetMark
{
    public static void Add(string path)
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A drive without streams (FAT, some network shares) can't carry the mark; the file is still saved.
            Webignite.Data.AppLog.Write("Couldn't mark a saved attachment as from the internet: " + e.Message);
        }
    }
}
