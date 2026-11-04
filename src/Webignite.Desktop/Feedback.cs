using Avalonia.Threading;

namespace Webignite.Desktop;

/// <summary>
/// Small acknowledgements of what was just done: a row fading out before it is removed, a ticked to-do staying
/// put a moment before it moves to Done. How long each takes follows the Motion setting; with Motion off it all
/// happens at once.
/// </summary>
public static class Feedback
{
    /// <summary>How long a removed row takes to fade out. Matches the leaving animations in Styles/Motion.axaml.</summary>
    public static TimeSpan LeaveTime(Motion motion) => motion switch
    {
        Motion.Full => TimeSpan.FromMilliseconds(220),
        Motion.Reduced => TimeSpan.FromMilliseconds(120),
        _ => TimeSpan.Zero,
    };

    /// <summary>How long a ticked to-do stays where it is, so the tick can be seen, before it moves to Done.</summary>
    public static TimeSpan DoneTime(Motion motion) => motion switch
    {
        Motion.Full => TimeSpan.FromMilliseconds(650),
        Motion.Reduced => TimeSpan.FromMilliseconds(350),
        _ => TimeSpan.Zero,
    };

    /// <summary>Runs <paramref name="then"/> after <paramref name="wait"/>, or straight away when there is no wait.</summary>
    public static void After(TimeSpan wait, Action then)
    {
        if (wait <= TimeSpan.Zero)
            then();
        else
            DispatcherTimer.RunOnce(then, wait);
    }

    /// <summary>Marks a row as leaving so its fade plays, then removes it once the fade is done.</summary>
    public static void Leave(Motion motion, Action markLeaving, Action remove)
    {
        var wait = LeaveTime(motion);
        if (wait > TimeSpan.Zero)
            markLeaving();
        After(wait, remove);
    }
}
