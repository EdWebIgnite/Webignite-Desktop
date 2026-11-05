using Webignite.Core;
using Webignite.Core.Backups;
using Webignite.Core.Documents;
using Webignite.Core.Mail;
using Webignite.Core.Overview;

namespace Webignite.Desktop;

/// <summary>
/// Gathers what the morning summary (Needs you) is worked out from, for Overview and the Needs you widget on the
/// person's own pages. The rules themselves are <see cref="MorningSummary"/> in Core.
/// </summary>
public static class Summaries
{
    public static SummaryInput Input(AppServices services, DateOnly today, IReadOnlyList<StoredDocument> documents)
    {
        var accounts = services.Store.ListMailAccounts();
        var hasPassword = services.HasBackupPassword;
        return new SummaryInput
        {
            Today = today,
            Documents = documents,
            Renewals = services.Store.ListRenewals(),
            Entries = services.Store.ListEntries(Dates.MonthStart(today), today),
            Inbox = Inbox(services, 300),
            Clients = services.Clients.List(),
            OwnAddresses = accounts.Select(a => a.Address).ToList(),
            StatementsSent = services.Settings.StatementsSent,
            Sites = Webignite.Core.Web.Uptime.Domains(services.Clients.List(), services.Settings.Uptime)
                .Select(d => services.Settings.Uptime.Sites.GetValueOrDefault(d.Domain)).OfType<Webignite.Core.Web.WatchedSite>().ToList(),
            BackupsOn = hasPassword,
            BackupOverdue = BackupRules.Overdue(services.Settings.Backups, hasPassword, DateTimeOffset.Now),
            NoReplyNeeded = services.Settings.NoReplyNeeded,
        };
    }

    /// <summary>The mail in every account's inbox kept on this PC, up to a number from each, account by account.</summary>
    public static List<MailSummary> Inbox(AppServices services, int each)
    {
        var inbox = new List<MailSummary>();
        foreach (var account in services.Store.ListMailAccounts())
        {
            if (services.Store.ListMailFolders(account.Id).FirstOrDefault(f => f.Role == FolderRole.Inbox) is { } folder)
                inbox.AddRange(services.Store.ListMailSummaries(account.Id, folder.Path, each));
        }
        return inbox;
    }
}
