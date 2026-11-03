using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Core.Mail;
using Webignite.Core.Overview;

namespace Webignite.Desktop;

/// <summary>Every page in the sidebar, by name. Jumping to a page uses one of these, never its title.</summary>
public enum Page
{
    Overview, Approvals, Mail,
    Clients, Retainers, Renewals,
    Quotes, Invoices, Money, Reports,
    Todo, Calendar, Planner, Time,
    SiteCheck, Markup, Tools,
    Settings,
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
    internal Func<OutgoingMail, string?, Func<Task>?, bool>? ComposeMailAction { get; set; }
    internal Action<StoredDocument>? OpenDocumentAction { get; set; }
    internal Action<Client>? OpenClientAction { get; set; }
    internal Func<decimal, string, string?>? AddToInvoiceAction { get; set; }

    /// <summary>Shows a page.</summary>
    public void To(Page page) => OpenPage?.Invoke(page);

    /// <summary>Opens a mail on the Mail page.</summary>
    public void Mail(MailSummary mail) => OpenMailAction?.Invoke(mail);

    /// <summary>Starts a new mail to these people on the Mail page.</summary>
    public void WriteMail(IReadOnlyList<MailPerson> to) => WriteMailAction?.Invoke(to);

    /// <summary>
    /// Opens a prepared mail in the Mail page, with a note shown above it. The action runs after it was sent.
    /// Returns false when another mail with changes is still open.
    /// </summary>
    public bool ComposeMail(OutgoingMail mail, string? note, Func<Task>? afterSent) =>
        ComposeMailAction?.Invoke(mail, note, afterSent) ?? false;

    /// <summary>Opens a quote or invoice on its own page, e.g. a copy just made.</summary>
    public void Document(StoredDocument stored) => OpenDocumentAction?.Invoke(stored);

    /// <summary>Opens a client's page.</summary>
    public void Client(Client client) => OpenClientAction?.Invoke(client);

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
