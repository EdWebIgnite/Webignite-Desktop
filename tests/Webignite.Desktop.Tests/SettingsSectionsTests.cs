using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>Settings is one page made of parts that each stand on their own; the page hands them out and listens to two of them.</summary>
public sealed class SettingsSectionsTests
{
    [Fact]
    public Task A_file_tag_is_added_once_and_a_second_of_the_same_name_is_refused() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var settings = new SettingsViewModel(app.Services);
        var tags = settings.FileTags;
        var before = tags.FileTagRows.Count;
        Assert.False(tags.AddFileTagCommand.CanExecute(null));

        tags.NewFileTag = "Waiting on client";
        tags.AddFileTagCommand.Execute(null);

        Assert.Equal(before + 1, tags.FileTagRows.Count);
        Assert.Equal("", tags.NewFileTag);
        Assert.Equal("", tags.FileTagMessage);

        tags.NewFileTag = "waiting on client";
        tags.AddFileTagCommand.Execute(null);

        Assert.Equal(before + 1, tags.FileTagRows.Count);
        Assert.NotEmpty(tags.FileTagMessage);
        settings.Dispose();
    });

    [Fact]
    public Task The_page_says_when_ChatGPT_is_connected_as_the_setup_guide_watches_the_page() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var settings = new SettingsViewModel(app.Services);
        var heard = new List<string?>();
        settings.PropertyChanged += (_, e) => heard.Add(e.PropertyName);

        settings.ChatGpt.ChatGptConnected = !settings.ChatGpt.ChatGptConnected;

        Assert.Contains(nameof(SettingsChatGptViewModel.ChatGptConnected), heard);

        // Once let go, the page no longer answers for its parts.
        settings.Dispose();
        heard.Clear();
        settings.ChatGpt.ChatGptConnected = !settings.ChatGpt.ChatGptConnected;
        Assert.Empty(heard);
    });

    [Fact]
    public Task A_new_install_is_signed_out_with_nothing_to_show_for_the_team() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var settings = new SettingsViewModel(app.Services);

        Assert.False(settings.Account.IsSignedIn);
        Assert.Null(settings.Account.TwoFactorQr);
        Assert.Null(settings.MailBrand.BrandLogo);
        settings.Dispose();
    });
}
