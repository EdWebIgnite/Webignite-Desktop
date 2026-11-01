using System.Globalization;
using System.Text;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Mail;
using Webignite.Core.Renewals;
using Webignite.Core.Tasks;

namespace Webignite.Core.Search;

public enum HitKind { Client, Quote, Invoice, Mail, Todo, Renewal }

/// <summary>One search result. Exactly one of the references is set, matching the kind.</summary>
public sealed record SearchHit(HitKind Kind, string Title, string Detail, int Score)
{
    public Client? Client { get; init; }
    public StoredDocument? Document { get; init; }
    public MailSummary? Mail { get; init; }
    public TaskItem? Task { get; init; }
    public Renewal? Renewal { get; init; }

    public string KindLabel => Kind switch
    {
        HitKind.Client => "Client",
        HitKind.Quote => "Quote",
        HitKind.Invoice => "Invoice",
        HitKind.Mail => "Mail",
        HitKind.Todo => "To-do",
        _ => "Renewal",
    };
}

/// <summary>What Ctrl+K looks through.</summary>
public sealed record SearchSources
{
    public IReadOnlyList<Client> Clients { get; init; } = [];
    public IReadOnlyList<StoredDocument> Documents { get; init; } = [];
    public IReadOnlyList<MailSummary> Mail { get; init; } = [];
    public IReadOnlyList<TaskItem> Tasks { get; init; } = [];
    public IReadOnlyList<Renewal> Renewals { get; init; } = [];
}

/// <summary>
/// Search across clients, quotes, invoices, mail, to-dos and renewals. Every word typed must appear somewhere;
/// a match at the start of the title ranks highest. Accents and case don't matter.
/// </summary>
public static class QuickSearch
{
    public const int Limit = 30;
    private const int PerKind = 8;

    public static IReadOnlyList<SearchHit> Find(string query, SearchSources sources)
    {
        var words = Words(query);
        if (words.Length == 0)
            return [];
        var hits = new List<SearchHit>();

        Add(hits, words, sources.Clients.Select(c => new SearchHit(HitKind.Client, c.Name,
            Join(c.ContactPerson, c.Email, c.Phone, c.Website), 0) { Client = c }),
            c => Join(c.Client!.Notes, c.Client.Address));

        foreach (var kind in new[] { DocumentKind.Quote, DocumentKind.Invoice })
        {
            Add(hits, words, sources.Documents.Where(d => d.Document.Kind == kind).Select(d => new SearchHit(
                kind == DocumentKind.Quote ? HitKind.Quote : HitKind.Invoice,
                Join(d.XeroNumber, d.Document.Customer.Name),
                Join(d.Document.Reference, d.Document.Total is { } total ? Money.Rand(total) : null,
                    d.Document.Date?.ToString("d MMM yyyy", CultureInfo.InvariantCulture)), 0) { Document = d }),
                h => Join(string.Join(" ", h.Document!.Document.Lines.Select(l => l.Description)), h.Document.Document.Customer.Email));
        }

        Add(hits, words, sources.Mail.OrderByDescending(m => m.Date).Select(m => new SearchHit(HitKind.Mail,
            string.IsNullOrWhiteSpace(m.Subject) ? "(no subject)" : m.Subject,
            Join(m.From?.Display, m.Date.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)), 0) { Mail = m }),
            h => Join(h.Mail!.From?.Address, h.Mail.Preview));

        Add(hits, words, sources.Tasks.Where(t => !t.Done).Select(t => new SearchHit(HitKind.Todo, t.Title,
            Join(t.Customer, t.Due?.ToString("d MMM yyyy", CultureInfo.InvariantCulture)), 0) { Task = t }),
            h => h.Task!.Notes ?? "");

        Add(hits, words, sources.Renewals.Select(r => new SearchHit(HitKind.Renewal, r.Name,
            Join(RenewalRules.KindLabel(r.Kind), r.ClientName, "renews " + r.RenewsOn.ToString("d MMM yyyy", CultureInfo.InvariantCulture)), 0) { Renewal = r }),
            h => h.Renewal!.Provider ?? "");

        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Kind).Take(Limit).ToList();
    }

    private static void Add(List<SearchHit> hits, string[] words, IEnumerable<SearchHit> candidates, Func<SearchHit, string> more)
    {
        var found = new List<SearchHit>();
        foreach (var hit in candidates)
        {
            var title = Fold(hit.Title);
            var rest = Fold(hit.Detail + " " + more(hit));
            var score = 0;
            var all = true;
            foreach (var word in words)
            {
                if (title.StartsWith(word, StringComparison.Ordinal))
                    score += 30;
                else if (title.Contains(" " + word, StringComparison.Ordinal))
                    score += 20;
                else if (title.Contains(word, StringComparison.Ordinal))
                    score += 12;
                else if (rest.Contains(word, StringComparison.Ordinal))
                    score += 5;
                else
                {
                    all = false;
                    break;
                }
            }
            if (all)
                found.Add(hit with { Score = score });
        }
        hits.AddRange(found.OrderByDescending(h => h.Score).Take(PerKind));
    }

    private static string[] Words(string query) =>
        Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Lower case, without accents, so "Lerato" finds "léràto" and "INV-0107" finds "inv-0107".</summary>
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
