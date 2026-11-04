using System.Globalization;

namespace Webignite.Core.Mail;

/// <summary>
/// A mail set to go at a later time. It waits as a draft in the mailbox's Drafts folder, so nothing is lost if
/// the PC is off, and Webignite sends it at the next mail check once the time has come.
/// </summary>
public sealed record ScheduledSend
{
    public required string Id { get; init; }
    public required string AccountId { get; init; }

    /// <summary>The draft's number in the Drafts folder.</summary>
    public required uint DraftUid { get; init; }

    public required DateTimeOffset SendAt { get; init; }
    public string To { get; init; } = "";
    public string Subject { get; init; } = "";

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

public sealed record SendLaterSettings
{
    public IReadOnlyList<ScheduledSend> Scheduled { get; init; } = [];
}

public static class SendLater
{
    /// <summary>How long after Send a mail can still be taken back.</summary>
    public const int UndoSeconds = 10;

    /// <summary>The quick times to send at: the same ones snooze offers.</summary>
    public static IReadOnlyList<(string Label, DateTimeOffset When)> Choices(DateTimeOffset now) => Snooze.Choices(now);

    /// <summary>Mail whose time to go has come, oldest first.</summary>
    public static IReadOnlyList<ScheduledSend> Due(IEnumerable<ScheduledSend> scheduled, DateTimeOffset now) =>
        scheduled.Where(s => s.SendAt <= now).OrderBy(s => s.SendAt).ToList();

    /// <summary>The send set for a draft, if any.</summary>
    public static ScheduledSend? For(IEnumerable<ScheduledSend> scheduled, string accountId, uint draftUid) =>
        scheduled.FirstOrDefault(s => s.AccountId == accountId && s.DraftUid == draftUid);

    /// <summary>"today at 15:00", "tomorrow at 08:00" or "Mon 9 Nov at 08:00".</summary>
    public static string When(DateTimeOffset when, DateTimeOffset now) =>
        when.Date == now.Date ? $"today at {when.ToString("HH:mm", CultureInfo.InvariantCulture)}"
        : when.Date == now.Date.AddDays(1) ? $"tomorrow at {when.ToString("HH:mm", CultureInfo.InvariantCulture)}"
        : $"{when.ToString("ddd d MMM", CultureInfo.InvariantCulture)} at {when.ToString("HH:mm", CultureInfo.InvariantCulture)}";

    /// <summary>"Sending to Thandi in 7 seconds." while Undo is still possible.</summary>
    public static string Countdown(string to, int seconds) =>
        $"Sending to {to} in {seconds} second{(seconds == 1 ? "" : "s")}.";
}
