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
}
