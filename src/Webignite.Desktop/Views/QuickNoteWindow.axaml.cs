using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Webignite.Core.Notes;

namespace Webignite.Desktop.Views;

/// <summary>
/// A small box from the quick-note shortcut, over whatever program is in front. What is typed is kept when the
/// box closes, however it closes, unless Discard is pressed: a thought caught in a hurry isn't lost.
/// </summary>
public partial class QuickNoteWindow : Window
{
    private readonly AppServices? services;
    private bool done;

    /// <summary>For the designer only.</summary>
    public QuickNoteWindow() => InitializeComponent();

    public QuickNoteWindow(AppServices services)
    {
        this.services = services;
        InitializeComponent();
        var notebooks = services.Notes.Notebooks();
        NotebookBox.ItemsSource = notebooks;
        NotebookBox.SelectedItem = notebooks.FirstOrDefault(n => n.Id == services.Settings.Notes.QuickNotebookId) ?? notebooks[0];
        AddHandler(KeyDownEvent, Keys, RoutingStrategies.Tunnel);
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Activate();
            Words.Focus();
        });
        Closing += (_, _) => Keep();
    }

    /// <summary>The note made, once the box is closed with something typed.</summary>
    public Note? Saved { get; private set; }

    /// <summary>The words, for a screenshot of the box.</summary>
    public string Typed
    {
        get => Words.Text ?? "";
        set => Words.Text = value;
    }

    private void Keys(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            Close();
            e.Handled = true;
        }
    }

    private void SaveClicked(object? sender, RoutedEventArgs e) => Close();

    private void DiscardClicked(object? sender, RoutedEventArgs e)
    {
        done = true;
        Close();
    }

    /// <summary>Saves what was typed, once.</summary>
    public void Keep()
    {
        if (done || services is null)
            return;
        done = true;
        var typed = Words.Text ?? "";
        if (typed.Trim().Length == 0)
            return;
        var notebook = (NotebookBox.SelectedItem as Notebook)?.Id;
        if (notebook is not null && notebook != services.Settings.Notes.QuickNotebookId)
            services.Settings.Notes = services.Settings.Notes with { QuickNotebookId = notebook };
        Saved = services.Notes.Quick(typed, notebook);
    }
}
