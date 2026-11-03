using Webignite.Data;

namespace Webignite.Desktop;

/// <summary>
/// For work started without waiting for it: a mail check on a timer, a backup at start-up. A task that is
/// just dropped (<c>_ = DoAsync()</c>) fails in silence; one passed through here leaves a line in the log.
/// </summary>
public static class Background
{
    /// <summary>Runs the task and logs a failure under <paramref name="what"/>. Cancellation is not a failure.</summary>
    public static async void Forget(this Task task, string what)
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
            AppLog.Write(what + " failed", e);
        }
    }
}
