using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Writing;

namespace Webignite.Desktop.ViewModels;

// Settings, Mail, Spelling: on or off, and which of Windows' languages to check in, several at once. British
// English, South African English and Afrikaans are the usual three; the first ticked gives suggestions first.
public partial class SettingsViewModel
{
    [ObservableProperty] public partial bool SpellCheckOn { get; set; }

    /// <summary>A tick box per language: the usual three, then any other Windows has on this PC.</summary>
    public ObservableCollection<SpellLanguageRow> SpellLanguages { get; } = [];

    public string SpellNote { get; private set; } = "";

    /// <summary>A language wanted isn't on the PC: show the way to add it in Windows.</summary>
    public bool SpellLanguageMissing { get; private set; }

    private bool loadingSpelling;

    private void LoadSpelling()
    {
        loadingSpelling = true;
        var settings = services.Settings.SpellCheck;
        SpellCheckOn = settings.On;
        var available = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                available = [.. WindowsSpelling.Languages()];
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidCastException)
            {
                Webignite.Data.AppLog.Write("Reading Windows' spelling languages", e);
            }
        }
        var wanted = Spelling.Wanted(settings);
        var tags = wanted.Concat(Spelling.Usual).Concat(available.Order(StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        SpellLanguages.Clear();
        foreach (var tag in tags)
        {
            var onPc = available.Any(a => string.Equals(a, tag, StringComparison.OrdinalIgnoreCase));
            SpellLanguages.Add(new SpellLanguageRow(tag, Name(tag), onPc, wanted.Contains(tag, StringComparer.OrdinalIgnoreCase), this));
        }
        var missing = Spelling.Missing(wanted, available);
        SpellLanguageMissing = OperatingSystem.IsWindows() && missing.Count > 0;
        SpellNote = !OperatingSystem.IsWindows() ? "Spell check uses Windows' own checker, so it works on Windows only."
            : available.Count == 0 ? "Windows has no spelling languages on this PC. Add one under Windows Settings, Time and language, Language and region."
            : missing.Count > 0
                ? $"{string.Join(" and ", missing.Select(m => ShortName(m)))} isn't on this PC yet, so it can't be checked. Add it in Windows (Time and language, Language and region, Add a language) and it's checked here too."
                : "A word is marked only when none of the ticked languages knows it. Words you add are kept by Windows, for Word and Edge too.";
        OnPropertyChanged(nameof(SpellNote));
        OnPropertyChanged(nameof(SpellLanguageMissing));
        loadingSpelling = false;
    }

    partial void OnSpellCheckOnChanged(bool value) => SaveSpelling();

    internal void SaveSpelling()
    {
        if (loadingSpelling)
            return;
        var languages = SpellLanguages.Where(l => l.IsOn).Select(l => l.Tag).ToList();
        services.Settings.SpellCheck = new SpellCheckSettings { On = SpellCheckOn, Languages = languages.Count > 0 ? languages : null };
        services.ResetSpelling();
    }

    /// <summary>Opens Windows' language settings, to add Afrikaans or another language to check in.</summary>
    [RelayCommand]
    private void OpenWindowsLanguages()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:regionlanguage") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Webignite.Data.AppLog.Write("Opening Windows' language settings", e);
        }
    }

    private static string Name(string tag)
    {
        try
        {
            return $"{CultureInfo.GetCultureInfo(tag).DisplayName} ({tag})";
        }
        catch (CultureNotFoundException)
        {
            return tag;
        }
    }

    private static string ShortName(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return tag;
        }
    }
}

/// <summary>A language to check spelling in: ticked or not, and whether Windows has it on this PC.</summary>
public sealed partial class SpellLanguageRow : ObservableObject
{
    private readonly SettingsViewModel owner;

    public SpellLanguageRow(string tag, string name, bool onPc, bool on, SettingsViewModel owner)
    {
        this.owner = owner;
        Tag = tag;
        Name = onPc ? name : $"{name}, not on this PC";
        IsOnPc = onPc;
        IsOn = on;
    }

    public string Tag { get; }
    public string Name { get; }
    public bool IsOnPc { get; }

    [ObservableProperty] public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value) => owner.SaveSpelling();
}
