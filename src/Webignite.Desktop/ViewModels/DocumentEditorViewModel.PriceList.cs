using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

// The price list in the editor: add a standard package's lines, or save this document's lines as one.
public partial class DocumentEditorViewModel
{
    public IReadOnlyList<PriceEntryChoice> PriceChoices => services.PriceList.Select(e => new PriceEntryChoice(e)).ToList();
    public bool HasPriceList => services.PriceList.Count > 0;

    [ObservableProperty] public partial PriceEntryChoice? ChosenPrice { get; set; }
    [ObservableProperty] public partial bool SavingPackage { get; set; }
    [ObservableProperty] public partial string PackageName { get; set; } = "";
    [ObservableProperty] public partial string PackageMessage { get; set; } = "";
    [ObservableProperty] public partial bool PackageProblem { get; set; }

    partial void OnChosenPriceChanged(PriceEntryChoice? value)
    {
        if (value is null || !IsEditable)
            return;
        var lines = PriceList.AddTo(Lines.Select(row => row.ToLine()), value.Entry);
        Lines.Clear();
        foreach (var line in lines)
            Lines.Add(LineRowViewModel.From(line, Recalculate));
        Recalculate();
        PackageMessage = $"Added {value.Entry.Name}. Change anything for this client.";
        PackageProblem = false;
        ChosenPrice = null;
    }

    [RelayCommand]
    private void StartSavePackage()
    {
        SavingPackage = true;
        PackageName = "";
        PackageMessage = "";
    }

    [RelayCommand]
    private void CancelSavePackage() => SavingPackage = false;

    [RelayCommand]
    private void SavePackage()
    {
        try
        {
            services.PriceList = PriceList.Save(services.PriceList, PackageName, Lines.Select(row => row.ToLine()).OfType<Line>());
            SavingPackage = false;
            PackageMessage = $"Saved \"{PackageName.Trim()}\" to the price list.";
            PackageProblem = false;
            OnPropertyChanged(nameof(PriceChoices));
            OnPropertyChanged(nameof(HasPriceList));
        }
        catch (ArgumentException e)
        {
            PackageMessage = e.Message.Split(" (Parameter")[0];
            PackageProblem = true;
        }
    }
}

/// <summary>A price list entry as shown in the drop-down: name and total.</summary>
public sealed record PriceEntryChoice(PriceEntry Entry)
{
    public override string ToString() => $"{Entry.Name}  ({Money.Rand(Entry.Total)})";
}
