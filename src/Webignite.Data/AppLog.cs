using System.Text;

namespace Webignite.Data;

/// <summary>
/// A plain text log on this PC, one file a day under the data folder's <c>logs</c>, kept for two weeks.
/// For problems a person would otherwise never see: a background check that failed, a crash at start-up.
/// Writing never throws, so a broken disk can't turn a logged problem into a second one.
/// </summary>
public static class AppLog
{
    public const int KeepDays = 14;
    private static readonly Lock Gate = new();
    private static string? root;

    /// <summary>Where the log files go. Set once at start-up; defaults to the normal data folder.</summary>
    public static string Folder => Path.Combine(root ?? DataPaths.Root(), "logs");

    /// <summary>Points the log at another data folder, e.g. in tests.</summary>
    public static void UseRoot(string? dataRoot) => root = dataRoot;

    /// <summary>Today's file, whether or not it exists yet.</summary>
    public static string CurrentFile(DateTimeOffset now) => Path.Combine(Folder, $"webignite-{now:yyyy-MM-dd}.log");

    /// <summary>Writes one line: the time, what was happening, and the error if there was one.</summary>
    public static void Write(string what, Exception? error = null) => Write(what, error, DateTimeOffset.Now);

    public static void Write(string what, Exception? error, DateTimeOffset now)
    {
        try
        {
            var line = new StringBuilder()
                .Append(now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
                .Append("  ")
                .Append(what);
            if (error is not null)
                line.AppendLine().Append(error);
            line.AppendLine();
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(CurrentFile(now), line.ToString());
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Nowhere to write. The problem is still reported wherever Write was called from.
        }
    }

    /// <summary>Removes files older than <see cref="KeepDays"/>. Called once at start-up.</summary>
    public static void Trim(DateTimeOffset now)
    {
        try
        {
            if (!Directory.Exists(Folder))
                return;
            var cutoff = now.Date.AddDays(-KeepDays);
            foreach (var file in Directory.EnumerateFiles(Folder, "webignite-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file)["webignite-".Length..];
                if (DateTime.TryParseExact(name, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day) && day < cutoff)
                    File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
