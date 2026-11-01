namespace Webignite.Desktop.ViewModels;

/// <summary>One option in a drop-down: the words shown and the value behind them.</summary>
public sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}
