using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Tools;

namespace Webignite.Desktop.ViewModels;

// Password generator: words (RedVelvetLantern47$) or random characters, made on this PC, shown once, never saved.
public sealed partial class PasswordsToolViewModel : ViewModelBase
{
    public PasswordsToolViewModel()
    {
        NewPassword();
    }

    /// <summary>Words, easier to type and remember, unless random characters are picked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRandom))]
    public partial bool UseWords { get; set; } = true;

    public bool IsRandom => !UseWords;

    [ObservableProperty] public partial double WordCount { get; set; } = 3;
    public int WordMin => Passphrases.MinWords;
    public int WordMax => Passphrases.MaxWords;
    public string WordCountText => $"{(int)WordCount} words";

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

    private PassphraseOptions WordChoices => new((int)WordCount, PasswordNumbers, PasswordSymbols);

    partial void OnUseWordsChanged(bool value) => NewPassword();

    partial void OnWordCountChanged(double value)
    {
        OnPropertyChanged(nameof(WordCountText));
        NewPassword();
    }

    partial void OnPasswordLengthChanged(double value)
    {
        OnPropertyChanged(nameof(PasswordLengthText));
        NewPassword();
    }

    partial void OnPasswordNumbersChanged(bool value) => NewPassword();
    partial void OnPasswordSymbolsChanged(bool value) => NewPassword();
    partial void OnPasswordAvoidLookalikesChanged(bool value) => NewPassword();

    [RelayCommand]
    private void UseRandom() => UseWords = false;

    [RelayCommand]
    private void NewPassword()
    {
        if (UseWords)
        {
            Password = Passphrases.Make(WordChoices);
            var wordBits = Passphrases.Bits(WordChoices);
            PasswordStrength = $"{Passphrases.Strength(wordBits)}, about {wordBits} bits";
        }
        else
        {
            Password = Passwords.Make(PasswordChoices);
            var bits = Passwords.Bits(PasswordChoices);
            PasswordStrength = $"{Passwords.Strength(bits)}, about {bits} bits";
        }
        PasswordMessage = "";
    }

    [RelayCommand]
    private async Task CopyPassword(Avalonia.Controls.TopLevel? top)
    {
        if (Password.Length == 0 || top?.Clipboard is not { } clipboard)
            return;
        // Kept out of Windows' clipboard history, the cloud clipboard and Webignite's own, and cleared after a while.
        await SecretClipboard.CopyAsync(clipboard, Password);
        PasswordMessage = "Copied. Paste it where it's needed; Webignite doesn't keep it, and it's cleared from the clipboard shortly.";
    }
}
