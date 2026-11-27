using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

// Old quotes and invoices brought in from a CSV file: picked here, or prepared by Claude or ChatGPT and waiting on
// this PC. What would come in is shown first; nothing is added until Bring them in, and a note offers Undo.
public partial class DocumentsViewModel
{
    // The file being looked at: its text, what it is when the rows don't say, and where it came from.
    private PendingImport? looking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImport))]
    public partial string ImportSummary { get; set; } = "";

    [ObservableProperty]
    public partial string ImportFrom { get; set; } = "";

    [ObservableProperty]
    public partial bool CanBringIn { get; set; }

    public bool HasImport => ImportSummary.Length > 0;

    /// <summary>What was left out and why: numbers here already, rows that couldn't be read.</summary>
    public ObservableCollection<string> ImportNotes { get; } = [];

    public bool HasImportNotes => ImportNotes.Count > 0;

    /// <summary>A file picked on this page: shown first, brought in only when asked.</summary>
    public void LookAtFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > PendingImport.Largest)
            {
                XeroMessage = "That file is too big to bring in at once. Split it into parts of a few thousand rows.";
                return;
            }
            Look(new PendingImport(File.ReadAllText(path), Kind, info.Name, DateTimeOffset.UtcNow), assistant: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            XeroMessage = "Couldn't read the file: " + e.Message;
        }
    }

    [RelayCommand]
    private void BringIn()
    {
        if (looking is null)
            return;
        var plan = DocumentImport.Plan(looking.Csv, looking.Kind, services.Documents.List(), DateTimeOffset.UtcNow, services.Settings.Documents.Currency);
        var ids = services.Documents.Import(plan.Documents);
        ForgetImport();
        Refresh();
        if (ids.Count == 0)
        {
            XeroMessage = "Nothing new came in: they're all here already.";
            return;
        }
        var what = ids.Count == 1 ? "1 old quote or invoice" : $"{ids.Count} old quotes and invoices";
        XeroMessage = $"Brought in {what}. Nothing was mailed, and nothing went to Xero.";
        services.OfferUndo($"Brought in {what}.", () =>
        {
            services.Documents.Unimport(ids);
            Refresh();
        });
    }

    [RelayCommand]
    private void ThrowAwayImport()
    {
        ForgetImport();
        XeroMessage = "";
    }

    /// <summary>One Claude or ChatGPT prepared, shown when the page is looked at again.</summary>
    private void ShowWaitingImport()
    {
        if (looking is not null && !looking.From.StartsWith("Claude", StringComparison.Ordinal) && !looking.From.StartsWith("ChatGPT", StringComparison.Ordinal))
            return; // a file picked here is being looked at
        if (services.Store.GetSetting<PendingImport>(PendingImport.SettingName) is { } waiting)
            Look(waiting, assistant: true);
        else if (looking is not null)
            ClearImport();
    }

    private void Look(PendingImport import, bool assistant)
    {
        looking = import;
        var plan = DocumentImport.Plan(import.Csv, import.Kind, services.Documents.List(), DateTimeOffset.UtcNow, services.Settings.Documents.Currency);
        ImportFrom = assistant ? $"{import.From} prepared these from a list" : $"From {import.From}";
        ImportSummary = DocumentImport.Summary(plan);
        CanBringIn = plan.Documents.Count > 0;
        ImportNotes.Clear();
        foreach (var note in plan.Problems.Concat(plan.Skipped).Take(30))
            ImportNotes.Add(note);
        if (plan.Problems.Count + plan.Skipped.Count > 30)
            ImportNotes.Add($"And {plan.Problems.Count + plan.Skipped.Count - 30} more.");
        OnPropertyChanged(nameof(HasImportNotes));
    }

    private void ForgetImport()
    {
        if (services.Store.GetSetting<PendingImport>(PendingImport.SettingName) is not null)
            services.Store.SetSetting<PendingImport?>(PendingImport.SettingName, null);
        ClearImport();
    }

    private void ClearImport()
    {
        looking = null;
        ImportSummary = "";
        ImportFrom = "";
        CanBringIn = false;
        ImportNotes.Clear();
        OnPropertyChanged(nameof(HasImportNotes));
    }
}
