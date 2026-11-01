using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

// Password generator: made on this PC, shown once, never saved.
public sealed partial class ToolsViewModel
{
    [ObservableProperty] public partial double PasswordLength { get; set; } = 20;
    [ObservableProperty] public partial bool PasswordNumbers { get; set; } = true;
    [ObservableProperty] public partial bool PasswordSymbols { get; set; } = true;
    [ObservableProperty] public partial bool PasswordAvoidLookalikes { get; set; } = true;
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string PasswordStrength { get; set; } = "";
    [ObservableProperty] public partial string PasswordMessage { get; set; } = "";

    public int PasswordMin => Passwords.MinLength;
    public int PasswordMax => Passwords.MaxLength;
    public string PasswordLengthText => $"{(int)PasswordLength} characters";

    private PasswordOptions PasswordChoices => new((int)PasswordLength, PasswordNumbers, PasswordSymbols, PasswordAvoidLookalikes);

    partial void OnPasswordLengthChanged(double value)
    {
        OnPropertyChanged(nameof(PasswordLengthText));
        NewPassword();
    }

    partial void OnPasswordNumbersChanged(bool value) => NewPassword();
    partial void OnPasswordSymbolsChanged(bool value) => NewPassword();
    partial void OnPasswordAvoidLookalikesChanged(bool value) => NewPassword();

    [RelayCommand]
    private void NewPassword()
    {
        Password = Passwords.Make(PasswordChoices);
        var bits = Passwords.Bits(PasswordChoices);
        PasswordStrength = $"{Passwords.Strength(bits)}, about {bits} bits";
        PasswordMessage = "";
    }

    [RelayCommand]
    private async Task CopyPassword(Avalonia.Controls.TopLevel? top)
    {
        if (Password.Length == 0 || top?.Clipboard is not { } clipboard)
            return;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, Password);
        PasswordMessage = "Copied. Paste it where it's needed; Webignite doesn't keep it.";
    }
}
