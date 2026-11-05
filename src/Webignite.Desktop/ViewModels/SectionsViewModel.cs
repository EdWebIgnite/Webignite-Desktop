using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// A page's folding rows: which are open and in what order. Saved under the page's name as soon as it
/// changes, so every client opens the same way.
/// </summary>
public sealed partial class SectionsViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private readonly string page;

    public SectionsViewModel(AppSettings settings, string page, IReadOnlyList<string> keys, IReadOnlyList<string> openAtFirst)
    {
        this.settings = settings;
        this.page = page;
        Layout = Sections.Reconcile(settings.SectionLayout(page), keys, openAtFirst);
    }

    [ObservableProperty] public partial SectionLayout Layout { get; private set; }

    public bool IsOpen(string key) => Layout.IsOpen(key);

    public void Toggle(string key) => Change(Sections.Toggle(Layout, key));

    public void All(bool open) => Change(Sections.All(Layout, open));

    public void MoveTo(string key, int index) => Change(Sections.MoveTo(Layout, key, index));

    public void MoveBy(string key, int by) => Change(Sections.MoveBy(Layout, key, by));

    private void Change(SectionLayout layout)
    {
        if (layout.Order.SequenceEqual(Layout.Order) && layout.Open.SequenceEqual(Layout.Open))
            return;
        Layout = layout;
        settings.SetSectionLayout(page, layout);
    }
}
