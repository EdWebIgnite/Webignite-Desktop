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
    public bool MeetingAdded => addedMeeting is not null;
    public bool MeetingCanBeAdded => HasMeeting && !MeetingAdded;
    public string? MeetingLink => addedMeeting?.JoinLink ?? meeting?.JoinLink;
    public bool CanJoinMeeting => MeetingLink is not null;

    public string MeetingLead => addedMeeting is { } added
        ? $"In your calendar for {When(added.Date, added.Start)}."
        : meeting is { FromInvite: true } ? "This mail is a meeting invite."
        : "This mail has a link to an online meeting. The day and time were read from its words, so check them.";

    [ObservableProperty] public partial string MeetingTitle { get; set; } = "";
    [ObservableProperty] public partial DateTime? MeetingDay { get; set; }
    [ObservableProperty] public partial string MeetingTime { get; set; } = "";
    [ObservableProperty] public partial string MeetingMessage { get; set; } = "";

    private static string When(DateOnly day, TimeOnly? time) =>
        day.ToString("dddd, d MMMM", CultureInfo.CurrentCulture) + (time is { } t ? " at " + t.ToString("HH:mm", CultureInfo.InvariantCulture) : "");

    // The key an event added from this mail is kept under, so opening the mail again shows it is in the calendar.
    private string MeetingKey => Summary.MessageId is { Length: > 0 } id ? id : $"{Summary.AccountId}/{Summary.Folder}/{Summary.Uid}";

    private void FindMeeting(Webignite.Core.Mail.MailBody body)
    {
        var received = DateOnly.FromDateTime(Summary.Date.LocalDateTime);
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
        }
        MeetingMessage = "";
        MeetingChanged();
    }

    private void MeetingChanged()
    {
        foreach (var name in new[] { nameof(HasMeeting), nameof(MeetingAdded), nameof(MeetingCanBeAdded), nameof(MeetingLink), nameof(CanJoinMeeting), nameof(MeetingLead) })
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
            JoinLink = meeting.JoinLink,
            MailId = MeetingKey,
            Customer = ClientMail.For(services.Clients.List(), Summary.From?.Address)?.Name,
        });
        MeetingMessage = "";
        MeetingChanged();
        // The calendar, week and diary pages that are open show it straight away.
        services.AreasChanged?.Invoke(["calendar"]);
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
