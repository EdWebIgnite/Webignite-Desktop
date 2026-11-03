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
        new("assistants", "Claude and ChatGPT"),
        new("emails", "Email wording"),
        new("documents", "Quotes and invoices"),
        new("help", "If something goes wrong"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowXero), nameof(ShowBackups), nameof(ShowLook), nameof(ShowMail),
        nameof(ShowAssistants), nameof(ShowEmails), nameof(ShowDocuments), nameof(ShowHelp))]
    public partial Section Current { get; set; } = Sections[0];

    /// <summary>Opens a section by its key, e.g. from a "Connect Xero in Settings first" note.</summary>
    public void Open(string key) => Current = Sections.FirstOrDefault(s => s.Key == key) ?? Sections[0];

    public bool ShowXero => Current.Key == "xero";
    public bool ShowBackups => Current.Key == "backups";
    public bool ShowLook => Current.Key == "look";
    public bool ShowMail => Current.Key == "mail";
    public bool ShowAssistants => Current.Key == "assistants";
    public bool ShowEmails => Current.Key == "emails";
    public bool ShowDocuments => Current.Key == "documents";
    public bool ShowHelp => Current.Key == "help";
}
