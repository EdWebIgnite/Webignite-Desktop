using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Finance;
using Webignite.Core.Retainers;

namespace Webignite.Desktop.ViewModels;

/// <summary>The Retainers module: clients billed the same every month, with the invoice drafted for you.</summary>
public partial class RetainersViewModel : ViewModelBase
{
    private readonly AppServices services;

    public RetainersViewModel(AppServices services)
    {
        this.services = services;
        Refresh();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public ObservableCollection<RetainerRowViewModel> Rows { get; } = [];

    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListing))]
    public partial RetainerEditorViewModel? Editor { get; set; }

    public bool IsListing => Editor is null;
    public bool IsEmpty => Rows.Count == 0;
    public bool HasMessage => Message.Length > 0;

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    [RelayCommand]
    private void New() => Editor = new RetainerEditorViewModel(null, services, Closed);

    public void Edit(Retainer retainer) => Editor = new RetainerEditorViewModel(retainer, services, Closed);

    private void Closed(bool saved)
    {
        Editor = null;
        var drafted = saved ? services.Store.DraftRetainerInvoices(services.DocumentSettings, Today) : 0;
        Message = drafted switch
        {
            0 => "",
            1 => "Drafted 1 invoice. It is waiting under Invoices for you to check.",
            _ => $"Drafted {drafted} invoices. They are waiting under Invoices for you to check.",
        };
        Refresh();
    }

    public void SetPaused(Retainer retainer, bool paused)
    {
        services.Store.SaveRetainer(RetainerSchedule.SetPaused(retainer, paused, Today));
        Refresh();
    }

    public void Stop(Retainer retainer)
    {
        services.Store.DeleteRetainer(retainer.Id);
        Refresh();
    }

    public void Refresh()
    {
        var retainers = services.Store.ListRetainers();
        var runs = services.Store.RetainerRuns();
        var drafted = runs.Keys.ToHashSet();
        Rows.Clear();
        foreach (var retainer in retainers)
            Rows.Add(new RetainerRowViewModel(retainer, RetainerSchedule.NextDate(retainer, drafted, Today), this));
        var running = retainers.Count(r => !r.Paused);
        Summary = retainers.Count == 0
            ? "Set up a client who pays the same every month, and the invoice is drafted on its day for you to check and send."
            : $"{Money.Rand(RetainerSchedule.MonthlyTotal(retainers, Today))} a month from {running} running retainer{(running == 1 ? "" : "s")}. Invoices are drafted on their day and wait under Invoices.";
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public partial class RetainerRowViewModel(Retainer retainer, DateOnly? next, RetainersViewModel owner) : ViewModelBase
{
    public string Client => retainer.ClientName;
    public string Title => retainer.Title;
    public string Amount => Money.Rand(retainer.MonthlyAmount);
    public bool IsPaused => retainer.Paused;
    public string Schedule => retainer.Paused
        ? "Paused"
        : next is { } date
            ? $"On the {MoneyViewModel.Ordinal(retainer.DayOfMonth)} of the month  ·  next draft {date:d MMM yyyy}"
            : "Ended";
    public string PauseLabel => retainer.Paused ? "Resume" : "Pause";

    [ObservableProperty] public partial bool ConfirmingStop { get; set; }

    [RelayCommand]
    private void Edit() => owner.Edit(retainer);

    [RelayCommand]
    private void TogglePause() => owner.SetPaused(retainer, !retainer.Paused);

    [RelayCommand]
    private void AskStop() => ConfirmingStop = true;

    [RelayCommand]
    private void CancelStop() => ConfirmingStop = false;

    [RelayCommand]
    private void Stop() => owner.Stop(retainer);
}

public sealed record StartChoice(string Label, int MonthsFromNow)
{
    public override string ToString() => Label;
}

/// <summary>Sets up or changes a retainer. Changes apply to invoices drafted from now on.</summary>
public partial class RetainerEditorViewModel : ViewModelBase
{
    private readonly AppServices services;
    private readonly Action<bool> close;
    private readonly Retainer? existing;

    public RetainerEditorViewModel(Retainer? existing, AppServices services, Action<bool> close)
    {
        this.existing = existing;
        this.services = services;
        this.close = close;
        ClientNames = services.Store.ListClients().Select(c => c.Name).ToList();
        StartChoices = [new("This month", 0), new("Next month", 1)];
        Start = StartChoices[0];
        if (existing is not null)
        {
            ClientName = existing.ClientName;
            RetainerTitle = existing.Title;
            Day = existing.DayOfMonth.ToString();
            foreach (var line in existing.Lines)
                Lines.Add(LineRowViewModel.From(line, Recalculate));
        }
        if (Lines.Count == 0)
            Lines.Add(new LineRowViewModel(Recalculate) { Description = "Hosting, " + RetainerSchedule.MonthPlaceholder });
        Recalculate();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public string Heading => existing is null ? "New retainer" : "Change retainer";
    public bool IsNew => existing is null;
    public IReadOnlyList<string> ClientNames { get; }
    public IReadOnlyList<StartChoice> StartChoices { get; }
    public ObservableCollection<LineRowViewModel> Lines { get; } = [];
    public ObservableCollection<string> Problems { get; } = [];

    [ObservableProperty] public partial string ClientName { get; set; } = "";
    [ObservableProperty] public partial string RetainerTitle { get; set; } = "";
    [ObservableProperty] public partial string Day { get; set; } = "1";
    [ObservableProperty] public partial StartChoice Start { get; set; }
    [ObservableProperty] public partial string Total { get; set; } = "";
    [ObservableProperty] public partial string Preview { get; set; } = "";

    public bool HasProblems => Problems.Count > 0;

    partial void OnClientNameChanged(string value) => Recalculate();
    partial void OnRetainerTitleChanged(string value) => Recalculate();
    partial void OnDayChanged(string value) => Recalculate();
    partial void OnStartChanged(StartChoice value) => Recalculate();

    [RelayCommand]
    private void AddLine()
    {
        Lines.Add(new LineRowViewModel(Recalculate));
        Recalculate();
    }

    [RelayCommand]
    private void RemoveLine(LineRowViewModel row)
    {
        Lines.Remove(row);
        if (Lines.Count == 0)
            Lines.Add(new LineRowViewModel(Recalculate));
        Recalculate();
    }

    private Retainer Build() => (existing ?? new Retainer
    {
        Id = Retainer.NewId(),
        ClientName = "",
        Title = "",
        Lines = [],
        Starts = Ledger.MonthOf(Today).AddMonths(Start?.MonthsFromNow ?? 0),
        Created = DateTimeOffset.UtcNow,
    }) with
    {
        ClientName = ClientName.Trim(),
        Title = RetainerTitle.Trim(),
        DayOfMonth = int.TryParse(Day.Trim(), out var day) ? day : 0,
        Lines = Lines.Select(row => row.ToLine()).OfType<Line>().ToList(),
    };

    private void Recalculate()
    {
        if (StartChoices is null)
            return;
        var retainer = Build();
        Total = $"{Money.Rand(retainer.MonthlyAmount)} a month";
        var next = RetainerSchedule.NextDate(retainer, services.Store.RetainerRuns().Keys.ToHashSet(), Today);
        Preview = next is { } date && RetainerSchedule.Check(retainer).Count == 0
            ? $"Next draft dated {date:d MMM yyyy}, reference \"{RetainerSchedule.InvoiceFor(retainer, date, services.DocumentSettings).Reference}\"."
              + (date <= Today ? " That day has passed, so it is drafted as soon as you save." : "")
            : "";
    }

    [RelayCommand]
    private void Save()
    {
        var retainer = Build();
        Problems.Clear();
        foreach (var problem in RetainerSchedule.Check(retainer))
            Problems.Add(problem);
        OnPropertyChanged(nameof(HasProblems));
        if (Problems.Count > 0)
            return;
        services.Store.SaveRetainer(retainer);
        close(true);
    }

    [RelayCommand]
    private void Cancel() => close(false);
}
