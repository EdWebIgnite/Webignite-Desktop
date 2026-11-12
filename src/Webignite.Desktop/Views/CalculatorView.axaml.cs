using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// A desk calculator: click the keys, or type, the number pad included. Enter or = works it out and carries on
/// from the answer: a number next starts afresh, an operator goes on from it. Esc clears.
/// </summary>
public partial class CalculatorView : UserControl
{
    public CalculatorView()
    {
        InitializeComponent();
        foreach (var key in Pad.Children.OfType<Button>())
            key.Click += KeyClicked;
        SumBox.AddHandler(KeyDownEvent, Keys, RoutingStrategies.Tunnel);
        SumBox.AddHandler(TextInputEvent, Typed, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => SumBox.Focus());
    }

    private CalculatorToolViewModel? Tools => DataContext as CalculatorToolViewModel;

    private void KeyClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string key)
            return;
        switch (key)
        {
            case "=": WorkOut(); break;
            case "clear": Tools?.ClearSumCommand.Execute(null); break;
            case "back": RubOut(); break;
            default: Insert(key); break;
        }
        SumBox.Focus();
    }

    private void Keys(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                WorkOut();
                break;
            case Key.Escape:
                Tools?.ClearSumCommand.Execute(null);
                break;
            // The number pad's point gives a comma on PCs set to a comma decimal; here it is always the point.
            case Key.Decimal:
                Insert(".");
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // After an answer the whole sum is picked, so a number replaces it; an operator carries on from it instead.
    private void Typed(object? sender, TextInputEventArgs e)
    {
        if (e.Text is [var c] && IsOperator(c) && AllPicked())
            SumBox.CaretIndex = SumBox.Text?.Length ?? 0;
    }

    private void Insert(string text)
    {
        if (text is [var c] && IsOperator(c) && AllPicked())
            SumBox.CaretIndex = SumBox.Text?.Length ?? 0;
        SumBox.SelectedText = text;
    }

    private void RubOut()
    {
        var sum = SumBox.Text ?? "";
        if (SumBox.SelectionStart != SumBox.SelectionEnd)
        {
            SumBox.SelectedText = "";
            return;
        }
        var at = SumBox.CaretIndex;
        if (at == 0)
            return;
        SumBox.Text = sum.Remove(at - 1, 1);
        SumBox.CaretIndex = at - 1;
    }

    private void WorkOut()
    {
        if (Tools is not { } tools)
            return;
        tools.WorkOutCommand.Execute(null);
        Dispatcher.UIThread.Post(() =>
        {
            SumBox.Focus();
            SumBox.SelectAll();
        });
    }

    private bool AllPicked()
    {
        var length = SumBox.Text?.Length ?? 0;
        return length > 0 && Math.Min(SumBox.SelectionStart, SumBox.SelectionEnd) == 0 && Math.Max(SumBox.SelectionStart, SumBox.SelectionEnd) == length;
    }

    private static bool IsOperator(char c) => c is '+' or '-' or '*' or '/' or 'x' or '×' or '÷' or '%';
}
