using System.IO.Pipes;
using System.Text;
using Avalonia.Threading;
using Webignite.Data;

namespace Webignite.Desktop;

/// <summary>
/// One copy of Webignite at a time, so two never write to the same data. A second start (from the Start menu, or a
/// picture double-clicked in Explorer) hands what it was asked to open to the copy already running, which comes
/// to the front, and stops. If the first copy is closing (a restart after a restore), the second waits for it.
/// </summary>
public static class SingleCopy
{
    // Per Windows user: two people signed in on one PC each get their own Webignite.
    private static readonly string Name = "Webignite.Desktop." + Environment.UserName;
    private static Mutex? held;
    private static CancellationTokenSource? listening;

    /// <summary>Whether this copy should carry on starting. False when the running copy took over.</summary>
    public static bool Claim(string[] args)
    {
        var mutex = new Mutex(initiallyOwned: true, "Local\\" + Name, out var first);
        if (!first && Send(args.FirstOrDefault(a => !a.StartsWith('-')) ?? ""))
        {
            mutex.Dispose();
            return false;
        }
        if (!first)
        {
            // Nobody answered: the other copy is closing. Wait for it, then carry on as the one copy.
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(20)))
                {
                    mutex.Dispose();
                    return false;
                }
            }
            catch (AbandonedMutexException)
            {
                // It stopped without letting go; it's ours now.
            }
        }
        held = mutex;
        return true;
    }

    private static bool Send(string opening)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            pipe.Connect(2000);
            var bytes = Encoding.UTF8.GetBytes(opening + "\n");
            pipe.Write(bytes);
            pipe.Flush();
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException)
        {
            return false;
        }
    }

    /// <summary>Listens for later starts while the app runs; each hands over what it was asked to open ("" for nothing).</summary>
    public static void Listen(Action<string> opened)
    {
        if (held is null)
            return;
        listening = new CancellationTokenSource();
        var stop = listening.Token;
        Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync(stop);
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    var line = (await reader.ReadLineAsync(stop))?.Trim() ?? "";
                    Dispatcher.UIThread.Post(() => opened(line));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException e)
                {
                    AppLog.Write("Listening for Webignite being opened again", e);
                    await Task.Delay(1000, CancellationToken.None);
                }
            }
        }, stop).Forget("Listening for Webignite being opened again");
    }

    /// <summary>Stops listening and lets the next copy start; called as the app closes.</summary>
    public static void Release()
    {
        listening?.Cancel();
        listening = null;
        try
        {
            held?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not held by this thread any more; disposing still lets it go.
        }
        held?.Dispose();
        held = null;
    }
}
