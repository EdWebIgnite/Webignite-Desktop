namespace Webignite.Desktop.ViewModels;

/// <summary>A page whose panes can be shown, hidden and moved; Ctrl+Shift+1 to 4 show or hide them.</summary>
public interface IHasPanes
{
    PanelsViewModel Panels { get; }
}
