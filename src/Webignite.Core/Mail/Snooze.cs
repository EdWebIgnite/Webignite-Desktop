using System.Globalization;

namespace Webignite.Core.Mail;

/// <summary>
/// A mail put away until later. It waits in a hidden Snoozed folder on the server, so it also leaves the inbox on
/// Eddie's phone, and is found again by its Message-ID, since moving gives it a new number.
/// </summary>
public sealed record SnoozedMail
{
    public required string AccountId { get; init; }
    public required string MessageId { get; init; }

    /// <summary>The folder it came from and goes back to.</summary>
    public required string BackTo { get; init; }

    public required DateTimeOffset Until { get; init; }
    public string Subject { get; init; } = "";
}

/// <summary>A mail that came back from snooze, and when, so it sits at the top of the list for a while.</summary>
public sealed record WokenMail(string AccountId, string MessageId, DateTimeOffset At);

public sealed record SnoozeSettings
{
    public IReadOnlyList<SnoozedMail> Snoozed { get; init; } = [];
    public IReadOnlyList<WokenMail> Woken { get; init; } = [];
}

public static class Snooze
{
    /// <summary>The folder's name on the server. It is kept out of the folder list in the app.</summary>
    public const string FolderName = "Snoozed";

    /// <summary>How long a mail back from snooze stays at the top of the list.</summary>
    public static readonly TimeSpan TopFor = TimeSpan.FromDays(2);

    /// <summary>Morning starts at 8, evening at 6.</summary>
    private static readonly TimeSpan Morning = TimeSpan.FromHours(8);
    private static readonly TimeSpan Evening = TimeSpan.FromHours(18);

    /// <summary>
    /// The quick choices for now: later today (in three hours, on the hour) while it is before 3 in the afternoon,
    /// this evening while it is before 5, tomorrow morning, and next Monday morning.
    /// </summary>
    public static IReadOnlyList<(string Label, DateTimeOffset When)> Choices(DateTimeOffset now)
    {
        var choices = new List<(string, DateTimeOffset)>();
        var today = new DateTimeOffset(now.Date, now.Offset);
        if (now.TimeOfDay < TimeSpan.FromHours(15))
        {
            var later = today.AddHours(now.Hour + 3);
            choices.Add(($"Later today, {later.ToString("HH:mm", CultureInfo.InvariantCulture)}", later));
        }
        if (now.TimeOfDay < TimeSpan.FromHours(17))
            choices.Add(("This evening, 18:00", today + Evening));
        var tomorrow = today.AddDays(1) + Morning;
        choices.Add(($"Tomorrow morning, {Day(tomorrow)}", tomorrow));
        var toMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        var monday = today.AddDays(toMonday == 0 ? 7 : toMonday) + Morning;
        if (monday.Date != tomorrow.Date)
            choices.Add(($"Next week, {Day(monday)}", monday));
        return choices;
    }

    /// <summary>A picked day at a picked hour, in the same time zone as now. Null when that time has already passed.</summary>
    public static DateTimeOffset? At(DateOnly day, int hour, DateTimeOffset now)
    {
        var when = new DateTimeOffset(day.ToDateTime(new TimeOnly(Math.Clamp(hour, 0, 23), 0)), now.Offset);
        return when > now ? when : null;
    }

    /// <summary>"until tomorrow at 08:00" or "until Mon 9 Nov at 08:00", for the note after snoozing.</summary>
    public static string Until(DateTimeOffset when, DateTimeOffset now) =>
        when.Date == now.Date ? $"until {when.ToString("HH:mm", CultureInfo.InvariantCulture)} today"
        : when.Date == now.Date.AddDays(1) ? $"until tomorrow at {when.ToString("HH:mm", CultureInfo.InvariantCulture)}"
        : $"until {Day(when)} at {when.ToString("HH:mm", CultureInfo.InvariantCulture)}";

    /// <summary>Snoozed mail whose time has come.</summary>
    public static IReadOnlyList<SnoozedMail> Due(IEnumerable<SnoozedMail> snoozed, DateTimeOffset now) =>
        snoozed.Where(s => s.Until <= now).ToList();

    /// <summary>The mail in the Snoozed folder that a snooze stands for.</summary>
    public static MailSummary? Find(SnoozedMail snoozed, IEnumerable<MailSummary> folder) =>
        folder.FirstOrDefault(m => m.AccountId == snoozed.AccountId && string.Equals(m.MessageId?.Trim(), snoozed.MessageId.Trim(), StringComparison.Ordinal));

    /// <summary>
    /// When a mail counts as arriving, for the order of the list: when it came back from snooze if that was in the
    /// last two days, otherwise its own date. So mail back from snooze is at the top, as if it had just arrived.
    /// </summary>
    public static DateTimeOffset Arrived(MailSummary mail, IEnumerable<WokenMail> woken, DateTimeOffset now)
    {
        if (mail.MessageId is not { } id)
            return mail.Date;
        var back = woken.Where(w => w.AccountId == mail.AccountId && w.MessageId == id.Trim() && now - w.At < TopFor)
            .Select(w => (DateTimeOffset?)w.At).Max();
        return back is { } at && at > mail.Date ? at : mail.Date;
    }

    /// <summary>Drops woken marks older than <see cref="TopFor"/>, so the list doesn't grow.</summary>
    public static IReadOnlyList<WokenMail> Tidy(IEnumerable<WokenMail> woken, DateTimeOffset now) =>
        woken.Where(w => now - w.At < TopFor).ToList();

    public static bool IsSnoozeFolder(MailFolder folder) => folder.Name.Equals(FolderName, StringComparison.OrdinalIgnoreCase);

    private static string Day(DateTimeOffset when) => when.ToString("ddd d MMM", CultureInfo.InvariantCulture);
}
