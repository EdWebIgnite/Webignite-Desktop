namespace Webignite.Desktop.ViewModels;

public sealed class OverviewViewModel : ViewModelBase
{
    public string Greeting
    {
        get
        {
            var hour = DateTime.Now.Hour;
            return hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
        }
    }
}
