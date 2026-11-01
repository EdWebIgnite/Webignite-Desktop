namespace Webignite.Core.Time;

/// <summary>
/// Time spent on something. It can belong to a client, to one of that client's projects, or to nothing at all
/// (admin, learning, quoting): <see cref="ClientId"/> and <see cref="ProjectId"/> are both optional.
/// </summary>
public sealed record TimeEntry
{
    public required string Id { get; init; }
    public required DateOnly Date { get; init; }
    public required int Minutes { get; init; }

    /// <summary>What it was for, e.g. "Homepage layout" or "Bookkeeping".</summary>
    public string Note { get; init; } = "";

    public string? ClientId { get; init; }

    /// <summary>A project of the client above. Ignored without a client.</summary>
    public string? ProjectId { get; init; }

    public DateTimeOffset Created { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}

/// <summary>The timer that's running, kept so it survives closing the app.</summary>
public sealed record RunningTimer
{
    public required DateTimeOffset Started { get; init; }
    public string Note { get; init; } = "";
    public string? ClientId { get; init; }
    public string? ProjectId { get; init; }
}
