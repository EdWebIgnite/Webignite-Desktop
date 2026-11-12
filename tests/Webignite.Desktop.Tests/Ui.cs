using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Webignite.Desktop;

// Every test here shares one screen thread, as the app has one, so they run one at a time.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Webignite.Desktop.Tests;

/// <summary>
/// The app's screen thread, with no window shown. View models expect to be made and used on it, as they are in
/// the app, so a test does its work inside <see cref="Run(Action)"/>. Started once, the first time it is needed.
/// </summary>
public static class Ui
{
    private static readonly Lazy<Dispatcher> Screen = new(Start);

    private static Dispatcher Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<App>()
                    .UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .WithInterFont()
                    .SetupWithoutStarting();
                ready.SetResult(Dispatcher.UIThread);
            }
            catch (Exception problem) when (problem is InvalidOperationException or TypeInitializationException or DllNotFoundException)
            {
                ready.SetException(problem);
                return;
            }
            Dispatcher.UIThread.MainLoop(CancellationToken.None);
        })
        { IsBackground = true, Name = "Test screen thread" };
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }

    /// <summary>Does the work on the screen thread and waits for it.</summary>
    public static Task Run(Action work) => Screen.Value.InvokeAsync(work).GetTask();

    /// <summary>Does work that waits (a command that awaits) on the screen thread, to its end.</summary>
    public static Task Run(Func<Task> work) => Screen.Value.InvokeAsync(work);

    /// <summary>Lets everything already queued for the screen thread (posted notes, bindings) finish.</summary>
    public static Task Settle() => Screen.Value.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).GetTask();
}
