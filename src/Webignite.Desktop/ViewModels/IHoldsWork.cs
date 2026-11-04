namespace Webignite.Desktop.ViewModels;

/// <summary>
/// A page that can have something open or typed in it (an editor, a mail being written). The app lets an
/// unused page go after a while to free its memory, but never while this says it holds work. A page that
/// needs to tidy up when it is let go (stop a timer, save what was typed) is also IDisposable.
/// </summary>
public interface IHoldsWork
{
    bool HoldsWork { get; }
}
