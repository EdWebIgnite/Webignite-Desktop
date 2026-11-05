using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using Webignite.Core.Writing;

namespace Webignite.Desktop;

/// <summary>
/// Windows' own spell checker (the one Word and Edge use), so words added in one are known in the others.
/// Made the first time a mail is written, never at start-up. Answers are kept, so each word is asked once.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSpelling : ISpelling, IDisposable
{
    private const int MaxRemembered = 5000;
    private ISpellChecker? checker;
    private readonly Dictionary<string, bool> known = new(StringComparer.Ordinal);

    private WindowsSpelling(ISpellChecker checker, string language)
    {
        this.checker = checker;
        Language = language;
    }

    public string Language { get; }

    public int Revision { get; private set; }

    /// <summary>The languages Windows can check on this PC.</summary>
    public static IReadOnlyList<string> Languages()
    {
        var factory = Factory();
        try
        {
            return Read(factory.get_SupportedLanguages());
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>A checker in the language wanted, or the closest the PC has. Null when Windows has none.</summary>
    public static WindowsSpelling? Open(string? wanted)
    {
        var factory = Factory();
        try
        {
            if (Spelling.Pick(wanted, Read(factory.get_SupportedLanguages())) is not { } language)
                return null;
            return new WindowsSpelling(factory.CreateSpellChecker(language), language);
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    public bool IsWrong(string word)
    {
        if (checker is null || word.Length == 0)
            return false;
        if (known.TryGetValue(word, out var wrong))
            return wrong;
        var errors = checker.Check(word);
        try
        {
            wrong = errors.Next(out var error) == 0;
            if (error is not null)
                Marshal.ReleaseComObject(error);
        }
        finally
        {
            Marshal.ReleaseComObject(errors);
        }
        if (known.Count >= MaxRemembered)
            known.Clear();
        known[word] = wrong;
        return wrong;
    }

    public IReadOnlyList<string> Suggest(string word)
    {
        if (checker is null)
            return [];
        return Read(checker.Suggest(word)).Take(6).ToList();
    }

    public void Add(string word)
    {
        checker?.Add(word);
        Forget(word);
    }

    public void Ignore(string word)
    {
        checker?.Ignore(word);
        Forget(word);
    }

    private void Forget(string word)
    {
        known.Remove(word);
        Revision++;
    }

    public void Dispose()
    {
        if (checker is not null)
            Marshal.ReleaseComObject(checker);
        checker = null;
        known.Clear();
    }

    private static ISpellCheckerFactory Factory() =>
        (ISpellCheckerFactory)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC"), throwOnError: true)!)!;

    private static List<string> Read(IEnumString list)
    {
        var found = new List<string>();
        try
        {
            var one = new string[1];
            while (list.Next(1, one, IntPtr.Zero) == 0)
                found.Add(one[0]);
        }
        finally
        {
            Marshal.ReleaseComObject(list);
        }
        return found;
    }

    // From spellcheck.h. Only the members used are called, but the order must match Windows'.

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        IEnumString get_SupportedLanguages();

        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);

        ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string get_LanguageTag();

        IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);

        IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);

        void Add([MarshalAs(UnmanagedType.LPWStr)] string word);

        void Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        /// <summary>0 (S_OK) with an error, 1 (S_FALSE) when there are no more.</summary>
        [PreserveSig]
        int Next(out ISpellingError? value);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellingError
    {
        uint get_StartIndex();

        uint get_Length();
    }
}
