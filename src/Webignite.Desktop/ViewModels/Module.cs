namespace Webignite.Desktop.ViewModels;

/// <summary>
/// One entry in the sidebar. The app is a shell of modules, so Mail and later tools slot in as
/// another entry without touching the rest.
/// </summary>
public sealed record Module(string Title, ViewModelBase Page, bool IsLater = false);
