using Avalonia.Controls;
using Avalonia.Media;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>
/// A mail being written, in a window of its own, so the mail it answers and the rest of the app stay in view
/// behind it. The mail still belongs to its Mail page: sending, Undo and Drafts work as they do there. Closing
/// the window asks what the composer's own Close asks (save the draft, discard, keep writing).
/// </summary>
public sealed class ComposeWindow : Window
{
    private readonly ComposeViewModel composer;
    private bool leaving;

    public ComposeWindow(ComposeViewModel composer)
    {
        this.composer = composer;
        DataContext = composer;
        Width = 780;
        Height = 720;
        MinWidth = 440;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = this.TryFindResource("Canvas", out var canvas) && canvas is IBrush brush ? brush : Brushes.WhiteSmoke;
        var view = new ComposeView { DataContext = composer };
        // The window's own title and X stand in for the composer's heading row, which gives the mail another line.
        view.FindControl<Panel>("Root")?.Classes.Add("inwindow");
        Content = view;
        Looks.Follow(this);
        ShowTitle();
        composer.PropertyChanged += Changed;
        Closed += (_, _) => composer.PropertyChanged -= Changed;
    }

    public ComposeViewModel Composer => composer;

    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ComposeViewModel.Subject))
            ShowTitle();
    }

    private void ShowTitle() => Title = string.IsNullOrWhiteSpace(composer.Subject) ? "New mail" : composer.Subject.Trim();

    /// <summary>Closes without asking: the mail was sent, saved or discarded, or the app is closing (which saves it first).</summary>
    public void Leave()
    {
        leaving = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (leaving || e.CloseReason != WindowCloseReason.WindowClosing)
            return;
        // The X is the composer's own Close: it goes at once when nothing was typed, and otherwise asks in the window.
        e.Cancel = true;
        composer.CloseCommand.Execute(null);
    }
}
