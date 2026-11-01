namespace Webignite.Core.Clients;

/// <summary>A client of Webignite: who they are and how to reach them.</summary>
public sealed record Client
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ContactPerson { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Website { get; init; }
    public string? Address { get; init; }
    public string? Notes { get; init; }

    /// <summary>The matching contact in Xero, once known.</summary>
    public string? XeroContactId { get; init; }

    /// <summary>Dated notes: calls, meetings, decisions. Newest first.</summary>
    public IReadOnlyList<ClientNote> Log { get; init; } = [];

    /// <summary>Files about this client (contracts, briefs), linked where they already live, e.g. OneDrive.</summary>
    public IReadOnlyList<ClientFile> Files { get; init; } = [];

    /// <summary>What's still needed from them for a new site. Empty until onboarding is started.</summary>
    public IReadOnlyList<OnboardingItem> Onboarding { get; init; } = [];

    /// <summary>When onboarding was started, for "waiting since".</summary>
    public DateOnly? OnboardingStarted { get; init; }

    public DateTimeOffset Created { get; init; }

    public static string NewId() => Guid.CreateVersion7().ToString("N");
}
