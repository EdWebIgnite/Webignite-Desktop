using System.Globalization;

namespace Webignite.Core.Clients;

/// <summary>One thing needed from a new client, and when it came in.</summary>
public sealed record OnboardingItem
{
    public required string Name { get; init; }
    public DateOnly? Received { get; init; }
    public string? Note { get; init; }

    public bool Done => Received is not null;
}

/// <summary>
/// The checklist for a new website client: what's needed before work can start, ticked off as it comes in,
/// with a plain reminder of what's still outstanding.
/// </summary>
public static class Onboarding
{
    public static readonly IReadOnlyList<string> Standard =
    [
        "Deposit paid",
        "Logo (SVG or large PNG)",
        "Brand colours and fonts",
        "Page content and text",
        "Photos",
        "Domain access or transfer code",
        "Hosting or cPanel access",
        "Email addresses wanted",
        "Sitemap signed off",
    ];

    /// <summary>A client only gets a nudge once nothing has come in for this long.</summary>
    public const int QuietDays = 7;

    public static Client Start(Client client, DateOnly today, IEnumerable<string>? items = null) => client with
    {
        Onboarding = (items ?? Standard).Select(n => new OnboardingItem { Name = n }).ToList(),
        OnboardingStarted = today,
    };

    public static Client Tick(Client client, string name, bool done, DateOnly today) => client with
    {
        Onboarding = client.Onboarding.Select(i => i.Name == name ? i with { Received = done ? i.Received ?? today : null } : i).ToList(),
    };

    public static Client Add(Client client, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || client.Onboarding.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)))
            return client;
        return client with { Onboarding = [.. client.Onboarding, new OnboardingItem { Name = name }] };
    }

    public static Client Remove(Client client, string name) => client with
    {
        Onboarding = client.Onboarding.Where(i => i.Name != name).ToList(),
    };

    public static bool InProgress(Client client) => client.Onboarding.Count > 0 && client.Onboarding.Any(i => !i.Done);

    public static IReadOnlyList<string> Outstanding(Client client) => client.Onboarding.Where(i => !i.Done).Select(i => i.Name).ToList();

    public static string Progress(Client client) => client.Onboarding.Count == 0 ? ""
        : InProgress(client) ? $"{client.Onboarding.Count(i => i.Done)} of {client.Onboarding.Count} in"
        : "All in";

    /// <summary>The day anything last came in, or the start date when nothing has.</summary>
    public static DateOnly? LastMovement(Client client) =>
        client.Onboarding.Where(i => i.Done).Select(i => i.Received).Max() ?? client.OnboardingStarted;

    /// <summary>Clients whose onboarding has stalled: something is outstanding and nothing came in for a week.</summary>
    public static IReadOnlyList<(Client Client, int Days)> Stalled(IEnumerable<Client> clients, DateOnly today) =>
        clients.Where(InProgress)
            .Select(c => (Client: c, Days: LastMovement(c) is { } last ? today.DayNumber - last.DayNumber : 0))
            .Where(x => x.Days >= QuietDays)
            .OrderByDescending(x => x.Days)
            .ToList();

    /// <summary>A friendly list of what's still needed, for a mail to the client.</summary>
    public static (string Subject, string Body) Reminder(Client client, string tradingName)
    {
        var first = client.ContactPerson is { Length: > 0 } person ? person.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] : client.Name;
        var have = client.Onboarding.Where(i => i.Done).Select(i => i.Name).ToList();
        var lines = new List<string> { $"Hi {first},", "" };
        lines.Add(have.Count > 0
            ? "Thanks for what you've sent so far. To keep the website moving, I still need:"
            : "To get started on the website, I need a few things from you:");
        lines.Add("");
        lines.AddRange(Outstanding(client).Select(n => "- " + Plain(n)));
        lines.Add("");
        lines.Add("Send them whenever you can, and shout if anything is unclear.");
        lines.Add("");
        lines.Add("Thanks,");
        lines.Add(tradingName);
        return ($"What we still need for your website, {client.Name}", string.Join("\n", lines));
    }

    // Item names are written for Eddie; the mail reads better without the bracketed notes.
    private static string Plain(string name)
    {
        var bracket = name.IndexOf(" (", StringComparison.Ordinal);
        return bracket > 0 ? name[..bracket] : name;
    }

    public static string Since(DateOnly day) => day.ToString("d MMMM", CultureInfo.InvariantCulture);
}
