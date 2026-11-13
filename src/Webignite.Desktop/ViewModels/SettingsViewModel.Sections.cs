using CommunityToolkit.Mvvm.ComponentModel;

namespace Webignite.Desktop.ViewModels;

/// <summary>The side menu on the Settings page: one section showing at a time.</summary>
public partial class SettingsViewModel
{
    public sealed record Section(string Key, string Title);

    public static IReadOnlyList<Section> Sections { get; } =
    [
        new("xero", "Xero"),
        new("backups", "Backups"),
        new("look", "Look and feel"),
        new("mail", "Mail"),
        new("files", "Files"),
        new("screenshots", "Screenshots"),
        new("notes", "Notes"),
        new("shortcuts", "Shortcuts"),
        new("organisation", "Your organisation"),
        new("account", "Account and sync"),
        new("assistants", "Claude and ChatGPT"),
        new("emails", "Email wording"),
        new("documents", "Quotes and invoices"),
        new("help", "If something goes wrong"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowXero), nameof(ShowBackups), nameof(ShowLook), nameof(ShowMail), nameof(ShowFiles), nameof(ShowScreenshots), nameof(ShowNotes), nameof(ShowShortcuts), nameof(ShowOrganisation), nameof(ShowAccount),
        nameof(ShowAssistants), nameof(ShowEmails), nameof(ShowDocuments), nameof(ShowHelp))]
    public partial Section Current { get; set; } = Sections[0];

    /// <summary>Opens a section by its key, e.g. from a "Connect Xero in Settings first" note.</summary>
    public void Open(string key) => Current = Sections.FirstOrDefault(s => s.Key == key) ?? Sections[0];

    public bool ShowXero => Current.Key == "xero";
    public bool ShowBackups => Current.Key == "backups";
    public bool ShowLook => Current.Key == "look";
    public bool ShowMail => Current.Key == "mail";
    public bool ShowFiles => Current.Key == "files";
    public bool ShowScreenshots => Current.Key == "screenshots";
    public bool ShowNotes => Current.Key == "notes";
    public bool ShowShortcuts => Current.Key == "shortcuts";

    /// <summary>Every keyboard shortcut with a search box, as F1 shows it. Filled only while its section is open.</summary>
    public ShortcutsPart Shortcuts => field ??= new ShortcutsPart(
        () => MainViewModel.AllShortcuts(services.Settings.Screenshots, services.Settings.Notes.QuickShortcut));

    partial void OnCurrentChanged(Section value)
    {
        if (value.Key == "shortcuts")
            Shortcuts.Open();
        else
            Shortcuts.Close();
    }

    /// <summary>From the Shortcuts section: to where the keys that work from any program are picked.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenSection(string key) => Open(key);
    public bool ShowOrganisation => Current.Key == "organisation";
    public bool ShowAccount => Current.Key == "account";
    public bool ShowAssistants => Current.Key == "assistants";
    public bool ShowEmails => Current.Key == "emails";
    public bool ShowDocuments => Current.Key == "documents";
    public bool ShowHelp => Current.Key == "help";
}
