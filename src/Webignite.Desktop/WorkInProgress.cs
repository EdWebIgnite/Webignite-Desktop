using CommunityToolkit.Mvvm.ComponentModel;

namespace Webignite.Desktop;

/// <summary>
/// What the app is busy with in the background, e.g. "Checking mail", shown with a spinner at the
/// bottom of the sidebar so it's clear something is happening. Used from the UI thread only.
/// </summary>
public sealed partial class WorkInProgress : ObservableObject
{
    private readonly List<Job> jobs = [];

    /// <summary>The newest job still running, or empty.</summary>
    public string Text => jobs.Count == 0 ? "" : jobs[^1].What + "…";

    public bool IsWorking => jobs.Count > 0;

    /// <summary>Starts a job. Dispose the result when it's done, finished or failed.</summary>
    public IDisposable Start(string what)
    {
        var job = new Job(this, what);
        jobs.Add(job);
        Changed();
        return job;
    }

    /// <summary>Runs a job with its name showing until it ends.</summary>
    public async Task<T> Run<T>(string what, Func<Task<T>> job)
    {
        using var _ = Start(what);
        return await job();
    }

    private void End(Job job)
    {
        if (jobs.Remove(job))
            Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(IsWorking));
    }

    private sealed class Job(WorkInProgress owner, string what) : IDisposable
    {
        public string What => what;
        public void Dispose() => owner.End(this);
    }
}
