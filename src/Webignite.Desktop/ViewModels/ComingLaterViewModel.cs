namespace Webignite.Desktop.ViewModels;

public sealed class ComingLaterViewModel(string title, string description) : ViewModelBase
{
    public string Title { get; } = title;
    public string Description { get; } = description;
}
