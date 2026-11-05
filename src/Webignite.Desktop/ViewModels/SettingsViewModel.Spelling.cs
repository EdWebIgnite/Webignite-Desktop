using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Writing;

namespace Webignite.Desktop.ViewModels;

// Settings, Mail, Spelling: on or off, and which of Windows' languages to check in.
public partial class SettingsViewModel
{
    [ObservableProperty] public partial bool SpellCheckOn { get; set; }

    public IReadOnlyList<Choice<string?>> SpellLanguages { get; private set; } = [];

    [ObservableProperty] public partial Choice<string?>? SpellLanguage { get; private set; }

    public string SpellNote { get; private set; } = "";

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
        var usual = Spelling.Pick(null, available);
        SpellLanguages =
        [
            new Choice<string?>(usual is null ? "South African English, or the closest on this PC" : $"{Name(usual)} (usual)", null),
            .. available.Where(a => a != usual).Order(StringComparer.OrdinalIgnoreCase).Select(a => new Choice<string?>(Name(a), a)),
        ];
        SpellLanguage = SpellLanguages.FirstOrDefault(c => c.Value is not null && string.Equals(c.Value, settings.Language, StringComparison.OrdinalIgnoreCase)) ?? SpellLanguages[0];
        SpellNote = !OperatingSystem.IsWindows() ? "Spell check uses Windows' own checker, so it works on Windows only."
            : available.Count == 0 ? "Windows has no spelling languages on this PC. Add one under Windows Settings, Time and language, Language and region."
            : !available.Any(a => a.Equals("en-ZA", StringComparison.OrdinalIgnoreCase))
                ? "South African English isn't on this PC, so the closest is used. Words you add are kept by Windows, for Word and Edge too."
                : "Words you add are kept by Windows, for Word and Edge too.";
        OnPropertyChanged(nameof(SpellLanguages));
        OnPropertyChanged(nameof(SpellNote));
        loadingSpelling = false;
    }

    /// <summary>The language picked in the list; the list is one-way, so only a real pick counts.</summary>
    public void PickSpellLanguage(Choice<string?> choice)
    {
        SpellLanguage = choice;
        SaveSpelling();
    }

    partial void OnSpellCheckOnChanged(bool value) => SaveSpelling();

    private void SaveSpelling()
    {
        if (loadingSpelling)
            return;
        services.Settings.SpellCheck = new SpellCheckSettings { On = SpellCheckOn, Language = SpellLanguage?.Value };
        services.ResetSpelling();
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
}
