namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Stands where a page would for an entry that is several pages in one (Billing). It is never on screen: opening
/// the entry opens one of its tabs, each a page of its own. It exists so such an entry is a <see cref="Module"/>
/// like any other, and the sidebar, the dock and the menu need know nothing special about it.
/// </summary>
public sealed class TabsOnlyViewModel : ViewModelBase;
