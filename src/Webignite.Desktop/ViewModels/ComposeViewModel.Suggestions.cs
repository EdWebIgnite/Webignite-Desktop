using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

public enum AddressField { To, Cc, Bcc }

// Name and address suggestions while typing in To, Cc or Bcc.
public partial class ComposeViewModel
{
    public ObservableCollection<Contact> Suggestions { get; } = [];

    [ObservableProperty] public partial Contact? ChosenSuggestion { get; set; }
    [ObservableProperty] public partial bool HasSuggestions { get; set; }

    public AddressField SuggestingFor { get; private set; }

    private int suggestRun;

    /// <summary>Called as the person types in an address box. Older lookups that finish late are ignored.</summary>
    public async Task SuggestAsync(AddressField field, string? text)
    {
        var run = ++suggestRun;
        if (AddressBook.LastPart(text).Length == 0)
        {
            CloseSuggestions();
            return;
        }
        var book = await services.AddressBookAsync();
        if (run != suggestRun)
            return;
        var found = AddressBook.Match(book, text);
        Suggestions.Clear();
        foreach (var contact in found)
            Suggestions.Add(contact);
        SuggestingFor = field;
        ChosenSuggestion = Suggestions.FirstOrDefault();
        HasSuggestions = Suggestions.Count > 0;
    }

    public void MoveSuggestion(int by)
    {
        if (!HasSuggestions)
            return;
        var at = ChosenSuggestion is null ? -1 : Suggestions.IndexOf(ChosenSuggestion);
        ChosenSuggestion = Suggestions[Math.Clamp(at + by, 0, Suggestions.Count - 1)];
    }

    /// <summary>Puts the chosen person in the box in place of what was typed. Returns the new text.</summary>
    public string? AcceptSuggestion(Contact? contact = null)
    {
        if ((contact ?? ChosenSuggestion) is not { } chosen)
            return null;
        var text = SuggestingFor switch { AddressField.Cc => Cc, AddressField.Bcc => Bcc, _ => To };
        var completed = AddressBook.Complete(text, chosen.Display);
        suggestRun++;
        switch (SuggestingFor)
        {
            case AddressField.Cc: Cc = completed; break;
            case AddressField.Bcc: Bcc = completed; break;
            default: To = completed; break;
        }
        CloseSuggestions();
        return completed;
    }

    public void CloseSuggestions()
    {
        Suggestions.Clear();
        ChosenSuggestion = null;
        HasSuggestions = false;
    }
}
