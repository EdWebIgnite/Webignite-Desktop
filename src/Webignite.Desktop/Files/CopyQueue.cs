using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;

namespace Webignite.Desktop.Files;

public enum CopyState
{
    Waiting,
    Running,
    Paused,
    Done,
    Stopped,
    Failed,
}

/// <summary>
/// Big copies and moves (folders, or more than 50 MB), one at a time, off the screen's thread, so Files stays
/// usable meanwhile. Every Files tab and window shows the same queue. A job can be paused and carried on, or
/// stopped: a stopped copy keeps what was already copied; a stopped move leaves the originals where they were.
/// </summary>
public sealed class CopyQueue(Func<FileTagBook> tags)
{
    private readonly Queue<CopyJob> waiting = new();
    private bool running;

    /// <summary>The jobs to show: running, waiting, and stopped or failed ones until they are put away.</summary>
    public ObservableCollection<CopyJob> Jobs { get; } = [];

    public CopyJob Add(IReadOnlyList<string> paths, string into, bool move)
    {
        var job = new CopyJob(paths, into, move, this);
        Jobs.Add(job);
        waiting.Enqueue(job);
        if (!running)
            RunNext().Forget("Copying files");
        return job;
    }

    internal void Dismiss(CopyJob job)
    {
        Jobs.Remove(job);
        job.Dispose();
    }

    private async Task RunNext()
    {
        running = true;
        try
        {
            while (waiting.TryDequeue(out var job))
            {
                if (job.State == CopyState.Stopped)
                    continue;
                await job.Run();
                // Tags go with what was copied or moved, whichever tab started it and whether it is still open.
                foreach (var (from, to) in job.Pairs)
                {
                    if (job.Move)
                        tags().Moved(from, to);
                    else
                        tags().Copied(from, to);
                }
                if (job.State == CopyState.Done)
                    Dismiss(job);
            }
        }
        finally
        {
            running = false;
        }
    }
}

public sealed partial class CopyJob : ObservableObject, IDisposable
{
    private readonly CopyQueue queue;
    private readonly ManualResetEventSlim carryOn = new(true);
    private readonly CancellationTokenSource stop = new();
    private readonly List<(string From, string To)> pairs = [];
    private DateTime started;
    private TimeSpan pausedFor;
    private DateTime pausedAt;

    internal CopyJob(IReadOnlyList<string> paths, string into, bool move, CopyQueue queue)
    {
        Paths = paths;
        Into = into;
        Move = move;
        this.queue = queue;
    }

    public IReadOnlyList<string> Paths { get; }
    public string Into { get; }
    public bool Move { get; }

    /// <summary>Each item copied or moved and where it went, once the job has finished.</summary>
    public IReadOnlyList<(string From, string To)> Pairs => pairs;

    /// <summary>Raised on the screen's thread when the job has finished, stopped or failed.</summary>
    public event EventHandler? Finished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaused), nameof(IsActive), nameof(IsOver), nameof(Heading))]
    public partial CopyState State { get; private set; } = CopyState.Waiting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fraction), nameof(Detail))]
    public partial long BytesDone { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fraction), nameof(Detail))]
    public partial long BytesTotal { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail))]
    public partial int FilesDone { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail))]
    public partial int FilesTotal { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail))]
    public partial string Problem { get; private set; } = "";

    public bool IsPaused => State == CopyState.Paused;
    public bool IsActive => State is CopyState.Running or CopyState.Paused or CopyState.Waiting;
    public bool IsOver => !IsActive;
    public double Fraction => BytesTotal == 0 ? 0 : (double)BytesDone / BytesTotal;

    public string Heading => (Move ? "Moving " : "Copying ") + Describe() + " to " + Path.GetFileName(Into.TrimEnd('\\', '/')) + State switch
    {
        CopyState.Waiting => ", waiting",
        CopyState.Paused => ", paused",
        CopyState.Stopped => ", stopped",
        CopyState.Failed => ", stopped by a problem",
        _ => "",
    };

    public string Detail => State switch
    {
        CopyState.Failed => Problem,
        CopyState.Stopped => Move ? "The originals are where they were." : $"{FilesDone} of {FilesTotal} files were copied before it stopped.",
        _ when FilesTotal == 0 => "Getting ready…",
        _ => $"{FilesDone} of {FilesTotal} files, {FileSizes.Format(BytesDone)} of {FileSizes.Format(BytesTotal)}"
            + (CopyProgress.TimeLeft(BytesDone, BytesTotal, Elapsed()) is { } left ? ". " + left : ""),
    };

    private string Describe() => Paths.Count == 1 ? Path.GetFileName(Paths[0].TrimEnd('\\', '/')) : $"{Paths.Count} items";

    private TimeSpan Elapsed() => started == default ? TimeSpan.Zero : DateTime.UtcNow - started - pausedFor;

    [RelayCommand]
    private void Pause()
    {
        if (State != CopyState.Running)
            return;
        carryOn.Reset();
        pausedAt = DateTime.UtcNow;
        State = CopyState.Paused;
    }

    [RelayCommand]
    private void Resume()
    {
        if (State != CopyState.Paused)
            return;
        pausedFor += DateTime.UtcNow - pausedAt;
        State = CopyState.Running;
        carryOn.Set();
    }

    [RelayCommand]
    private void Stop()
    {
        if (!IsActive)
            return;
        stop.Cancel();
        carryOn.Set();
        if (State == CopyState.Waiting)
            Finish(CopyState.Stopped);
    }

    [RelayCommand]
    private void Dismiss() => queue.Dismiss(this);

    internal async Task Run()
    {
        State = CopyState.Running;
        started = DateTime.UtcNow;
        try
        {
            await Task.Run(Work);
            Finish(stop.IsCancellationRequested ? CopyState.Stopped : CopyState.Done);
        }
        catch (OperationCanceledException)
        {
            Finish(CopyState.Stopped);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Problem = e.Message;
            Finish(CopyState.Failed);
        }
    }

    private void Finish(CopyState state)
    {
        State = state;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    // Works out every file to copy and where it goes, then copies them in pieces so Pause and Stop answer quickly.
    private void Work()
    {
        var plan = new List<(string From, string To, long Size)>();
        var folders = new List<string>();
        var tops = new List<(string From, string To)>();
        var names = Directory.EnumerateFileSystemEntries(Into).Select(Path.GetFileName).OfType<string>().ToList();
        foreach (var path in Paths)
        {
            var isFolder = Directory.Exists(path);
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            var sameFolder = FileTags.Same(Path.GetDirectoryName(path.TrimEnd('\\', '/')) ?? "", Into);
            if (Move && sameFolder)
                continue;
            if (isFolder && (FileTags.Same(Into, path) || Into.StartsWith(path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new IOException($"{name} can't go into itself.");
            var target = Path.Combine(Into, sameFolder && !Move ? FileNames.CopyOf(name, isFolder, names) : FileNames.Unique(name, isFolder, names));
            names.Add(Path.GetFileName(target));
            tops.Add((path, target));
            if (isFolder)
            {
                folders.Add(target);
                foreach (var folder in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                    folders.Add(Path.Combine(target, Path.GetRelativePath(path, folder)));
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    plan.Add((file, Path.Combine(target, Path.GetRelativePath(path, file)), new FileInfo(file).Length));
            }
            else
            {
                plan.Add((path, target, new FileInfo(path).Length));
            }
        }
        var total = plan.Sum(p => p.Size);
        Post(() =>
        {
            BytesTotal = total;
            FilesTotal = plan.Count;
        });

        var made = new List<string>();
        try
        {
            foreach (var folder in folders)
                Directory.CreateDirectory(folder);
            long done = 0;
            var lastShown = 0L;
            var buffer = new byte[1024 * 1024];
            var count = 0;
            foreach (var (from, to, _) in plan)
            {
                using (var source = File.OpenRead(from))
                using (var target = new FileStream(to, FileMode.CreateNew, FileAccess.Write))
                {
                    made.Add(to);
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        carryOn.Wait(stop.Token);
                        stop.Token.ThrowIfCancellationRequested();
                        target.Write(buffer, 0, read);
                        done += read;
                        if (Environment.TickCount64 - lastShown > 200)
                        {
                            lastShown = Environment.TickCount64;
                            var now = done;
                            Post(() => BytesDone = now);
                        }
                    }
                }
                File.SetLastWriteTime(to, File.GetLastWriteTime(from));
                count++;
                var files = count;
                var bytes = done;
                Post(() =>
                {
                    FilesDone = files;
                    BytesDone = bytes;
                });
            }
        }
        catch (OperationCanceledException)
        {
            // A stopped move takes its copies away again, so nothing is left twice. A stopped copy keeps whole
            // files and drops only the one it was part way through.
            if (made.Count > 0)
                TryDelete(made[^1]);
            if (Move)
                foreach (var (_, to) in tops)
                    TryDelete(to);
            throw;
        }
        if (Move)
            foreach (var (from, _) in tops)
            {
                if (Directory.Exists(from))
                    Directory.Delete(from, recursive: true);
                else
                    File.Delete(from);
            }
        lock (pairs)
            pairs.AddRange(tops);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Webignite.Data.AppLog.Write("Tidying after a stopped copy", e);
        }
    }

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);

    /// <summary>Put away once over: lets go of what Pause and Stop wait on.</summary>
    public void Dispose()
    {
        carryOn.Dispose();
        stop.Dispose();
    }
}
