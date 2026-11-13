using Webignite.Core.Chat;

namespace Webignite.Tests;

/// <summary>Chat between the two of you: files cut into parts and put back together, what counts as unread, and notes.</summary>
public sealed class ChatTests
{
    [Fact]
    public void A_file_travels_in_parts_and_comes_back_whole_only_when_every_part_is_here()
    {
        var data = Enumerable.Range(0, ChatRules.PartBytes * 2 + 100).Select(i => (byte)(i % 251)).ToArray();
        var (file, parts) = ChatRules.Split("homepage.png", data);
        Assert.Equal(3, file.Parts);
        Assert.Equal(data.Length, file.Bytes);
        Assert.Equal(data, ChatRules.Join(file, parts.Reverse()));
        Assert.Null(ChatRules.Join(file, parts.Take(2)));
    }

    [Fact]
    public void Big_or_empty_files_are_explained()
    {
        Assert.Null(ChatRules.Problem("brief.pdf", 120_000));
        Assert.Contains("10 MB", ChatRules.Problem("video.mp4", ChatRules.MostBytes + 1), StringComparison.Ordinal);
        Assert.Contains("empty", ChatRules.Problem("blank.txt", 0), StringComparison.Ordinal);
        Assert.True(ChatRules.IsPicture("Screenshot.PNG"));
        Assert.False(ChatRules.IsPicture("quote.pdf"));
    }

    [Fact]
    public void Only_the_partners_messages_after_the_last_look_are_unread_and_notes_say_what_came()
    {
        var at = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        ChatMessage[] messages =
        [
            new() { Id = "1", From = "jaco", Text = "Morning", Sent = at },
            new() { Id = "2", From = "eddie", Text = "Hi", Sent = at.AddMinutes(1) },
            new() { Id = "3", From = "jaco", Text = "", Files = [new ChatFile("f", "Screenshot.png", 10, 1)], Sent = at.AddMinutes(2) },
        ];
        Assert.Equal(2, ChatRules.Unread(messages, "eddie", DateTimeOffset.MinValue));
        Assert.Equal(1, ChatRules.Unread(messages, "eddie", at));
        Assert.Equal("Sent a picture.", ChatRules.Preview(messages[2]));
        Assert.EndsWith("…", ChatRules.Preview(messages[0] with { Text = new string('a', 120) }), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_message_says_when_with_the_time_even_on_older_days()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("Today at 09:05", ChatRules.When(now.AddHours(-2).AddMinutes(-55), now, TimeZoneInfo.Utc, culture));
        Assert.Equal("Yesterday at 16:02", ChatRules.When(new DateTimeOffset(2026, 11, 9, 16, 2, 0, TimeSpan.Zero), now, TimeZoneInfo.Utc, culture));
        Assert.Equal("4 Nov at 09:30", ChatRules.When(new DateTimeOffset(2026, 11, 4, 9, 30, 0, TimeSpan.Zero), now, TimeZoneInfo.Utc, culture));
        Assert.Equal("4 Nov 2025 at 09:30", ChatRules.When(new DateTimeOffset(2025, 11, 4, 9, 30, 0, TimeSpan.Zero), now, TimeZoneInfo.Utc, culture));
    }

    [Fact]
    public void A_status_shows_as_picked_away_after_ten_quiet_minutes_and_offline_once_the_pc_stops_syncing()
    {
        var now = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
        var here = new Presence("jaco", PresenceState.Available, now.AddSeconds(-30), now.AddMinutes(-2));
        Assert.Equal(PresenceState.Available, PresenceRules.Shown(here, now));
        Assert.Equal(PresenceState.Away, PresenceRules.Shown(here with { LastActive = now.AddMinutes(-11) }, now));
        Assert.Equal(PresenceState.Busy, PresenceRules.Shown(here with { Chosen = PresenceState.Busy, LastActive = now.AddMinutes(-30) }, now));
        Assert.Equal(PresenceState.Offline, PresenceRules.Shown(here with { LastSeen = now.AddMinutes(-4) }, now));
        Assert.Equal(PresenceState.Offline, PresenceRules.Shown(null, now));
        Assert.False(PresenceRules.Interrupts(PresenceState.Busy));
        Assert.True(PresenceRules.Interrupts(PresenceState.Away));
        Assert.DoesNotContain(PresenceState.Offline, PresenceRules.Choices);
    }

    [Theory]
    [InlineData(true, false, null, ChatPaste.Files)]
    [InlineData(true, true, "some words", ChatPaste.Files)]
    [InlineData(false, true, null, ChatPaste.Picture)]
    [InlineData(false, true, "https://example.co.za/photos/shop-front.jpg", ChatPaste.Picture)]
    [InlineData(false, true, "The shop front, as Pieter sent it", ChatPaste.Text)]
    [InlineData(false, false, "Morning!", ChatPaste.Text)]
    [InlineData(false, false, null, ChatPaste.Nothing)]
    public void A_paste_adds_files_a_picture_or_words_as_WhatsApp_does(bool files, bool picture, string? text, ChatPaste expected) =>
        Assert.Equal(expected, ChatRules.PasteAs(files, picture, text));

    private static readonly TimeZoneInfo Sast = TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");
    private static readonly System.Globalization.CultureInfo ZA = System.Globalization.CultureInfo.GetCultureInfo("en-ZA");

    private static ChatMessage Said(string from, DateTimeOffset at) => new() { Id = ChatMessage.NewId(), From = from, Text = "Hi", Sent = at };

    [Fact]
    public void Ticks_show_a_clock_until_sent_one_tick_on_the_server_and_two_once_read()
    {
        var at = new DateTimeOffset(2026, 11, 11, 7, 1, 0, TimeSpan.Zero);
        var message = Said("eddie", at);
        Assert.Equal(ChatTick.Waiting, ChatRules.Tick(message, sent: false, partnerReadUpTo: null));
        Assert.Equal(ChatTick.Sent, ChatRules.Tick(message, sent: true, partnerReadUpTo: at.AddMinutes(-1)));
        Assert.Equal(ChatTick.Read, ChatRules.Tick(message, sent: true, partnerReadUpTo: at));
    }

    [Fact]
    public void Days_read_today_yesterday_the_weekday_then_the_date()
    {
        var now = new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero); // Wednesday, 10:00 in South Africa
        Assert.Equal("Today", ChatRules.Day(now.AddHours(-1), now, Sast, ZA));
        Assert.Equal("Yesterday", ChatRules.Day(now.AddDays(-1), now, Sast, ZA));
        Assert.Equal("Monday", ChatRules.Day(now.AddDays(-2), now, Sast, ZA));
        Assert.Contains("October", ChatRules.Day(now.AddDays(-30), now, Sast, ZA), StringComparison.Ordinal);
        Assert.Contains("2025", ChatRules.Day(now.AddDays(-400), now, Sast, ZA), StringComparison.Ordinal);
    }

    [Fact]
    public void Messages_from_the_same_person_a_few_minutes_apart_are_grouped()
    {
        var at = new DateTimeOffset(2026, 11, 11, 7, 0, 0, TimeSpan.Zero);
        var first = Said("jaco", at);
        Assert.False(ChatRules.Follows(null, first, Sast));
        Assert.True(ChatRules.Follows(first, Said("jaco", at.AddMinutes(2)), Sast));
        Assert.False(ChatRules.Follows(first, Said("eddie", at.AddMinutes(2)), Sast));
        Assert.False(ChatRules.Follows(first, Said("jaco", at.AddMinutes(20)), Sast));
    }

    [Fact]
    public void The_line_under_my_partner_s_name_says_online_or_when_they_were_last_seen()
    {
        var now = new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal("offline", PresenceRules.Line(null, now, Sast, ZA));
        Assert.Equal("online", PresenceRules.Line(new Presence("jaco", PresenceState.Available, now, now), now, Sast, ZA));
        Assert.Equal("busy", PresenceRules.Line(new Presence("jaco", PresenceState.Busy, now, now), now, Sast, ZA));
        var gone = new Presence("jaco", PresenceState.Available, now.AddHours(-1), now.AddHours(-1));
        Assert.StartsWith("last seen today at", PresenceRules.Line(gone, now, Sast, ZA), StringComparison.Ordinal);
    }

    [Fact]
    public void Chat_is_looked_for_every_few_seconds_while_the_window_is_open_or_messages_went_lately()
    {
        var now = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
        Assert.True(ChatTiming.IsLively(chatOpen: true, lastMessage: null, now));
        Assert.True(ChatTiming.IsLively(chatOpen: false, lastMessage: now.AddMinutes(-3), now));
        Assert.False(ChatTiming.IsLively(chatOpen: false, lastMessage: now.AddMinutes(-30), now));
        Assert.False(ChatTiming.IsLively(chatOpen: false, lastMessage: null, now));
        Assert.True(ChatTiming.Lively <= TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_picture_is_told_from_its_first_bytes_whatever_it_is_called()
    {
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1];
        byte[] webp = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'E', (byte)'B', (byte)'P'];
        byte[] pdf = [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'7', 10, 10, 10, 10];
        Assert.True(ChatRules.IsPicture("funny", png));
        Assert.True(ChatRules.IsPicture("download.bin", jpeg));
        Assert.True(ChatRules.IsPicture("meme", webp));
        Assert.False(ChatRules.IsPicture("quote", pdf));
        Assert.True(ChatRules.IsPicture("photo.jfif"));
        Assert.False(ChatRules.LooksLikePicture([0xFF, 0xD8]));
    }
}
