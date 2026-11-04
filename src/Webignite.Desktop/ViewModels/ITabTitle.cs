namespace Webignite.Desktop.ViewModels;

/// <summary>
/// A page whose tab says what is open in it (a client's name, an invoice number) rather than just the page's
/// name, so two tabs of the same page can be told apart. Raise PropertyChanged for TabTitle when it changes.
/// </summary>
public interface ITabTitle
{
    string? TabTitle { get; }
}
