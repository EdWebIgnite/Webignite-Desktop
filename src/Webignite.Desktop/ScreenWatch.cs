using System.Diagnostics;
using Avalonia.Threading;
using Webignite.Data;

namespace Webignite.Desktop;

/// <summary>
/// Notices when the screen stops answering. Twice a second a timer off the screen's thread asks that thread for a
/// moment; when the answer takes over half a second, a click or a right-click made meanwhile went nowhere, and a
/// line goes into the log with how long it was and what was on screen. It does nothing else: the log (Settings, If
/// something goes wrong) is how such freezes get traced to what caused them.
/// </summary>
public sealed class ScreenWatch : IDisposable
{
    private static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan BetweenLines = TimeSpan.FromSeconds(5);

    private readonly Func<string> onScreen;
    private readonly Timer timer;
    private long asked;
    private long lastLine;
    private int waiting;

    /// <param name="onScreen">Says what is on screen, e.g. the page's name. Called on the screen's thread.</param>
    public ScreenWatch(Func<string> onScreen)
    {
        this.onScreen = onScreen;
        timer = new Timer(_ => Ask(), null, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(500));
    }

    private void Ask()
    {
        // One question at a time: while the screen is busy no more are queued behind it.
        if (Interlocked.Exchange(ref waiting, 1) == 1)
            return;
        asked = Stopwatch.GetTimestamp();
        Dispatcher.UIThread.Post(Answered, DispatcherPriority.Input);
    }

    private void Answered()
    {
        var took = Stopwatch.GetElapsedTime(asked);
        Volatile.Write(ref waiting, 0);
        if (took < Slow)
            return;
        var now = Stopwatch.GetTimestamp();
        if (lastLine != 0 && Stopwatch.GetElapsedTime(lastLine, now) < BetweenLines)
            return;
        lastLine = now;
        string where;
        try
        {
            where = onScreen();
        }
        catch (InvalidOperationException)
        {
            where = "";
        }
        AppLog.Write(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"The screen did not answer for {took.TotalMilliseconds:0} ms{(where.Length > 0 ? " (on " + where + ")" : "")}"));
    }

    public void Dispose() => timer.Dispose();
}
