using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// Attachments, as Asana's: files on a task (kept on this PC), a picture as the card's cover on the board, and a
// Files tab with every file in the project. Pictures are shrunk to small copies, made off the screen's thread and
// kept while the page is open; closing the page lets them go.
public sealed partial class ProjectFilesPart(AppServices services, ProjectsViewModel page) : ViewModelBase
{
    private readonly Dictionary<(string Path, int Width), Bitmap> thumbnails = [];

    /// <summary>The Files tab: every file on the open project's tasks, the newest first.</summary>
    public ObservableCollection<AttachmentRow> ProjectFiles { get; } = [];

    internal string PathOf(ProjectTask task, TaskAttachment attachment) => services.Projects.PathOf(task.Id, attachment);

    internal AttachmentRow CoverRow(ProjectTask task, TaskAttachment picture) => new(task, picture, page, 252);

    internal void ListFiles()
    {
        ProjectFiles.Clear();
        if (page.open is not null)
        {
            foreach (var file in Attachments.Of(services.Projects.AllTasks(page.open.Id)))
                ProjectFiles.Add(new AttachmentRow(file.Task, file.Attachment, page, 168));
        }
        OnPropertyChanged(nameof(page.NoProjectFiles));
    }

    /// <summary>Attaches files to a task, as picked or dropped on it; says which couldn't be.</summary>
    public void Attach(string taskId, IEnumerable<string> paths)
    {
        var problems = new List<string>();
        foreach (var path in paths)
        {
            var (_, problem) = page.Ours(() => services.Projects.Attach(taskId, path));
            if (problem is not null)
                problems.Add(problem);
        }
        page.Message = string.Join(" ", problems);
        AfterFilesChanged(taskId);
    }

    /// <summary>Files dropped on a task's row or card: attached to it, saying so, as the task may not be open.</summary>
    public void AttachTo(TaskRow row, IReadOnlyList<string> paths)
    {
        Attach(row.Task.Id, paths);
        if (page.Message.Length == 0 && paths.Count > 0)
            page.Message = paths.Count == 1 ? $"Attached {Path.GetFileName(paths[0])} to {row.Task.Title}." : $"Attached {paths.Count} files to {row.Task.Title}.";
    }

    internal void Detach(AttachmentRow row)
    {
        page.Ours(() => services.Projects.Detach(row.Task.Id, row.Attachment.Id));
        AfterFilesChanged(row.Task.Id);
    }

    internal void SetCover(AttachmentRow row, bool cover)
    {
        page.Ours(() => services.Projects.ChangeTask(row.Task.Id, t => Attachments.SetCover(t, cover ? row.Attachment.Id : null)));
        AfterFilesChanged(row.Task.Id);
    }

    private void AfterFilesChanged(string taskId)
    {
        page.ShowTasks();
        if (page.Detail is { } detail && detail.Task.Id == taskId)
            detail.Reload();
        if (page.IsFiles)
            ListFiles();
    }

    /// <summary>A small copy of a picture, if already made.</summary>
    internal Bitmap? Thumbnail(string path, int width) => thumbnails.GetValueOrDefault((path, width));

    /// <summary>Makes a small copy of a picture off the screen's thread, then hands it over; null when it can't be read.</summary>
    internal async Task<Bitmap?> ThumbnailAsync(string path, int width)
    {
        if (thumbnails.TryGetValue((path, width), out var made))
            return made;
        var bitmap = await Task.Run(() =>
        {
            try
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.MediumQuality);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                Webignite.Data.AppLog.Write("Making a small copy of an attached picture", e);
                return null;
            }
        });
        if (bitmap is null)
            return null;
        if (disposed)
        {
            bitmap.Dispose();
            return null;
        }
        if (thumbnails.TryGetValue((path, width), out var raced))
        {
            bitmap.Dispose();
            return raced;
        }
        thumbnails[(path, width)] = bitmap;
        return bitmap;
    }

    private bool disposed;

    internal void LetPicturesGo()
    {
        disposed = true;
        ProjectFiles.Clear();
        foreach (var bitmap in thumbnails.Values)
            bitmap.Dispose();
        thumbnails.Clear();
    }
}

/// <summary>A file on a task: in the task pane, the Files tab, or as a card's cover.</summary>
public sealed partial class AttachmentRow : ObservableObject
{
    private readonly ProjectsViewModel page;

    public AttachmentRow(ProjectTask task, TaskAttachment attachment, ProjectsViewModel page, int pictureWidth)
    {
        this.page = page;
        Task = task;
        Attachment = attachment;
        Path = page.FilesPart.PathOf(task, attachment);
        if (attachment.IsPicture)
        {
            Picture = page.FilesPart.Thumbnail(Path, pictureWidth);
            if (Picture is null)
                LoadPicture(pictureWidth).Forget("Showing an attached picture");
        }
    }

    public ProjectTask Task { get; }
    public TaskAttachment Attachment { get; }
    public string Path { get; }
    public string Name => Attachment.FileName;
    public string TaskTitle => Task.Title;
    public string About => Arrived
        ? $"{FileKinds.Of(Attachment.FileName, false)} · {FileSizes.Format(Attachment.Size)}"
        : Webignite.Core.Projects.AttachmentParts.Travels(Attachment) ? "On its way…" : "Too big to sync, on the other PC only";

    /// <summary>The file is on this PC: attached here, or come over from the other PC with sync.</summary>
    public bool Arrived => File.Exists(Path);
    public bool IsPicture => Attachment.IsPicture;
    public bool IsCover => Task.CoverId == Attachment.Id;
    public bool CanBeCover => IsPicture && !IsCover;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicture), nameof(ShowsIcon))]
    public partial Bitmap? Picture { get; private set; }

    public bool HasPicture => Picture is not null;
    public bool ShowsIcon => Picture is null;

    private async Task LoadPicture(int width) => Picture = await page.FilesPart.ThumbnailAsync(Path, width);

    [RelayCommand]
    private void Open()
    {
        if (!File.Exists(Path))
            page.Tell(Webignite.Core.Projects.AttachmentParts.Travels(Attachment)
                ? $"{Name} is still on its way from the other PC. It comes with the next sync, within 15 minutes."
                : $"{Name} is bigger than 25 MB, so it stays on the PC it was attached on.");
        // A program or script (from a mail, say) is never started from here, as with mail attachments: it's shown in
        // its folder, where Windows asks before running it.
        else if (Webignite.Core.Mail.AttachmentPreview.IsProgram(Name))
        {
            page.Tell($"{Name} is a program, so it isn't opened from here. It's shown in its folder.");
            page.ShowFile(Path);
        }
        else
            Webignite.Desktop.Files.FileOps.Open(Path);
    }

    [RelayCommand]
    private void ShowInFolder() => page.ShowFile(Path);

    [RelayCommand]
    private void OpenTask() => page.OpenTaskById(Task.Id);

    [RelayCommand]
    private void MakeCover() => page.FilesPart.SetCover(this, true);

    [RelayCommand]
    private void RemoveCover() => page.FilesPart.SetCover(this, false);

    [RelayCommand]
    private void Remove() => page.FilesPart.Detach(this);
}
