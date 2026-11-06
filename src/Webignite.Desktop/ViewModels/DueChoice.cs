namespace Webignite.Desktop.ViewModels;

/// <summary>A day to plan something for, as a list offers it: "Today", "Fri 7", worked out from today.</summary>
public sealed record DueChoice(string Label, Func<DateOnly, DateOnly?> Pick)
{
    public override string ToString() => Label;
}
