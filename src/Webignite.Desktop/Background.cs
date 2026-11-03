using Avalonia.Threading;
using Webignite.Data;

namespace Webignite.Desktop;

/// <summary>
/// For work started without waiting for it, and for problems nobody else caught. A task that is just dropped
/// (<c>_ = DoAsync()</c>) or an <c>async void</c> handler fails in silence or closes the app; one passed through
/// here leaves a line in the log, and, for something the person did, a note in the corner.
/// </summary>
public static class Background
{
    /// <summary>
    /// Runs the task and logs a failure under <paramref name="what"/>. Cancellation is not a failure. With
    /// <paramref name="tell"/>, a failure also shows a note, for work the person started by clicking or dropping
    /// something; background checks leave it off so a missing internet connection doesn't nag every few minutes.
    /// </summary>
    public static async void Forget(this Task task, string what, bool tell = false)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (tell)
                Report(what, e);
            else
                AppLog.Write(what + " failed", e);
        }
    }

    private static string? lastNote;
    private static DateTimeOffset lastNoteAt;

    /// <summary>
    /// Writes a problem to the log and says so in a note, so the app carries on instead of closing. The same
    /// problem twice within a few seconds shows one note, so a loop of errors can't bury the screen.
    /// </summary>
    public static void Report(string what, Exception error)
    {
        AppLog.Write(what + " failed", error);
        var text = $"{error.Message} Webignite carried on; the details are in the log.";
        var now = DateTimeOffset.Now;
        if (text == lastNote && now - lastNoteAt < TimeSpan.FromSeconds(10))
            return;
        lastNote = text;
        lastNoteAt = now;
        Dispatcher.UIThread.Post(() => App.Services?.ShowNote?.Invoke($"{what} didn't work", text, null));
    }

    /// <summary>
    /// The last line of defence on the screen's thread: an error that reaches it is logged and shown, and the app
    /// keeps running. Errors from command buttons, timers and event handlers all end up here if nothing caught them.
    /// </summary>
    public static void CatchUnhandled() => Dispatcher.UIThread.UnhandledException += (_, e) =>
    {
        Report("Something", e.Exception);
        e.Handled = true;
    };
}
