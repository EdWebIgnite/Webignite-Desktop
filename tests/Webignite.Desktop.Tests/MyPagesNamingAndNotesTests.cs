using Webignite.Core.Layout;
using Webignite.Core.Writing;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>A page of your own: naming it, and notes on it with formatting.</summary>
public sealed class MyPagesNamingAndNotesTests
{
    [Fact]
    public Task A_new_page_asks_for_its_name_and_the_name_typed_is_kept() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var pages = new MyPagesViewModel(app.Services);
        var asked = 0;
        pages.NameAsked += () => asked++;

        pages.NewPage();
        Assert.Equal(1, asked);
        pages.EditName = "Karoo Coffee";
        pages.ShowList();

        using var again = new MyPagesViewModel(app.Services);
        Assert.Contains(app.Services.Settings.MyPages, p => p.Name == "Karoo Coffee");
        pages.Dispose();
    });

    [Fact]
    public Task A_page_can_be_renamed_without_arranging_it() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var pages = new MyPagesViewModel(app.Services);
        pages.NewPage();
        pages.ToggleEditingCommand.Execute(null);
        Assert.False(pages.IsEditing);

        pages.EditName = "Monday";
        Assert.Equal("Monday", pages.TabTitle is null ? null : pages.EditName);
        pages.ShowList();

        Assert.Contains(app.Services.Settings.MyPages, p => p.Name == "Monday");
        pages.Dispose();
    });

    [Fact]
    public Task A_note_on_a_page_keeps_bold_and_bullets() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var pages = new MyPagesViewModel(app.Services);
        pages.NewPage();
        pages.AddWidget(WidgetKind.Note);
        var note = (NoteBody)pages.Widgets.Single().Body;

        note.Document = RichHtml.Read("<p><b>Call</b> Pieter</p><ul><li>photos</li><li>logo</li></ul>", RichHtml.DataAddress);
        note.Revision++;
        Assert.True(note.HasTyped);
        Assert.True(pages.HoldsWork);
        note.Keep();

        var saved = app.Services.Settings.MyPages.Single().Widgets.Single();
        Assert.Contains("Call Pieter", saved.Text!, StringComparison.Ordinal);
        Assert.Contains("<b>", saved.Html!, StringComparison.OrdinalIgnoreCase);
        // Opened again, the formatting is still there.
        pages.ShowList();
        pages.OpenPage(app.Services.Settings.MyPages.Single().Id);
        var again = (NoteBody)pages.Widgets.Single().Body;
        Assert.Equal(2, again.Document.Blocks.OfType<TextBlock>().Count(b => b.List == ListKind.Bullet));
        Assert.False(again.HasTyped);
        pages.Dispose();
    });

    [Fact]
    public Task Setting_a_notes_words_still_works_and_is_kept() => Ui.Run(() =>
    {
        using var app = new TestApp();
        var pages = new MyPagesViewModel(app.Services);
        pages.NewPage();
        pages.AddWidget(WidgetKind.Note);
        var note = (NoteBody)pages.Widgets.Single().Body;

        note.Text = "Call Pieter after 10";
        note.Keep();

        Assert.Equal("Call Pieter after 10", app.Services.Settings.MyPages.Single().Widgets.Single().Text);
        pages.Dispose();
    });
}
