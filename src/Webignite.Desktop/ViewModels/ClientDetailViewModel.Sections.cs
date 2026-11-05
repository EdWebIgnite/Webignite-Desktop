using System.Collections.Specialized;
using System.ComponentModel;
using Webignite.Core.Clients;

namespace Webignite.Desktop.ViewModels;

// The client page's folding rows: which are open and in what order (the same for every client), and the
// one-line summary each shows while closed.
public partial class ClientDetailViewModel
{
    public static readonly IReadOnlyList<string> SectionKeys = ["todo", "documents", "mail", "people", "checklist", "projects", "logins", "files"];

    private SectionsViewModel? sections;

    /// <summary>Made the first time the page asks, so it reads the saved layout once.</summary>
    public SectionsViewModel Sections => sections ??= MakeSections();

    private SectionsViewModel MakeSections()
    {
        var made = new SectionsViewModel(services.Settings, "client", SectionKeys, ["todo", "documents"]);
        // Each summary follows what it sums up.
        void Follow(INotifyCollectionChanged list, params string[] names) => list.CollectionChanged += (_, _) =>
        {
            foreach (var name in names)
                OnPropertyChanged(name);
        };
        Follow(Tasks, nameof(TodoSummary), nameof(TodoAttention));
        Follow(Events, nameof(TodoSummary));
        Follow(Documents, nameof(DocumentsSummary), nameof(DocumentsAttention));
        Follow(Mail, nameof(MailSummary));
        Follow(People, nameof(PeopleSummary));
        Follow(OnboardingItems, nameof(ChecklistSummary));
        Follow(Logins, nameof(LoginsSummary));
        Follow(NoteLinks, nameof(FilesSummary));
        Follow(LogEntries, nameof(FilesSummary));
        Follow(FileRows, nameof(FilesSummary));
        PropertyChanged += SummaryInputs;
        return made;
    }

    private void SummaryInputs(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Owed):
                OnPropertyChanged(nameof(DocumentsSummary));
                break;
            case nameof(TimeSummary):
                OnPropertyChanged(nameof(ProjectsSummary));
                break;
            case nameof(FolderPath):
                OnPropertyChanged(nameof(FilesSummary));
                break;
            case nameof(OnboardingProgress):
                OnPropertyChanged(nameof(ChecklistSummary));
                break;
        }
    }

    public string TodoSummary
    {
        get
        {
            var overdue = Tasks.Count(t => t.IsOverdue);
            var open = Tasks.Count switch { 0 => "Nothing to do", 1 => "1 to do", var n => $"{n} to do" };
            var parts = new List<string> { open };
            if (overdue > 0)
                parts.Add($"{overdue} overdue");
            if (Events.Count > 0)
                parts.Add(Events.Count == 1 ? "1 coming up" : $"{Events.Count} coming up");
            return string.Join(", ", parts);
        }
    }

    public bool TodoAttention => Tasks.Any(t => t.IsOverdue);

    public string DocumentsSummary =>
        HasOwed ? Owed
        : Documents.Count switch { 0 => "None yet", 1 => "1 quote or invoice", var n => $"{n} quotes and invoices" };

    public bool DocumentsAttention => Documents.Any(d => d.IsOverdue);

    public string MailSummary => Mail.Count switch
    {
        0 => "None found",
        _ => $"Latest {Mail[0].When}",
    };

    public string PeopleSummary => People.Count switch
    {
        0 => "Nobody yet",
        <= 2 => string.Join(", ", People.Select(p => p.Name)),
        _ => $"{People[0].Name}, {People[1].Name} and {People.Count - 2} more",
    };

    public string ChecklistSummary => HasOnboarding ? Onboarding.Progress(client) : "Not started";

    public string ProjectsSummary => TimeSummary.TrimEnd('.');

    public string LoginsSummary => Logins.Count switch { 0 => "None yet", 1 => "1 saved", var n => $"{n} saved" };

    public string FilesSummary
    {
        get
        {
            var notes = NoteLinks.Count + LogEntries.Count;
            var parts = new List<string> { HasFolder ? "Folder linked" : "No folder" };
            if (FileRows.Count > 0)
                parts.Add(FileRows.Count == 1 ? "1 file" : $"{FileRows.Count} files");
            if (notes > 0)
                parts.Add(notes == 1 ? "1 note" : $"{notes} notes");
            return string.Join(", ", parts);
        }
    }
}
