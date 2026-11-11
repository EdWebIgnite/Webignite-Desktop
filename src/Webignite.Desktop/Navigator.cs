using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Mail;
using Webignite.Core.Overview;

namespace Webignite.Desktop;

/// <summary>Every page in the sidebar, by name. Jumping to a page uses one of these, never its title.</summary>
public enum Page
{
    Overview, Approvals, Mail,
    Clients, Contacts, Files, Retainers, Renewals,
    Quotes, Invoices, Money, Reports,
    Home, Projects, Notes, Calendar, Planner, Time, MyPages,
    SiteCheck, Crawler, Markup, Pdf,
    Settings,
    Inbox,
}

/// <summary>
/// How one page sends the person to another: open a client, a document, a mail, or just a page. The main
/// window wires it up once; before that (tests, screenshots) every call is a quiet no-op. Pages talk to
/// this, never to each other.
/// </summary>
public sealed class Navigator
{
    internal Action<Page>? OpenPage { get; set; }
    internal Action<MailSummary>? OpenMailAction { get; set; }
    internal Action<IReadOnlyList<MailPerson>>? WriteMailAction { get; set; }
    internal Func<OutgoingMail, string?, Func<IReadOnlyList<MailPerson>, Task>?, Func<OutgoingMail, Task<OutgoingMail>>?, bool>? ComposeMailAction { get; set; }
    internal Action<StoredDocument>? OpenDocumentAction { get; set; }
    internal Action<Client>? OpenClientAction { get; set; }
    internal Action<Client>? OpenClientInNewTabAction { get; set; }
    internal Action<Client>? OpenClientInNewWindowAction { get; set; }
    internal Action<MailSummary>? OpenMailInNewTabAction { get; set; }
    internal Action<MailSummary>? OpenMailInNewWindowAction { get; set; }
    internal Action<string, bool>? OpenFolderAction { get; set; }
    internal Action<string>? ShowFileAction { get; set; }
    internal Action<string>? OpenFolderInNewWindowAction { get; set; }
    internal Action<string>? MarkupPictureAction { get; set; }
    internal Action<string>? OpenSettingsAction { get; set; }
    internal Action? SetupGuideAction { get; set; }
    internal Action<bool>? ScreenshotAction { get; set; }
    internal Action<bool>? RecordAction { get; set; }
    internal Action<IReadOnlyList<string>>? OpenPdfAction { get; set; }
    internal Action<string>? OpenNoteAction { get; set; }
    internal Action<string?, string?>? OpenPersonAction { get; set; }
    internal Action<string>? OpenProjectTaskAction { get; set; }
    internal Action<string>? OpenProjectAction { get; set; }
    internal Action<string>? OpenProjectMessagesAction { get; set; }
    internal Action? MyTasksAction { get; set; }
    internal Action? NewProjectAction { get; set; }
    internal Action<string, string>? ClientNotesAction { get; set; }
    internal Action? QuickNoteAction { get; set; }
    internal Action<ViewModels.ToolKind>? OpenToolAction { get; set; }
    internal Action<Action<string>?>? PickColourAction { get; set; }
    internal Action<string>? OpenPictureAction { get; set; }
    internal Func<decimal, string, string?>? AddToInvoiceAction { get; set; }

    /// <summary>Shows a page.</summary>
    public void To(Page page) => OpenPage?.Invoke(page);

    /// <summary>Opens a mail on the Mail page.</summary>
    public void Mail(MailSummary mail) => OpenMailAction?.Invoke(mail);

    /// <summary>Starts a new mail to these people on the Mail page.</summary>
    public void WriteMail(IReadOnlyList<MailPerson> to) => WriteMailAction?.Invoke(to);

    /// <summary>
    /// Opens a prepared mail in the Mail page, with a note shown above it. The action runs after it was sent,
    /// given who it went to.
    /// Returns false when another mail with changes is still open.
    /// </summary>
    public bool ComposeMail(OutgoingMail mail, string? note, Func<IReadOnlyList<MailPerson>, Task>? afterSent,
        Func<OutgoingMail, Task<OutgoingMail>>? beforeSend = null) =>
        ComposeMailAction?.Invoke(mail, note, afterSent, beforeSend) ?? false;

    /// <summary>Opens a quote or invoice on its own page, e.g. a copy just made.</summary>
    public void Document(StoredDocument stored) => OpenDocumentAction?.Invoke(stored);

    /// <summary>Opens a client's page.</summary>
    public void Client(Client client) => OpenClientAction?.Invoke(client);

    /// <summary>Opens a folder in a Files window of its own.</summary>
    public void FolderInNewWindow(string path) => OpenFolderInNewWindowAction?.Invoke(path);

    /// <summary>Opens a picture in Mark-up, to draw on it.</summary>
    public void Markup(string path) => MarkupPictureAction?.Invoke(path);

    /// <summary>Shows the folder a file is in, in Files, with the file picked: the app's own "Show in folder".</summary>
    public void ShowFile(string path) => ShowFileAction?.Invoke(path);

    /// <summary>Opens a folder in Files, or in a Files tab of its own (Ctrl+click or a middle click on a folder).</summary>
    public void Folder(string path, bool newTab = false) => OpenFolderAction?.Invoke(path, newTab);

    /// <summary>
    /// Freezes the screen to pick a part to grab, then opens it in Mark-up. From a button in the app, the app's
    /// window steps aside first (<paramref name="hideApp"/>); from the shortcut it is left as it is.
    /// </summary>
    public void Screenshot(bool hideApp = true) => ScreenshotAction?.Invoke(hideApp);

    /// <summary>
    /// Freezes the screen for the eyedropper; the colour clicked is copied as its code, e.g. #E8590C, and handed
    /// to <paramref name="picked"/> (the Colours tool), or offered in a note to open in Colours.
    /// </summary>
    public void PickColour(Action<string>? picked = null) => PickColourAction?.Invoke(picked);

    /// <summary>Shows a picture in Webignite's own viewer, with the others in its folder a key away.</summary>
    public void Picture(string path) => OpenPictureAction?.Invoke(path);

    /// <summary>Starts recording part of the screen, or stops the recording under way and opens it in Mark-up.</summary>
    public void Record(bool hideApp = true) => RecordAction?.Invoke(hideApp);

    /// <summary>Opens PDFs (and pictures, as pages) in the PDF tools.</summary>
    public void Pdf(IReadOnlyList<string> paths) => OpenPdfAction?.Invoke(paths);

    /// <summary>
    /// Opens a PDF that isn't a file yet (a mail's attachment) in the PDF tools: it is kept in a temporary folder,
    /// and saving asks where it should go.
    /// </summary>
    public void Pdf(string name, byte[] data)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Webignite PDFs", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Path.GetFileName(name));
        File.WriteAllBytes(path, data);
        Pdf([path]);
    }

    /// <summary>Opens a note on the Notes page.</summary>
    public void Note(string id) => OpenNoteAction?.Invoke(id);

    /// <summary>Opens a person in Contacts.</summary>
    public void Person(string id) => OpenPersonAction?.Invoke(id, null);

    /// <summary>Opens a task on the Projects page, beside its project.</summary>
    public void ProjectTask(string id) => OpenProjectTaskAction?.Invoke(id);

    /// <summary>Opens a project on the Projects page.</summary>
    public void Project(string id) => OpenProjectAction?.Invoke(id);

    /// <summary>Opens a project's Messages tab on the Projects page.</summary>
    public void ProjectMessages(string id) => OpenProjectMessagesAction?.Invoke(id);

    /// <summary>Shows My tasks on the Projects page.</summary>
    public void MyTasks() => MyTasksAction?.Invoke();

    /// <summary>Makes a new project and opens it, ready to be named.</summary>
    public void NewProject() => NewProjectAction?.Invoke();

    /// <summary>A new person in Contacts, already with this client.</summary>
    public void NewPerson(string clientId) => OpenPersonAction?.Invoke(null, clientId);

    /// <summary>Shows the notes about a client on the Notes page.</summary>
    public void ClientNotes(string clientId, string clientName) => ClientNotesAction?.Invoke(clientId, clientName);

    /// <summary>Opens a small tool (the calculator, the QR codes) in a window of its own.</summary>
    public void Tool(ViewModels.ToolKind kind) => OpenToolAction?.Invoke(kind);

    internal Action? OpenChatAction { get; set; }

    /// <summary>Opens the chat with your partner, or brings it forward.</summary>
    public void Chat() => OpenChatAction?.Invoke();

    /// <summary>The small quick-note box, as from the shortcut.</summary>
    public void QuickNote() => QuickNoteAction?.Invoke();

    /// <summary>Starts the setup guide again.</summary>
    public void SetupGuide() => SetupGuideAction?.Invoke();

    /// <summary>Opens Settings at a section, by its key (e.g. "files").</summary>
    public void Settings(string section) => OpenSettingsAction?.Invoke(section);

    /// <summary>Opens a client in a window of its own.</summary>
    public void ClientInNewWindow(Client client) => OpenClientInNewWindowAction?.Invoke(client);

    /// <summary>Opens a client in a tab of its own (Ctrl+click or a middle click on the client).</summary>
    public void ClientInNewTab(Client client) => OpenClientInNewTabAction?.Invoke(client);

    /// <summary>Opens a mail in a Mail window of its own, e.g. to read it on the other screen.</summary>
    public void MailInNewWindow(MailSummary mail) => OpenMailInNewWindowAction?.Invoke(mail);

    /// <summary>Opens a mail in a Mail tab of its own (a middle click on the mail).</summary>
    public void MailInNewTab(MailSummary mail) => OpenMailInNewTabAction?.Invoke(mail);

    /// <summary>Puts an amount on the invoice being edited, or a new one, and opens it. Returns a problem, or null.</summary>
    public string? AddToInvoice(decimal amount, string description) => AddToInvoiceAction?.Invoke(amount, description);

    /// <summary>Opens what a morning summary line points at: a mail, or its page.</summary>
    public void Summary(SummaryTarget target, MailSummary? mail)
    {
        if (target == SummaryTarget.Mail && mail is not null)
            Mail(mail);
        else
            To(PageFor(target));
    }

    public static Page PageFor(SummaryTarget target) => target switch
    {
        SummaryTarget.Invoices => Page.Invoices,
        SummaryTarget.Approvals => Page.Approvals,
        SummaryTarget.Mail => Page.Mail,
        SummaryTarget.Renewals => Page.Renewals,
        SummaryTarget.Money => Page.Money,
        SummaryTarget.Clients => Page.Clients,
        SummaryTarget.SiteCheck => Page.SiteCheck,
        _ => Page.Settings,
    };
}
