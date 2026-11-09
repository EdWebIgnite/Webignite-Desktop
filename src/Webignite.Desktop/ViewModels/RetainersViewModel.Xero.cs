using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Retainers;
using Webignite.Xero;

namespace Webignite.Desktop.ViewModels;

/// <summary>Xero's repeating invoices, each one able to become a retainer here.</summary>
public partial class RetainersViewModel
{
    public ObservableCollection<RepeatingRowViewModel> Repeating { get; } = [];

    [ObservableProperty] public partial bool ShowingRepeating { get; set; }
    [ObservableProperty] public partial bool ReadingRepeating { get; set; }
    [ObservableProperty] public partial string RepeatingNote { get; set; } = "";

    [RelayCommand]
    private async Task ShowXeroRepeating()
    {
        if (ReadingRepeating)
            return;
        ShowingRepeating = true;
        if (services.XeroApi is not { } api)
        {
            RepeatingNote = "Connect Xero in Settings first.";
            return;
        }
        ReadingRepeating = true;
        RepeatingNote = "Reading Xero…";
        try
        {
            var found = await services.Work.Run("Reading repeating invoices from Xero", () => api.RepeatingInvoicesAsync());
            ShowRepeating(found.Where(r => r.Status != "DELETED").ToList());
        }
        catch (Exception e) when (e is XeroException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            RepeatingNote = "Couldn't read Xero: " + e.Message;
        }
        finally
        {
            ReadingRepeating = false;
        }
    }

    /// <summary>Lists the repeating invoices; also used by the screenshots.</summary>
    public void ShowRepeating(IReadOnlyList<XeroRepeatingInvoice> found)
    {
        ShowingRepeating = true;
        var retainers = services.Store.ListRetainers();
        Repeating.Clear();
        foreach (var r in found)
            Repeating.Add(new RepeatingRowViewModel(r, XeroRepeating.WhyNot(r, retainers), this));
        RepeatingNote = found.Count == 0 ? "Xero has no repeating invoices."
            : "Each one is made paused. Stop the repeating invoice in Xero, then Resume the retainer here, so nobody is billed twice.";
    }

    [RelayCommand]
    private void HideXeroRepeating() => ShowingRepeating = false;

    internal void MakeRetainer(XeroRepeatingInvoice repeating)
    {
        var retainers = services.Store.ListRetainers();
        if (XeroRepeating.WhyNot(repeating, retainers) is not null)
            return;
        var retainer = services.Store.SaveRetainer(XeroRepeating.ToRetainer(repeating, Today, DateTimeOffset.UtcNow));
        Message = XeroRepeating.Made(retainer);
        Refresh();
        ShowRepeating(Repeating.Select(r => r.Source).ToList());
    }
}

/// <summary>One repeating invoice from Xero.</summary>
public sealed partial class RepeatingRowViewModel(XeroRepeatingInvoice source, string? whyNot, RetainersViewModel page) : ViewModelBase
{
    public XeroRepeatingInvoice Source => source;
    public string Client => source.ContactName;
    public string Title => source.Reference ?? source.Lines.FirstOrDefault(l => l.IsPriced)?.Description.Split('\n')[0] ?? "";
    public string Schedule => XeroRepeating.Schedule(source);
    public string Amount => Money.Rand(source.Total);
    public bool CanMake => whyNot is null;
    public string WhyNot => whyNot ?? "";

    [RelayCommand]
    private void Make() => page.MakeRetainer(source);
}
