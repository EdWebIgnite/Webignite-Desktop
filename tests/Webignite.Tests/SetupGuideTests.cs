using Webignite.Core.Accounts;

namespace Webignite.Tests;

/// <summary>The setup guide's steps: where it starts, moving through, and what the main button says.</summary>
public sealed class SetupGuideTests
{
    [Fact]
    public void It_starts_on_joining_unless_this_pc_is_signed_in_then_on_xero()
    {
        Assert.Equal("join", SetupGuide.Steps[SetupGuide.Start(signedIn: false)].Key);
        Assert.Equal("xero", SetupGuide.Steps[SetupGuide.Start(signedIn: true)].Key);
    }

    [Fact]
    public void Joining_xero_and_chatgpt_come_first_on_settings_then_a_look_round_and_done_last()
    {
        Assert.Equal(["join", "xero", "chatgpt"], SetupGuide.Steps.Take(3).Select(s => s.Key));
        Assert.All(SetupGuide.Steps.Take(3), s => Assert.True(s.Page == "Settings" && s.Section is not null && s.Check != SetupCheck.None && s.DoneText.Length > 0));
        Assert.All(SetupGuide.Steps.Skip(3), s => Assert.Equal(SetupCheck.None, s.Check));
        Assert.Equal("done", SetupGuide.Steps[^1].Key);
        Assert.Equal(SetupGuide.Steps.Count, SetupGuide.Steps.Select(s => s.Key).Distinct().Count());
    }

    [Fact]
    public void Back_and_next_stay_within_the_steps_and_the_button_says_what_happens()
    {
        Assert.Equal(0, SetupGuide.Back(0));
        Assert.Equal(SetupGuide.Steps.Count - 1, SetupGuide.Next(SetupGuide.Steps.Count - 1));
        Assert.Equal("Skip for now", SetupGuide.NextLabel(0, done: false));
        Assert.Equal("Next", SetupGuide.NextLabel(0, done: true));
        Assert.Equal("Next", SetupGuide.NextLabel(4, done: false));
        Assert.Equal("Finish", SetupGuide.NextLabel(SetupGuide.Steps.Count - 1, done: true));
        Assert.Equal($"Step 2 of {SetupGuide.Steps.Count}", SetupGuide.Progress(1));
    }

    [Fact]
    public void The_words_are_plain_without_long_dashes()
    {
        Assert.All(SetupGuide.Steps, s => Assert.DoesNotContain('—', s.Title + s.Text + s.DoneText));
    }
}
