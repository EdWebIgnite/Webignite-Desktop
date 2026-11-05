using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;
using Webignite.Core.Tools;
using Webignite.Desktop.Files;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// PDF tools: open PDFs and pictures, then put the pages in order, turn them, take some out, fill in and sign,
/// and save. Also: take pages out into a file of their own, split, make a smaller copy, save the pages as
/// pictures. The files opened are only changed by Save, and the old one goes to the Recycle Bin.
/// </summary>
public sealed partial class PdfViewModel : ViewModelBase, IHoldsWork, IDisposable, ITabTitle
{
    private readonly AppServices services;
    private readonly List<PdfSource> sources = [];
    private readonly List<PdfStamp> stamps = [];
    private int drawVersion;

    public PdfViewModel(AppServices services) => this.services = services;

    public ObservableCollection<PdfPageRow> Pages { get; } = [];

    /// <summary>The pages picked in the view, kept in step by the view.</summary>
    public IReadOnlyList<PdfPageRow> Picked { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoldsWork))]
    public partial bool Changed { get; private set; }

    [ObservableProperty] public partial string Message { get; private set; } = "";
    [ObservableProperty] public partial bool MessageIsProblem { get; private set; }
    [ObservableProperty] public partial bool Working { get; private set; }
    [ObservableProperty] public partial string RangeText { get; set; } = "";

    public bool HoldsWork => Changed;
    public bool HasPages => Pages.Count > 0;
    public bool IsEmpty => Pages.Count == 0;
    public string? TabTitle => sources.Count == 0 ? null : sources[0].Name;
    public string Title => sources.Count switch
    {
        0 => "PDF tools",
        1 => sources[0].Name,
        _ => $"{sources[0].Name} and {sources.Count - 1} more",
    };

    public string PageCountText => Pages.Count == 1 ? "1 page" : $"{Pages.Count} pages";

    /// <summary>The first file, which Save writes over; a file made from a mail's attachment is saved somewhere new.</summary>
    public PdfSource? Main => sources.FirstOrDefault();

    public bool CanSaveInPlace => Main is { IsTemporary: false, IsPdf: true };

    private IReadOnlyList<PdfPage> Plan => [.. Pages.Select(p => p.Page)];

    // Opening

    /// <summary>Opens PDFs and pictures; with pages already here, they go on the end.</summary>
    public async Task Open(IReadOnlyList<string> paths)
    {
        await Busy("Couldn't open it", async () =>
        {
            foreach (var path in paths)
            {
                var isPdf = FileKinds.IsPdf(path);
                if (!isPdf && !QuickActions.CanRotate(path))
                {
                    Say($"{Path.GetFileName(path)} isn't a PDF or a picture.", problem: true);
                    continue;
                }
                var data = await Task.Run(() => isPdf ? File.ReadAllBytes(path) : PictureAsPdf(path));
                var count = await Task.Run(() => PageCount(data));
                // A mail's attachment opened here is kept in the app's own temporary folder until saved somewhere.
                var temporary = path.StartsWith(Path.Combine(Path.GetTempPath(), "Webignite PDFs"), StringComparison.OrdinalIgnoreCase);
                var source = new PdfSource(path, data, isPdf, temporary);
                sources.Add(source);
                for (var i = 0; i < count; i++)
                    Pages.Add(new PdfPageRow(new PdfPage(sources.Count - 1, i), source.Name));
                Draw(source, sources.Count - 1).Forget("Drawing the pages");
            }
            // Two files together, or pictures, are a new PDF until saved.
            if (sources.Count > 1 || sources.Any(s => !s.IsPdf))
                Changed = true;
            Renumber();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(TabTitle));
            OnPropertyChanged(nameof(CanSaveInPlace));
        });
    }

    // Methods of their own, so the PDF library is only read from disk when a PDF is opened.
    private static byte[] PictureAsPdf(string path) => Webignite.Tools.PdfMaker.PictureAsPdf(path);

    private static int PageCount(byte[] data) => Webignite.Tools.PdfText.Pages(data);

    private static (IReadOnlyList<Bitmap> Pages, int Count) Render(byte[] data, int width, int most) => Webignite.Tools.PdfPages.Render(data, width, most);

    // Small pictures of a file's pages, drawn off the screen's thread and handed to their rows.
    private async Task Draw(PdfSource source, int index)
    {
        var version = drawVersion;
        var (drawn, _) = await Task.Run(() => Render(source.Data, 170, 400));
        if (version != drawVersion)
        {
            foreach (var bitmap in drawn)
                bitmap.Dispose();
            return;
        }
        for (var i = 0; i < drawn.Count; i++)
        {
            var row = Pages.FirstOrDefault(r => r.Page.Source == index && r.Page.Page == i && r.Thumbnail is null);
            if (row is null)
                drawn[i].Dispose();
            else
                row.Thumbnail = drawn[i];
        }
    }

    private void Renumber()
    {
        for (var i = 0; i < Pages.Count; i++)
            Pages[i].Number = i + 1;
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(PageCountText));
    }

    // Arranging

    private HashSet<int> PickedIndexes() => [.. Picked.Select(p => Pages.IndexOf(p)).Where(i => i >= 0)];

    [RelayCommand]
    private void TurnLeft() => Turn(-1);

    [RelayCommand]
    private void TurnRight() => Turn(1);

    private void Turn(int quarter)
    {
        var picked = PickedIndexes();
        if (picked.Count == 0)
        {
            Say("Pick the pages to turn first.", problem: false);
            return;
        }
        var turned = PdfPlan.Turn(Plan, picked, quarter);
        foreach (var i in picked)
            Pages[i].Page = turned[i];
        Changed = true;
    }

    [RelayCommand]
    private void Remove()
    {
        var picked = PickedIndexes();
        if (picked.Count == 0 || picked.Count == Pages.Count)
        {
            Say(picked.Count == 0 ? "Pick the pages to take out first." : "A PDF needs at least one page.", problem: false);
            return;
        }
        foreach (var i in picked.OrderDescending())
        {
            Pages[i].Free();
            Pages.RemoveAt(i);
            stamps.RemoveAll(s => s.Page == i);
        }
        Changed = true;
        Renumber();
        Say(picked.Count == 1 ? "Took out 1 page." : $"Took out {picked.Count} pages.", problem: false);
    }

    /// <summary>The picked pages dragged to land before the page at <paramref name="before"/>.</summary>
    public void MovePicked(int before)
    {
        var picked = PickedIndexes();
        if (picked.Count == 0)
            return;
        var rows = Pages.ToList();
        var moving = rows.Where((_, i) => picked.Contains(i)).ToList();
        var staying = rows.Where((_, i) => !picked.Contains(i)).ToList();
        var at = Math.Clamp(before - picked.Count(i => i < before), 0, staying.Count);
        staying.InsertRange(at, moving);
        Pages.Clear();
        foreach (var row in staying)
            Pages.Add(row);
        stamps.Clear(); // placed by page number, which just changed
        Changed = true;
        Renumber();
    }

    // Saving

    /// <summary>Writes over the first file; the old one goes to the Recycle Bin, so it can be got back.</summary>
    [RelayCommand]
    private Task Save()
    {
        if (Main is not { } main || !CanSaveInPlace)
        {
            Say("Use Save as to pick where it goes.", problem: false);
            return Task.CompletedTask;
        }
        return Busy("Couldn't save it", async () =>
        {
            var built = await Build(Plan, stamps);
            var path = main.Path;
            FileOps.Recycle([path]);
            await File.WriteAllBytesAsync(path, built);
            Saved($"Saved {main.Name}. The old copy is in the Recycle Bin.");
        });
    }

    public Task SaveAs(string path) => Busy("Couldn't save it", async () =>
    {
        await File.WriteAllBytesAsync(path, await Build(Plan, stamps));
        Saved($"Saved {Path.GetFileName(path)} in {Path.GetDirectoryName(path)}.");
    });

    private void Saved(string message)
    {
        Changed = false;
        Say(message, problem: false);
    }

    /// <summary>The picked pages, or those typed as "1-3, 5", into a PDF of their own beside the first file.</summary>
    [RelayCommand]
    private Task TakeOut() => Busy("Couldn't make the new PDF", async () =>
    {
        IReadOnlyList<int> chosen;
        if (RangeText.Trim().Length > 0)
        {
            var (range, problem) = PdfPlan.Range(RangeText, Pages.Count);
            if (range is null)
            {
                Say(problem!, problem: true);
                return;
            }
            chosen = range;
        }
        else
        {
            chosen = [.. PickedIndexes().Order()];
        }
        if (chosen.Count == 0)
        {
            Say("Pick the pages, or type them, e.g. 1-3, 5.", problem: false);
            return;
        }
        var path = Beside(PdfPlan.PartName(Main!.Name, chosen));
        await File.WriteAllBytesAsync(path, await Build([.. chosen.Select(i => Pages[i].Page)], []));
        Say($"Made {Path.GetFileName(path)}.", problem: false);
    });

    /// <summary>Every page, or every few pages, into files of their own.</summary>
    public Task Split(int every) => Busy("Couldn't split it", async () =>
    {
        var groups = PdfPlan.Every(Pages.Count, every);
        foreach (var group in groups)
        {
            var path = Beside(PdfPlan.PartName(Main!.Name, group));
            await File.WriteAllBytesAsync(path, await Build([.. group.Select(i => Pages[i].Page)], []));
        }
        Say($"Made {groups.Count} PDFs beside {Main!.Name}.", problem: false);
    });

    /// <summary>A smaller copy beside the first file, kept only when it is smaller.</summary>
    [RelayCommand]
    private Task MakeSmaller() => Busy("Couldn't make it smaller", async () =>
    {
        var built = await Build(Plan, stamps);
        var smaller = await Task.Run(() => Smaller(built));
        if (smaller.Length >= built.Length)
        {
            Say("It's already about as small as it gets; nothing was made.", problem: false);
            return;
        }
        var path = Beside(Path.GetFileNameWithoutExtension(Main!.Name) + " (smaller).pdf");
        await File.WriteAllBytesAsync(path, smaller);
        Say($"Made {Path.GetFileName(path)}: {FileSizes.Format(smaller.Length)} instead of {FileSizes.Format(built.Length)}. Its text can't be picked or searched.", problem: false);
    });

    /// <summary>Each page as a PNG picture, in a folder beside the first file.</summary>
    [RelayCommand]
    private Task SavePictures() => Busy("Couldn't save the pictures", async () =>
    {
        var built = await Build(Plan, stamps);
        var pictures = await Task.Run(() => Pictures(built));
        var folder = Beside(Path.GetFileNameWithoutExtension(Main!.Name) + " pages");
        Directory.CreateDirectory(folder);
        for (var i = 0; i < pictures.Count; i++)
            await File.WriteAllBytesAsync(Path.Combine(folder, $"Page {i + 1}.png"), pictures[i]);
        Say($"Saved {pictures.Count} pictures in {folder}.", problem: false);
    });

    [RelayCommand]
    private void ShowInFiles()
    {
        if (Main is { IsTemporary: false } main && Path.GetDirectoryName(main.Path) is { } folder)
            services.Go.Folder(folder);
    }

    private Task<byte[]> Build(IReadOnlyList<PdfPage> pages, IReadOnlyList<PdfStamp> onPages)
    {
        var files = sources.Select(s => s.Data).ToList();
        var placed = onPages.ToList();
        return Task.Run(() => BuildNow(files, pages, placed));
    }

    private static byte[] BuildNow(List<byte[]> files, IReadOnlyList<PdfPage> pages, List<PdfStamp> stamps) => Webignite.Tools.PdfEdit.Build(files, pages, stamps);

    private static byte[] Smaller(byte[] pdf) => Webignite.Tools.PdfEdit.Smaller(pdf);

    private static IReadOnlyList<byte[]> Pictures(byte[] pdf) => Webignite.Tools.PdfEdit.Pictures(pdf);

    // A free name beside the first file (or in Documents for a mail's attachment).
    private string Beside(string name)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Main is { IsTemporary: false } main ? Path.GetDirectoryName(main.Path)!
            : documents.Length > 0 ? documents : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var taken = Directory.Exists(folder) ? Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>() : [];
        return Path.Combine(folder, FileNames.Unique(name, !name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase), taken));
    }

    // Fill in and sign

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilling))]
    public partial PdfFillViewModel? Filling { get; private set; }

    public bool IsFilling => Filling is not null;

    /// <summary>The picked page, large, to put words and a signature on.</summary>
    [RelayCommand]
    private Task FillIn()
    {
        var index = PickedIndexes().Order().FirstOrDefault(-1);
        if (index < 0)
            index = 0;
        if (Pages.Count == 0)
            return Task.CompletedTask;
        return Busy("Couldn't show the page", async () =>
        {
            var single = await Build([Pages[index].Page], []);
            var (drawn, _) = await Task.Run(() => Render(single, 900, 1));
            Filling?.Free();
            Filling = new PdfFillViewModel(index, drawn[0], [.. stamps.Where(s => s.Page == index)], services.Settings.Signature, this);
        });
    }

    internal void FinishFilling(int page, IReadOnlyList<PdfStamp> placed)
    {
        stamps.RemoveAll(s => s.Page == page);
        stamps.AddRange(placed);
        if (placed.Count > 0)
            Changed = true;
        Filling?.Free();
        Filling = null;
        Say(placed.Count == 0 ? "" : $"Page {page + 1} is filled in. Save to keep it.", problem: false);
    }

    internal void KeepSignature(byte[] png) => services.Settings.Signature = png;

    // Common

    private async Task Busy(string problem, Func<Task> work)
    {
        Working = true;
        try
        {
            await work();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or DllNotFoundException or ArgumentException)
        {
            Say($"{problem}: {e.Message}", problem: true);
        }
        finally
        {
            Working = false;
        }
    }

    public void Say(string message, bool problem)
    {
        Message = message;
        MessageIsProblem = problem;
    }

    public void Dispose()
    {
        drawVersion++;
        foreach (var row in Pages)
            row.Free();
        Pages.Clear();
        Filling?.Free();
        Filling = null;
        sources.Clear();
    }
}

/// <summary>A file opened in the PDF tools.</summary>
public sealed record PdfSource(string Path, byte[] Data, bool IsPdf, bool IsTemporary)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>One page in the PDF tools: its small picture, number, file and turn.</summary>
public sealed partial class PdfPageRow(PdfPage page, string from) : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Angle))]
    public partial PdfPage Page { get; set; } = page;

    [ObservableProperty] public partial Bitmap? Thumbnail { get; set; }
    [ObservableProperty] public partial int Number { get; set; }

    public string From => from;

    /// <summary>The turn shown on the small picture.</summary>
    public double Angle => Page.Turns * 90;

    public void Free()
    {
        var thumbnail = Thumbnail;
        Thumbnail = null;
        if (thumbnail is not null)
            Dispatcher.UIThread.Post(thumbnail.Dispose, DispatcherPriority.Background);
    }
}
