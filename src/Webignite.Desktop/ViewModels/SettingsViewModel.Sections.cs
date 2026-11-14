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
        new("reminders", "Reminders"),
        new("shortcuts", "Shortcuts"),
        new("organisation", "Your organisation"),
        new("account", "Account and sync"),
        new("assistants", "Claude and ChatGPT"),
        new("emails", "Email wording"),
        new("documents", "Quotes and invoices"),
        new("help", "If something goes wrong"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowXero), nameof(ShowBackups), nameof(ShowLook), nameof(ShowMail), nameof(ShowFiles), nameof(ShowScreenshots), nameof(ShowNotes), nameof(ShowShortcuts), nameof(ShowReminders), nameof(ShowOrganisation), nameof(ShowAccount),
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
    public bool ShowReminders => Current.Key == "reminders";

    /// <summary>Whether tasks and events with a time are announced a little before.</summary>
    public bool RemindersOn
    {
        get => services.Settings.Reminders.On;
        set
        {
            services.Settings.Reminders = services.Settings.Reminders with { On = value };
            OnPropertyChanged();
        }
    }

    public sealed record LeadChoice(int Minutes, string Label)
    {
        public override string ToString() => Label;
    }

    public static IReadOnlyList<LeadChoice> ReminderLeads { get; } =
        [.. Webignite.Core.Calendar.Reminders.Leads.Select(m => new LeadChoice(m, Webignite.Core.Calendar.Reminders.LeadLabel(m)))];

    /// <summary>How long before the time a reminder shows.</summary>
    public LeadChoice ReminderLead
    {
        get => ReminderLeads.FirstOrDefault(l => l.Minutes == services.Settings.Reminders.MinutesBefore) ?? ReminderLeads[2];
        set
        {
            if (value is null)
                return;
            services.Settings.Reminders = services.Settings.Reminders with { MinutesBefore = value.Minutes };
            OnPropertyChanged();
        }
    }

    /// <summary>Whether reminders and the notes that matter while away also show as Windows notifications when Webignite is behind other windows.</summary>
    public bool WindowsNotes
    {
        get => services.Settings.Reminders.Windows;
        set
        {
            services.Settings.Reminders = services.Settings.Reminders with { Windows = value };
            OnPropertyChanged();
        }
    }

    /// <summary>Shows a notification now, the way a reminder would, so the person can see what to expect.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void TryNotification()
    {
        const string title = "This is a reminder", text = "This is how a reminder from Webignite looks.";
        services.ShowNote?.Invoke(title, text, null);
        if (WindowsNotes)
            services.ShowWindowsNote?.Invoke(title, text, null);
    }

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
