using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Webignite.Desktop.ViewModels;

// The welcome screen: when Webignite opens and nobody is signed in, it asks to sign in, create an account or join
// with an invite, which all carry on in Settings, Account and sync. Once signed in, the app remembers you (the
// kept sign-in is refreshed on every sync), so it doesn't ask again. Not now opens the app as it is.
public sealed partial class MainViewModel
{
    /// <summary>
    /// Whether the welcome screen is used at all. Off while Webignite is being tested, so the app opens straight in;
    /// switched on when it goes to customers.
    /// </summary>
    public static bool AskToSignInAtStart { get; set; }

    [ObservableProperty] public partial bool ShowingWelcome { get; private set; }

    /// <summary>Shows the welcome screen when it's switched on and nobody is signed in on this PC.</summary>
    public void Welcome() => ShowingWelcome = Webignite.Core.Accounts.StartRules.AskToSignIn(AskToSignInAtStart, services.MaySync);

    [RelayCommand]
    private void WelcomeSignIn() => FromWelcome("sign-in");

    [RelayCommand]
    private void WelcomeCreate() => FromWelcome("new");

    [RelayCommand]
    private void WelcomeJoin() => FromWelcome("join");

    /// <summary>Opens the app as it is, on this PC only; it asks again next time.</summary>
    [RelayCommand]
    private void WelcomeNotNow() => ShowingWelcome = false;

    private void FromWelcome(string form)
    {
        ShowingWelcome = false;
        Selected = this[Page.Settings];
        var settings = PageOf<SettingsViewModel>(Page.Settings);
        settings.Open("account");
        settings.ShowAccountFormCommand.Execute(form);
    }
}
