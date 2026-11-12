namespace Webignite.Core.Calendar;

/// <summary>A meeting or appointment, kept on this PC only.</summary>
public sealed record CalendarEvent
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required DateOnly Date { get; init; }

    /// <summary>Empty for an all-day event.</summary>
    public TimeOnly? Start { get; init; }

    public TimeOnly? End { get; init; }
    public string? Location { get; init; }
    public string? Notes { get; init; }
    public string? Customer { get; init; }

    /// <summary>Where to join a meeting held online (Teams, Zoom, Meet), when it has one.</summary>
    public string? JoinLink { get; init; }

    /// <summary>The mail it was added from, so the event can open it again.</summary>
    public string? MailId { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}
