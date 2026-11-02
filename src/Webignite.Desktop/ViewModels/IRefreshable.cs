namespace Webignite.Desktop.ViewModels;

/// <summary>A page that can read its data again, for F5.</summary>
public interface IRefreshable
{
    void Refresh();
}
