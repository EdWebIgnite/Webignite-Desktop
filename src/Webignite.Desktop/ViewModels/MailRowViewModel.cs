using System.Collections.ObjectModel;
using Avalonia.Controls.Selection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;
using Webignite.Mail;

namespace Webignite.Desktop.ViewModels;

public partial class MailRowViewModel(MailSummary summary, MailAccount account, bool showMailbox, MailViewModel owner) : ViewModelBase
{
    /// <summary>The mail the row shows: the newest in its conversation.</summary>
    public MailSummary Summary { get; private set; } = summary;

    /// <summary>All the mail in this row's conversation, oldest first. Just the one mail when conversations are off.</summary>
    public IReadOnlyList<MailSummary> Thread { get; internal set; } = [summary];

    /// <summary>The client this mail is from or to, for the badge on the row. Null for everyone else.</summary>
    public Webignite.Core.Clients.Client? Client { get; internal set; }
    public bool HasClient => Client is not null;
    public string ClientName => Client?.Name ?? "";

    /// <summary>How many mails the conversation holds, shown beside the sender when more than one.</summary>
    public int Count => Thread.Count;
    public bool IsConversation => Thread.Count > 1;
    public MailAccount Account => account;
    public bool ShowMailbox => showMailbox;

    private bool IsOutgoing => MailFolders.IsOutgoing(owner.RoleOf(Summary));

    public string Who => IsOutgoing
        ? "To " + (Summary.To.Count == 0 ? "nobody yet" : string.Join(", ", Summary.To.Select(p => p.Display)))
        : Summary.From?.Display ?? "Unknown sender";

    public string Subject => string.IsNullOrWhiteSpace(Summary.Subject) ? "(no subject)" : Summary.Subject;
    public string Preview => Summary.Preview;
    public bool IsUnread => Thread.Any(m => !m.Seen);
    public bool IsImportant => Thread.Any(m => m.Flagged);
    public bool HasAttachments => Thread.Any(m => m.HasAttachments);
    public string MailboxTag => account.Address.Split('@')[0];

    /// <summary>The categories this mail is in, shown as coloured dots.</summary>
    public IReadOnlyList<MailCategory> Dots => MailCategories.Of(owner.CategoryList, Summary);
    public bool HasDots => Dots.Count > 0;
    public string DotsTip => string.Join(", ", Dots.Select(c => c.Name));

    public string When
    {
        get
        {
            var local = Summary.Date.ToLocalTime();
            var today = DateTime.Today;
            return local.Date == today ? local.ToString("HH:mm")
                : local.Date > today.AddDays(-7) ? local.ToString("ddd HH:mm")
                : local.Year == today.Year ? local.ToString("d MMM")
                : local.ToString("d MMM yyyy");
        }
    }

    internal void Update(MailSummary updated) => Update(new Dictionary<(string, string, uint), MailSummary> { [MailViewModel.Key(updated)] = updated });

    internal void Update(IReadOnlyDictionary<(string, string, uint), MailSummary> changed)
    {
        Thread = Thread.Select(m => changed.GetValueOrDefault(MailViewModel.Key(m), m)).ToList();
        Summary = changed.GetValueOrDefault(MailViewModel.Key(Summary), Summary);
        OnPropertyChanged(nameof(IsUnread));
        OnPropertyChanged(nameof(IsImportant));
        RefreshDots();
    }

    internal void SetThread(IReadOnlyList<MailSummary> thread)
    {
        Thread = thread;
        Summary = thread[^1];
        OnPropertyChanged(string.Empty);
    }

    internal void RefreshDots()
    {
        OnPropertyChanged(nameof(Dots));
        OnPropertyChanged(nameof(HasDots));
        OnPropertyChanged(nameof(DotsTip));
    }
}
