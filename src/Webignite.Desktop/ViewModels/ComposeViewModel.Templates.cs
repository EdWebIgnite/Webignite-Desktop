using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Core.Writing;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// Templates in the editor: a whole mail (who it's to, subject and formatted writing) kept to start others from.
/// Using one fills only what's still empty, so nothing typed is lost.
/// </summary>
public partial class ComposeViewModel
{
    public IReadOnlyList<MailTemplate> TemplateList => services.Settings.MailTemplates;

    [ObservableProperty] public partial bool AskingTemplateName { get; set; }
    [ObservableProperty] public partial string TemplateName { get; set; } = "";
    [ObservableProperty] public partial string TemplateProblem { get; set; } = "";

    public bool HasTemplateProblem => TemplateProblem.Length > 0;
    partial void OnTemplateProblemChanged(string value) => OnPropertyChanged(nameof(HasTemplateProblem));

    /// <summary>
    /// Puts a template in. To, Cc and Subject are filled when empty. A mail with nothing written yet takes the
    /// template's writing with this mailbox's signature; otherwise the writing is returned to go in at the cursor.
    /// </summary>
    public RichDocument? UseTemplate(MailTemplate template)
    {
        if (To.Trim().Length == 0)
            To = template.To.Length > 0 ? template.To.TrimEnd(',', ' ') + ", " : "";
        if (Cc.Trim().Length == 0 && template.Cc.Length > 0)
        {
            Cc = template.Cc;
            ShowCc = true;
        }
        var person = Recipients.Parse(To).People.FirstOrDefault();
        var client = SavedReplies.ClientFor(services.Clients.List(), person?.Address);
        var firstName = SavedReplies.FirstName(person, client, services.Contacts.List());
        if (Subject.Trim().Length == 0)
            Subject = MailTemplates.Subject(template, firstName, client?.Name);
        if (Document.IsBlank)
        {
            Document = MailTemplates.Writing(template, firstName, client?.Name, Signature(From));
            return null;
        }
        return MailTemplates.Writing(template, firstName, client?.Name, []);
    }

    [RelayCommand]
    private void StartSaveTemplate()
    {
        TemplateName = Subject.Trim();
        TemplateProblem = "";
        AskingTemplateName = true;
    }

    [RelayCommand]
    private void SaveTemplate()
    {
        var problems = MailTemplates.Check(TemplateName, Document, Subject, TemplateList);
        if (problems.Count > 0)
        {
            TemplateProblem = string.Join(" ", problems);
            return;
        }
        var template = MailTemplates.From(TemplateName, To, Cc, Subject, Document, MailTemplate.NewId());
        services.Settings.MailTemplates = [.. TemplateList, template];
        AskingTemplateName = false;
        OnPropertyChanged(nameof(TemplateList));
        Say($"Saved the template \"{template.Name}\". Find it under Templates. {{name}} and {{client}} in it are filled in when it's used.", false);
    }

    [RelayCommand]
    private void CancelSaveTemplate() => AskingTemplateName = false;

    public void RemoveTemplate(MailTemplate template)
    {
        var before = TemplateList;
        var at = before.ToList().FindIndex(t => t.Id == template.Id);
        services.Settings.MailTemplates = before.Where(t => t.Id != template.Id).ToList();
        OnPropertyChanged(nameof(TemplateList));
        services.OfferUndo($"Removed the template \"{template.Name}\"", () =>
        {
            services.Settings.MailTemplates = Webignite.Core.Layout.ListUndo.PutBack(services.Settings.MailTemplates, template, at, t => t.Id);
            OnPropertyChanged(nameof(TemplateList));
        });
    }
}
