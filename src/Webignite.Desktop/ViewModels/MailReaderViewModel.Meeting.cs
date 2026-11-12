using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Calendar;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

// A meeting in the open mail: a calendar invite, or a Teams, Zoom or Meet link. It is shown to check and is only
// written into the calendar when Add to calendar is clicked.
public partial class MailReaderViewModel
{
    private MeetingInvite? meeting;
    private CalendarEvent? addedMeeting;

    public bool HasMeeting => meeting is not null;

    /// <summary>Nothing in the mail looked like a meeting, so the bar is offered by a link instead of shown by itself.</summary>
    public bool CanOfferMeeting => meeting is null && IsNotDraft && CanWrite;
    public bool MeetingAdded => addedMeeting is not null;
    public bool MeetingCanBeAdded => HasMeeting && !MeetingAdded;
    public string? MeetingLink => addedMeeting?.JoinLink ?? meeting?.JoinLink;
    public bool CanJoinMeeting => MeetingLink is not null;

    public string MeetingLead => addedMeeting is { } added
        ? $"In your calendar for {When(added.Date, added.Start)}."
        : meeting is { FromInvite: true } ? "This mail is a meeting invite."
        : meeting is { JoinLink: null } ? "Add this mail to your calendar. Give it a day, a time and, for an online meeting, its link."
        : "This mail has a link to an online meeting. The day and time were read from its words, so check them.";

    [ObservableProperty] public partial string MeetingTitle { get; set; } = "";
    [ObservableProperty] public partial DateTime? MeetingDay { get; set; }
    [ObservableProperty] public partial string MeetingTime { get; set; } = "";
    [ObservableProperty] public partial string MeetingMessage { get; set; } = "";

    /// <summary>The link to join the meeting, as found in the mail; it can be corrected or pasted in before adding.</summary>
    [ObservableProperty] public partial string MeetingLinkTyped { get; set; } = "";

    private string bodyText = "";

    private static string When(DateOnly day, TimeOnly? time) =>
        day.ToString("dddd, d MMMM", CultureInfo.CurrentCulture) + (time is { } t ? " at " + t.ToString("HH:mm", CultureInfo.InvariantCulture) : "");

    // The key an event added from this mail is kept under, so opening the mail again shows it is in the calendar.
    private string MeetingKey => Summary.MessageId is { Length: > 0 } id ? id : $"{Summary.AccountId}/{Summary.Folder}/{Summary.Uid}";

    private void FindMeeting(Webignite.Core.Mail.MailBody body)
    {
        var received = DateOnly.FromDateTime(Summary.Date.LocalDateTime);
        bodyText = body.Text;
        meeting = IsDraft ? null : MeetingInvites.InMail(Summary.Subject, body.Text, body.Html, body.Calendar, received, TimeZoneInfo.Local);
        addedMeeting = null;
        if (meeting is not null)
        {
            // Looked for around the day it is for, or for a year on from the mail when the day isn't known.
            var from = meeting.Date ?? received.AddDays(-1);
            var to = meeting.Date ?? received.AddDays(366);
            addedMeeting = services.Store.ListEvents(from.AddDays(-31), to.AddDays(31)).FirstOrDefault(e => e.MailId == MeetingKey);
            MeetingTitle = meeting.Title;
            MeetingDay = meeting.Date?.ToDateTime(TimeOnly.MinValue);
            MeetingTime = meeting.Start?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
            MeetingLinkTyped = meeting.JoinLink ?? "";
        }
        MeetingMessage = "";
        MeetingChanged();
    }

    private void MeetingChanged()
    {
        foreach (var name in new[] { nameof(HasMeeting), nameof(CanOfferMeeting), nameof(MeetingAdded), nameof(MeetingCanBeAdded), nameof(MeetingLink), nameof(CanJoinMeeting), nameof(MeetingLead) })
            OnPropertyChanged(name);
    }

    [RelayCommand]
    private void AddMeeting()
    {
        if (meeting is null)
            return;
        if (string.IsNullOrWhiteSpace(MeetingTitle))
        {
            MeetingMessage = "Give the meeting a name.";
            return;
        }
        if (MeetingDay is not { } day)
        {
            MeetingMessage = "Pick the day of the meeting.";
            return;
        }
        TimeOnly? start = null;
        if (!string.IsNullOrWhiteSpace(MeetingTime))
        {
            if (!CalendarEvents.TryParseTime(MeetingTime, out var typed))
            {
                MeetingMessage = "Type the time like 9:30 or 14:00, or leave it empty for all day.";
                return;
            }
            start = typed;
        }
        string? link = null;
        if (!string.IsNullOrWhiteSpace(MeetingLinkTyped) && (link = MeetingInvites.TypedLink(MeetingLinkTyped)) is null)
        {
            MeetingMessage = "That meeting link isn't a web address. Paste the whole link, or leave it empty.";
            return;
        }
        // The invite's own end when its start was kept; otherwise an hour.
        var end = start is null ? (TimeOnly?)null : start == meeting.Start && meeting.End is { } said ? said : start.Value.AddHours(1);
        addedMeeting = services.Store.SaveEvent(new CalendarEvent
        {
            Id = CalendarEvent.NewId(),
            Title = MeetingTitle.Trim(),
            Date = DateOnly.FromDateTime(day),
            Start = start,
            End = end,
            Location = meeting.Location,
            JoinLink = link,
            MailId = MeetingKey,
            Customer = ClientMail.For(services.Clients.List(), Summary.From?.Address)?.Name,
        });
        MeetingMessage = "";
        MeetingChanged();
        // The calendar, week and diary pages that are open show it straight away.
        services.AreasChanged?.Invoke(["calendar"]);
    }

    /// <summary>
    /// Any mail can go into the calendar, not only one that looked like a meeting: the bar opens with the mail's
    /// subject, and whatever day and time its words give, to fill in.
    /// </summary>
    [RelayCommand]
    private void OfferMeeting()
    {
        if (meeting is not null)
            return;
        var received = DateOnly.FromDateTime(Summary.Date.LocalDateTime);
        meeting = new MeetingInvite { Title = string.IsNullOrWhiteSpace(Summary.Subject) ? "Meeting" : Summary.Subject.Trim(), Date = MeetingInvites.DateIn(bodyText, received) };
        MeetingTitle = meeting.Title;
        MeetingDay = meeting.Date?.ToDateTime(TimeOnly.MinValue);
        MeetingTime = MeetingLinkTyped = MeetingMessage = "";
        MeetingChanged();
    }

    [RelayCommand]
    private void ShowMeetingInDiary()
    {
        if (addedMeeting is { } added)
            services.Go.Diary(added.Date);
    }

    [RelayCommand]
    private void JoinMeeting()
    {
        if (MeetingLink is { } link && Uri.TryCreate(link, UriKind.Absolute, out var url))
            AppServices.OpenInBrowser(url).Forget("Opening a meeting's link", tell: true);
    }
}
