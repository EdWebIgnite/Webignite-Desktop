using System.Globalization;

namespace Webignite.Core.Backups;

/// <summary>Where backups go and how the last one went. The password itself lives in Credential Manager.</summary>
public sealed record BackupSettings
{
    public string? Folder { get; init; }
    public DateTimeOffset? LastBackup { get; init; }
    public string? LastFile { get; init; }
    public string? LastProblem { get; init; }
    public DateTimeOffset? LastProblemAt { get; init; }
}

/// <summary>A backup file found in the backup folder.</summary>
public sealed record BackupFile(string Path, DateTimeOffset Made);

public static class BackupRules
{
    public const string Extension = ".wbak";
    public const int Keep = 14;
    public const int MinimumPasswordLength = 10;
    public static readonly TimeSpan Every = TimeSpan.FromDays(1);

    /// <summary>A backup is due once a day, and straight away if there has never been one.</summary>
    public static bool IsDue(BackupSettings settings, DateTimeOffset now) =>
        settings.LastBackup is not { } last || now - last >= Every;

    /// <summary>"Webignite backup 2026-11-01 1030.wbak", in local time so it sorts and reads naturally.</summary>
    public static string FileName(DateTimeOffset made) =>
        "Webignite backup " + made.ToLocalTime().ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture) + Extension;

    /// <summary>Reads the time back out of a backup file name, or null for any other file.</summary>
    public static DateTimeOffset? MadeAt(string fileName)
    {
        const string prefix = "Webignite backup ";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) || !fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            return null;
        var stamp = fileName[prefix.Length..^Extension.Length];
        return DateTime.TryParseExact(stamp, "yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local)
            ? new DateTimeOffset(local)
            : null;
    }

    /// <summary>The oldest backups beyond the newest <see cref="Keep"/>. Only the app's own backup files are ever counted.</summary>
    public static IReadOnlyList<BackupFile> ToRemove(IEnumerable<BackupFile> files, int keep = Keep) =>
        files.OrderByDescending(f => f.Made).Skip(keep).ToList();

    /// <summary>Why a backup password can't be used, or null when it's fine.</summary>
    public static string? PasswordProblem(string password, string repeated)
    {
        if (password.Length < MinimumPasswordLength)
            return $"Use at least {MinimumPasswordLength} characters.";
        if (password != repeated)
            return "The two passwords don't match.";
        return null;
    }

    /// <summary>What the Settings page says about backups.</summary>
    public static string Status(BackupSettings settings, bool hasPassword, DateTimeOffset now)
    {
        if (!hasPassword)
            return "Backups are off. Choose a backup password to turn them on.";
        if (settings.LastProblem is { } problem && (settings.LastBackup is not { } ok || settings.LastProblemAt > ok))
            return "The last backup failed: " + problem;
        if (settings.LastBackup is not { } last)
            return "No backup yet. The first one runs in a moment.";
        var local = last.ToLocalTime();
        var when = local.Date == now.ToLocalTime().Date ? "today" : local.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        return $"Last backup {when} at {local:HH:mm}.";
    }

    /// <summary>True when backups have been failing or missing for more than two days, worth a red note.</summary>
    public static bool Overdue(BackupSettings settings, bool hasPassword, DateTimeOffset now) =>
        hasPassword && settings.LastBackup is { } last && now - last > TimeSpan.FromDays(2);
}
