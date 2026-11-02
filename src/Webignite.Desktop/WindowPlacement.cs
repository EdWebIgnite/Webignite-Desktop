namespace Webignite.Desktop;

/// <summary>Where the window was and which page was open when the app last closed, so it opens the same way.</summary>
public sealed record WindowPlacement
{
    public int X { get; init; }
    public int Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public bool Maximized { get; init; }
    public string? LastPage { get; init; }
}
