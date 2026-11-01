namespace Webignite.Core.Tasks;

public enum TaskSource
{
    /// <summary>Typed in by a person.</summary>
    App,

    /// <summary>Raised by the app from quotes and invoices, e.g. a follow-up.</summary>
    Auto,

    Claude,
    ChatGpt,
}

/// <summary>A to-do. Called TaskItem to stay clear of System.Threading.Tasks.Task.</summary>
public sealed record TaskItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Notes { get; init; }
    public DateOnly? Due { get; init; }
    public bool Done { get; init; }
    public DateTimeOffset? DoneAt { get; init; }
    public TaskSource Source { get; init; } = TaskSource.App;

    /// <summary>The quote or invoice this is about, if any.</summary>
    public string? DocumentId { get; init; }

    public string? Customer { get; init; }

    /// <summary>
    /// For automatic tasks: what raised it, e.g. "quote-follow-up:{id}". The same key is never raised
    /// twice, so ticking one off or deleting it makes it stay gone.
    /// </summary>
    public string? AutoKey { get; init; }

    public DateTimeOffset Created { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}
