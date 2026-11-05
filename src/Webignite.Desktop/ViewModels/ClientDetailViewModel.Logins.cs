using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Clients;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

// Site logins on a client's page: the sites Webignite looks after for them, with where to sign in and the
// username and password. Saved straight away, kept encrypted, and never shown to assistants.
public partial class ClientDetailViewModel
{
    public ObservableCollection<SiteLoginRow> Logins { get; } = [];

    public bool HasLogins => Logins.Count > 0;
    public bool NoLogins => Logins.Count == 0 && !EditingLogin;

    /// <summary>Set by the page: copies text, and when it's a password, keeps it out of clipboard history and clears it.</summary>
    public Func<string, bool, Task>? Copy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoLogins))]
    public partial bool EditingLogin { get; set; }

    [ObservableProperty] public partial string LoginFormTitle { get; set; } = "";
    [ObservableProperty] public partial string LoginSite { get; set; } = "";
    [ObservableProperty] public partial string LoginAddress { get; set; } = "";
    [ObservableProperty] public partial string LoginUser { get; set; } = "";
    [ObservableProperty] public partial string LoginPassword { get; set; } = "";
    [ObservableProperty] public partial string LoginNotes { get; set; } = "";
    [ObservableProperty] public partial bool ShowLoginPassword { get; set; }
    [ObservableProperty] public partial string LoginsMessage { get; set; } = "";

    public ObservableCollection<string> LoginProblems { get; } = [];
    public bool HasLoginProblems => LoginProblems.Count > 0;

    private SiteLogin? editingLogin;

    // A problem shown for the site name or address goes once either is typed in again.
    partial void OnLoginSiteChanged(string value) => ClearLoginProblems();
    partial void OnLoginAddressChanged(string value) => ClearLoginProblems();

    private void ClearLoginProblems()
    {
        if (LoginProblems.Count == 0)
            return;
        LoginProblems.Clear();
        OnPropertyChanged(nameof(HasLoginProblems));
    }

    private void LoadLogins()
    {
        Logins.Clear();
        if (!isNew)
            foreach (var login in services.Clients.Logins(client.Id))
                Logins.Add(new SiteLoginRow(login, this));
        OnPropertyChanged(nameof(HasLogins));
        OnPropertyChanged(nameof(NoLogins));
    }

    [RelayCommand]
    private void AddLogin() => StartLogin(null);

    internal void StartLogin(SiteLogin? login)
    {
        if (isNew && !TrySave())
            return;
        editingLogin = login;
        LoginFormTitle = login is null ? "New login" : "Change " + login.Site;
        LoginSite = login?.Site ?? "";
        LoginAddress = login?.Address ?? DefaultAddress();
        LoginUser = login?.UserName ?? "";
        LoginPassword = login?.Password ?? "";
        LoginNotes = login?.Notes ?? "";
        ShowLoginPassword = false;
        LoginProblems.Clear();
        OnPropertyChanged(nameof(HasLoginProblems));
        LoginsMessage = "";
        EditingLogin = true;
    }

    // A new login starts at their website's admin, which is the usual one, and can be changed.
    private string DefaultAddress() => Logins.Count == 0 && SiteLogins.Link(client.Website) is not null ? SiteLogins.Shown(client.Website) : "";

    [RelayCommand]
    private void SaveLogin()
    {
        var typed = new SiteLogin
        {
            Id = editingLogin?.Id ?? SiteLogin.NewId(),
            ClientId = client.Id,
            Site = LoginSite,
            Address = LoginAddress,
            UserName = LoginUser,
            Password = LoginPassword,
            Notes = LoginNotes,
        };
        var (saved, problems) = services.Clients.SaveLogin(typed);
        LoginProblems.Clear();
        foreach (var problem in problems)
            LoginProblems.Add(problem);
        OnPropertyChanged(nameof(HasLoginProblems));
        if (saved is null)
            return;
        CloseLoginForm();
        LoginsMessage = editingLogin is null ? $"Added {saved.Site}." : $"Saved {saved.Site}.";
        editingLogin = null;
        LoadLogins();
    }

    [RelayCommand]
    private void CancelLogin()
    {
        CloseLoginForm();
        editingLogin = null;
    }

    private void CloseLoginForm()
    {
        EditingLogin = false;
        LoginPassword = ""; // so the typed password isn't left in the form
        ShowLoginPassword = false;
    }

    [RelayCommand]
    private void MakeLoginPassword()
    {
        LoginPassword = Passwords.Make(new PasswordOptions());
        ShowLoginPassword = true;
    }

    internal void DeleteLogin(SiteLogin login)
    {
        if (services.Clients.DeleteLogin(login.Id) is not { } gone)
            return;
        if (editingLogin?.Id == gone.Id)
            CancelLogin();
        LoadLogins();
        LoginsMessage = "";
        services.OfferUndo($"Deleted the {gone.Site} login", () =>
        {
            services.Clients.RestoreLogin(gone);
            LoadLogins();
        });
    }

    internal async Task CopyFromLogin(string? text, bool secret, string what)
    {
        if (string.IsNullOrEmpty(text) || Copy is not { } copy)
            return;
        await copy(text, secret);
        LoginsMessage = secret
            ? $"{what} copied. It's cleared from the clipboard in {(int)SiteLogins.ClipboardLife.TotalSeconds} seconds."
            : $"{what} copied.";
    }
}

/// <summary>One site login on a client's page. The password shows as dots until Show is clicked.</summary>
public sealed partial class SiteLoginRow(SiteLogin login, ClientDetailViewModel page) : ObservableObject
{
    public SiteLogin Login { get; } = login;
    public string Site => Login.Site;
    public string AddressText => SiteLogins.Shown(Login.Address);
    public bool HasAddress => SiteLogins.Link(Login.Address) is not null;
    public string UserName => Login.UserName ?? "";
    public bool HasUserName => UserName.Length > 0;
    public bool HasPassword => !string.IsNullOrEmpty(Login.Password);
    public string Notes => Login.Notes ?? "";
    public bool HasNotes => Notes.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordText), nameof(ShowText))]
    public partial bool Revealed { get; set; }

    public string PasswordText => !HasPassword ? "" : Revealed ? Login.Password! : SiteLogins.Hidden;
    public string ShowText => Revealed ? "Hide" : "Show";

    [RelayCommand]
    private Task OpenSite() => SiteLogins.Link(Login.Address) is { } uri ? AppServices.OpenInBrowser(uri) : Task.CompletedTask;

    [RelayCommand]
    private void Reveal() => Revealed = !Revealed;

    [RelayCommand]
    private Task CopyUserName() => page.CopyFromLogin(Login.UserName, false, "Username");

    [RelayCommand]
    private Task CopyPassword() => page.CopyFromLogin(Login.Password, true, "Password");

    [RelayCommand]
    private Task CopyAddress() => page.CopyFromLogin(SiteLogins.Link(Login.Address)?.ToString(), false, "Login address");

    [RelayCommand]
    private void Edit() => page.StartLogin(Login);

    [RelayCommand]
    private void Delete() => page.DeleteLogin(Login);
}
