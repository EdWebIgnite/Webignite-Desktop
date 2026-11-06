using Avalonia.Controls;

namespace Webignite.Desktop.Controls;

/// <summary>
/// A drop-down menu whose items are made each time it opens (folders to move to, saved replies, widgets to add).
/// <see cref="Filling"/> runs before the menu is shown, so it opens measured with its items in. A plain
/// MenuFlyout filled in its Opening event opened as a thin empty line on Windows, before its items arrived.
/// </summary>
public class FreshMenu : MenuFlyout
{
    /// <summary>Fill <see cref="MenuFlyout.Items"/> here; the sender is the menu.</summary>
    public event EventHandler? Filling;

    protected override bool ShowAtCore(Control placementTarget, bool showAtPointer = false)
    {
        Filling?.Invoke(this, EventArgs.Empty);
        return Items.Count > 0 && base.ShowAtCore(placementTarget, showAtPointer);
    }
}
