using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

/// <summary>Small everyday tools in one place: calculator, passwords, QR codes and colours.</summary>
public sealed partial class ToolsViewModel : ViewModelBase
{
    private readonly AppServices services;

    public ToolsViewModel(AppServices services)
    {
        this.services = services;
        QrKindChoice = QrKinds[0];
        WifiKindChoice = WifiKinds[0];
        ImageWidth = ImageWidths[1];
        ImageFormatChoice = ImageFormats[0];
        ImageResults.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasImageResults));
        NewPassword();
        OnColourInputChanged(ColourInput);
    }

    // Calculator

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnswer))]
    public partial decimal? Answer { get; set; }

    [ObservableProperty] public partial string Sum { get; set; } = "";
    [ObservableProperty] public partial string AnswerText { get; set; } = "";
    [ObservableProperty] public partial string CalcMessage { get; set; } = "";
    [ObservableProperty] public partial bool CalcProblem { get; set; }
    [ObservableProperty] public partial string LineDescription { get; set; } = "";

    public bool HasAnswer => Answer is not null;

    /// <summary>Earlier sums, newest first, so a figure can be picked up again.</summary>
    public ObservableCollection<CalcTapeRow> Tape { get; } = [];

    // The answer shows as you type; nothing is added to the tape until Enter.
    partial void OnSumChanged(string value)
    {
        var result = Calculator.Evaluate(value);
        Answer = result.Value;
        AnswerText = result.Value is { } v ? "= " + Money.Rand(v) : "";
        CalcMessage = "";
        CalcProblem = false;
    }

    [RelayCommand]
    private void WorkOut()
    {
        var result = Calculator.Evaluate(Sum);
        if (!result.Ok)
        {
            CalcMessage = result.Problem!;
            CalcProblem = true;
            return;
        }
        Tape.Insert(0, new CalcTapeRow(Sum.Trim(), result.Value!.Value));
        while (Tape.Count > 20)
            Tape.RemoveAt(Tape.Count - 1);
        // Carry on from the answer, like a desk calculator.
        Sum = Calculator.Plain(result.Value.Value);
    }

    [RelayCommand]
    private void UseTape(CalcTapeRow row) => Sum = Calculator.Plain(row.Answer);

    [RelayCommand]
    private void ClearSum() => Sum = "";

    [RelayCommand]
    private async Task CopyAnswer(Avalonia.Controls.TopLevel? top)
    {
        if (Answer is not { } answer || top?.Clipboard is not { } clipboard)
            return;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, Calculator.Plain(Money.Cents(answer)));
        CalcMessage = "Copied.";
        CalcProblem = false;
    }

    [RelayCommand]
    private void AddToInvoice()
    {
        if (Answer is not { } answer)
            return;
        var problem = services.AddToInvoice?.Invoke(answer, LineDescription.Trim());
        CalcMessage = problem ?? "";
        CalcProblem = problem is not null;
        if (problem is null)
            LineDescription = "";
    }
}

public sealed record CalcTapeRow(string Sum, decimal Answer)
{
    public string AnswerText => Money.Rand(Answer);
}
