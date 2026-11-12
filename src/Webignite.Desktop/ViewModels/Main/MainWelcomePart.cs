using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Accounts;

namespace Webignite.Desktop.ViewModels;

// The welcome screen and the setup guide. When Webignite opens and nobody is signed in, the welcome screen asks to
// join with an invite, sign in or create an account; each starts the setup guide, a strip across the top that takes
// the person through joining, Xero and ChatGPT on the real Settings page, then round the main pages. Once signed in,
// the app remembers you (the kept sign-in is refreshed on every sync), so it doesn't ask again. Not now opens the app
// as it is.
public sealed partial class MainWelcomePart(AppServices services, MainViewModel main) : ViewModelBase
{
    /// <summary>
    /// Whether the welcome screen is used at all. The app switches it on as it starts; tests and the screenshot tool
    /// leave it off, so they open straight in.
    /// </summary>
    public static bool AskToSignInAtStart { get; set; }

    [ObservableProperty] public partial bool ShowingWelcome { get; private set; }

    /// <summary>
    /// Shows the welcome screen when it's switched on and nobody is signed in on this PC; picks the setup guide up
    /// again where it was left when the app was closed during it.
    /// </summary>
    public void Welcome()
    {
        ShowingWelcome = StartRules.AskToSignIn(AskToSignInAtStart, services.MaySync);
        if (!ShowingWelcome && AskToSignInAtStart && services.Settings.SetupGuide is { Finished: false } left)
            ShowGuide(Math.Max(0, SetupGuide.Steps.ToList().FindIndex(s => s.Key == left.At)), null);
    }

    [RelayCommand]
    private void WelcomeSignIn() => StartGuide("sign-in");

    [RelayCommand]
    private void WelcomeCreate() => StartGuide("new");

    [RelayCommand]
    private void WelcomeJoin() => StartGuide("join");

    /// <summary>Opens the app as it is, on this PC only; it asks again next time.</summary>
    [RelayCommand]
    private void WelcomeNotNow() => ShowingWelcome = false;

    // The setup guide

    private SettingsViewModel? watched;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuideStep), nameof(GuideProgress), nameof(GuideFraction), nameof(GuideCanGoBack))]
    public partial int GuideAt { get; private set; }

    [ObservableProperty] public partial bool ShowingGuide { get; private set; }

    public SetupStep GuideStep => SetupGuide.Steps[GuideAt];
    public string GuideProgress => SetupGuide.Progress(GuideAt);
    public double GuideFraction => 100.0 * (GuideAt + 1) / SetupGuide.Steps.Count;
    public bool GuideCanGoBack => GuideAt > 0;
    public bool GuideStepDone => GuideStep.Check != SetupCheck.None && IsDone(GuideStep.Check);
    public string GuideNextLabel => SetupGuide.NextLabel(GuideAt, IsDone(GuideStep.Check));

    /// <summary>Starts the guide from the beginning, or from Xero when this PC is signed in already.</summary>
    public void StartGuide(string? form = null) => ShowGuide(SetupGuide.Start(services.Account.IsSignedIn), form);

    [RelayCommand]
    private void GuideNext()
    {
        if (SetupGuide.IsLast(GuideAt))
            EndGuide();
        else
            GoToStep(SetupGuide.Next(GuideAt), null);
    }

    [RelayCommand]
    private void GuideBack() => GoToStep(SetupGuide.Back(GuideAt), null);

    /// <summary>Closes the guide; it doesn't come back on its own. Settings, Account and sync can start it again.</summary>
    [RelayCommand]
    private void EndGuide()
    {
        ShowingGuide = false;
        services.Settings.SetupGuide = new SetupProgress(SetupGuide.Steps[GuideAt].Key, Finished: true);
        if (watched is not null)
            watched.PropertyChanged -= SettingsChanged;
        watched = null;
    }

    private void ShowGuide(int at, string? form)
    {
        ShowingWelcome = false;
        ShowingGuide = true;
        GoToStep(at, form);
    }

    private void GoToStep(int at, string? form)
    {
        GuideAt = at;
        var step = GuideStep;
        services.Settings.SetupGuide = new SetupProgress(step.Key, Finished: false);
        main.Selected = main[Enum.Parse<Page>(step.Page)];
        if (step.Section is { } section)
        {
            var settings = main.PageOf<SettingsViewModel>(Page.Settings);
            if (watched != settings)
            {
                if (watched is not null)
                    watched.PropertyChanged -= SettingsChanged;
                watched = settings;
                settings.PropertyChanged += SettingsChanged;
            }
            settings.LoadShared(); // the Xero app may have arrived from the other PC since joining
            settings.Open(section);
            if (step.Check == SetupCheck.SignedIn && !services.Account.IsSignedIn)
                settings.Account.ShowAccountFormCommand.Execute(form ?? "join");
        }
        GuideStatusChanged();
    }

    // Signing in, connecting Xero or ChatGPT on the Settings page ticks the step off.
    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsAccountViewModel.IsSignedIn) or nameof(SettingsViewModel.IsConnected) or nameof(SettingsChatGptViewModel.ChatGptConnected))
            GuideStatusChanged();
    }

    private void GuideStatusChanged()
    {
        OnPropertyChanged(nameof(GuideStepDone));
        OnPropertyChanged(nameof(GuideNextLabel));
    }

    private bool IsDone(SetupCheck check) => check switch
    {
        SetupCheck.SignedIn => services.Account.IsSignedIn,
        SetupCheck.XeroConnected => services.Settings.Xero.TenantId is not null && services.Xero?.HasSavedSignIn == true,
        SetupCheck.ChatGptConnected => services.Settings.ChatGptTunnel.On,
        _ => true,
    };
}
