namespace Webignite.Core.Documents;

public enum DocumentStatus
{
    /// <summary>Being written. Nothing has gone anywhere.</summary>
    Draft,

    /// <summary>Prepared by Claude or ChatGPT and waiting for a person to approve it.</summary>
    AwaitingApproval,

    /// <summary>Created in Xero as a draft.</summary>
    InXero,
}

/// <summary>Who wrote the document.</summary>
public enum DocumentSource
{
    App,
    Claude,
    ChatGpt,
}

/// <summary>A document as kept on this PC, with what we know about it in Xero.</summary>
public sealed record StoredDocument
{
    public required string Id { get; init; }
    public required Document Document { get; init; }
    public DocumentStatus Status { get; init; } = DocumentStatus.Draft;
    public DocumentSource Source { get; init; } = DocumentSource.App;
    public string? XeroId { get; init; }
    public string? XeroNumber { get; init; }

    /// <summary>Where the Xero write stands, and the Xero contact it used.</summary>
    public Xero.XeroWriteState XeroState { get; init; }
    public string? XeroContactId { get; init; }

    /// <summary>Exactly what was sent to Xero, kept so it can be checked against what Xero saved.</summary>
    public string? XeroSent { get; init; }

    public IReadOnlyList<string> XeroProblems { get; init; } = [];
    public DateTimeOffset Created { get; init; }
    public DateTimeOffset Updated { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}
