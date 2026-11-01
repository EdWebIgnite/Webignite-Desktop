using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Renewals;

namespace Webignite.Desktop.ViewModels;

public sealed record KindChoice(string Label, RenewalKind Kind)
{
    public override string ToString() => Label;
}

public sealed record PeriodChoice(string Label, int Months)
{
    public override string ToString() => Label;
}

/// <summary>Domains and hosting: what renews when, with reminders and renewal invoices drafted in time.</summary>
public partial class RenewalsViewModel : ViewModelBase
{
    private readonly AppServices services;
    private Renewal? editing;

    public RenewalsViewModel(AppServices services)
    {
        this.services = services;
        Kinds = Enum.GetValues<RenewalKind>().Select(k => new KindChoice(RenewalRules.KindLabel(k), k)).ToList();
        Periods = [new("Every month", 1), new("Every year", 12), new("Every 2 years", 24)];
        ClearForm();
        Refresh();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public IReadOnlyList<KindChoice> Kinds { get; }
    public IReadOnlyList<PeriodChoice> Periods { get; }
    public ObservableCollection<string> ClientNames { get; } = [];
    public ObservableCollection<RenewalRowViewModel> Soon { get; } = [];
    public ObservableCollection<RenewalRowViewModel> Later { get; } = [];

    public bool HasSoon => Soon.Count > 0;
    public bool HasLater => Later.Count > 0;
    public bool IsEmpty => Soon.Count == 0 && Later.Count == 0;

    [ObservableProperty] public partial bool ShowForm { get; set; }
    [ObservableProperty] public partial string FormTitle { get; set; } = "";
    [ObservableProperty] public partial string FormName { get; set; } = "";
    [ObservableProperty] public partial KindChoice FormKind { get; set; }
    [ObservableProperty] public partial string FormClient { get; set; } = "";
    [ObservableProperty] public partial string FormProvider { get; set; } = "";
    [ObservableProperty] public partial DateTime? FormDate { get; set; }
    [ObservableProperty] public partial PeriodChoice FormPeriod { get; set; }
    [ObservableProperty] public partial string FormCost { get; set; } = "";
    [ObservableProperty] public partial string FormPrice { get; set; } = "";
    [ObservableProperty] public partial string FormRemind { get; set; } = "30";
    [ObservableProperty] public partial string FormNotes { get; set; } = "";
    [ObservableProperty] public partial string FormMessage { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";

    public bool HasMessage => Message.Length > 0;
    public bool HasFormMessage => FormMessage.Length > 0;

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));
    partial void OnFormMessageChanged(string value) => OnPropertyChanged(nameof(HasFormMessage));

    [RelayCommand]
    private void New()
    {
        ClearForm();
        FormTitle = "Add a domain or hosting";
        ShowForm = true;
    }

    public void Edit(Renewal renewal)
    {
        editing = renewal;
        FormTitle = "Change " + renewal.Name;
        FormName = renewal.Name;
        FormKind = Kinds.First(k => k.Kind == renewal.Kind);
        FormClient = renewal.ClientName ?? "";
        FormProvider = renewal.Provider ?? "";
        FormDate = renewal.RenewsOn.ToDateTime(TimeOnly.MinValue);
        FormPeriod = Periods.FirstOrDefault(p => p.Months == renewal.PeriodMonths) ?? Periods[1];
        FormCost = renewal.Cost?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
        FormPrice = renewal.Price?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
        FormRemind = renewal.RemindDaysBefore.ToString();
        FormNotes = renewal.Notes ?? "";
        FormMessage = "";
        ShowForm = true;
    }

    [RelayCommand]
    private void CancelForm()
    {
        ClearForm();
        ShowForm = false;
    }

    [MemberNotNull(nameof(FormKind), nameof(FormPeriod))]
    private void ClearForm()
    {
        editing = null;
        FormName = FormClient = FormProvider = FormCost = FormPrice = FormNotes = FormMessage = "";
        FormKind = Kinds[0];
        FormPeriod = Periods[1];
        FormRemind = "30";
        FormDate = Today.AddYears(1).ToDateTime(TimeOnly.MinValue);
    }

    [RelayCommand]
    private void SaveForm()
    {
        var cost = Amount(FormCost, out var costOk);
        var price = Amount(FormPrice, out var priceOk);
        if (!costOk || !priceOk)
        {
            FormMessage = "Type amounts as numbers, like 99 or 250,00, or leave them empty.";
            return;
        }
        var renewal = (editing ?? new Renewal { Id = Renewal.NewId(), Name = "", RenewsOn = Today, Created = DateTimeOffset.UtcNow }) with
        {
            Name = FormName.Trim(),
            Kind = FormKind.Kind,
            ClientName = Blank(FormClient),
            Provider = Blank(FormProvider),
            RenewsOn = FormDate is { } d ? DateOnly.FromDateTime(d) : Today,
            PeriodMonths = FormPeriod.Months,
            Cost = cost,
            Price = price,
            RemindDaysBefore = int.TryParse(FormRemind.Trim(), out var days) ? days : -1,
            Notes = Blank(FormNotes),
        };
        if (RenewalRules.Check(renewal).FirstOrDefault() is { } problem)
        {
            FormMessage = problem;
            return;
        }
        services.Store.SaveRenewal(renewal);
        ClearForm();
        ShowForm = false;
        Refresh();
    }

    private static decimal? Amount(string text, out bool ok)
    {
        ok = true;
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var value = Money.Parse(text);
        ok = value is not null;
        return value;
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void MarkRenewed(Renewal renewal)
    {
        var next = services.Store.MarkRenewed(renewal, Today);
        Message = $"{renewal.Name} now renews on {next.RenewsOn:d MMM yyyy}."
                  + (renewal.Cost is > 0 ? $" {Money.Rand(renewal.Cost.Value)} was added to this month's expenses." : "");
        Refresh();
    }

    public void Remove(Renewal renewal)
    {
        services.Store.DeleteRenewal(renewal.Id);
        Refresh();
    }

    public void Refresh()
    {
        services.Store.RaiseRenewals(services.DocumentSettings, Today);
        Soon.Clear();
        Later.Clear();
        foreach (var renewal in services.Store.ListRenewals())
        {
            var row = new RenewalRowViewModel(renewal, RenewalRules.State(renewal, Today), this);
            (row.IsFine ? Later : Soon).Add(row);
        }
        ClientNames.Clear();
        foreach (var client in services.Store.ListClients())
            ClientNames.Add(client.Name);
        OnPropertyChanged(nameof(HasSoon));
        OnPropertyChanged(nameof(HasLater));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public partial class RenewalRowViewModel(Renewal renewal, RenewalState state, RenewalsViewModel owner) : ViewModelBase
{
    public string Name => renewal.Name;
    public string Detail => string.Join("  ·  ", new[] { RenewalRules.KindLabel(renewal.Kind), renewal.ClientName ?? "Webignite", renewal.Provider }
        .Where(s => !string.IsNullOrWhiteSpace(s)));
    public bool IsOverdue => state == RenewalState.Overdue;
    public bool IsFine => state == RenewalState.Fine;

    public string When
    {
        get
        {
            var days = renewal.RenewsOn.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
            var date = renewal.RenewsOn.ToString("d MMM yyyy");
            return days switch
            {
                0 => $"{date}, today",
                1 => $"{date}, tomorrow",
                -1 => $"{date}, yesterday",
                < 0 => $"{date}, {-days} days ago",
                < 60 => $"{date}, in {days} days",
                _ => date,
            };
        }
    }

    public string Amounts => string.Join("  ·  ", new[]
    {
        renewal.Cost is { } cost ? $"Costs {Money.Rand(cost)}" : null,
        renewal.Price is { } price ? $"Charge {Money.Rand(price)}" : null,
    }.Where(s => s is not null));

    [ObservableProperty] public partial bool ConfirmingRemove { get; set; }

    [RelayCommand]
    private void Renewed() => owner.MarkRenewed(renewal);

    [RelayCommand]
    private void Change() => owner.Edit(renewal);

    [RelayCommand]
    private void AskRemove() => ConfirmingRemove = true;

    [RelayCommand]
    private void CancelRemove() => ConfirmingRemove = false;

    [RelayCommand]
    private void Remove() => owner.Remove(renewal);
}
