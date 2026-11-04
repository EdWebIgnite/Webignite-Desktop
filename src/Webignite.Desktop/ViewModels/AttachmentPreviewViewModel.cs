using Webignite.Tools;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>A PDF or picture shown inside the app, page by page: a mail's attachment, or a file in Files.</summary>
public partial class AttachmentPreviewViewModel : ViewModelBase
{
    private readonly Action open;
    private readonly Action? save;
    private readonly Action close;

    public AttachmentPreviewViewModel(AttachmentViewModel attachment, Action close)
        : this(attachment.Name, () => attachment.OpenCommand.Execute(null), () => attachment.SaveCommand.Execute(null), close)
    {
    }

    public AttachmentPreviewViewModel(string name, Action open, Action? save, Action close)
    {
        Name = name;
        this.open = open;
        this.save = save;
        this.close = close;
    }

    public string Name { get; }

    /// <summary>A mail's attachment can be saved; a file in Files already is somewhere.</summary>
    public bool CanSave => save is not null;
    public System.Collections.ObjectModel.ObservableCollection<Bitmap> Pages { get; } = [];

    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial string Note { get; set; } = "";

    public bool HasNote => Note.Length > 0;
    partial void OnNoteChanged(string value) => OnPropertyChanged(nameof(HasNote));

    /// <summary>Reads the attachment and draws it. A PDF is drawn off the screen's thread, so the app keeps moving.</summary>
    public async Task Load(byte[] data, PreviewKind kind)
    {
        try
        {
            if (kind == PreviewKind.Picture)
            {
                using var stream = new MemoryStream(data);
                Keep(new Bitmap(stream));
            }
            else
            {
                var (pages, count) = await Task.Run(() => PdfPages.Render(data, 1100, AttachmentPreview.MaxPages));
                foreach (var page in pages)
                    Keep(page);
                if (count > pages.Count)
                    Note = $"Showing the first {pages.Count} of {count} pages. Open it to see the rest.";
                else if (count == 0)
                    Note = "This PDF has no pages.";
            }
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or Docnet.Core.Exceptions.DocnetException or DllNotFoundException)
        {
            Note = $"Couldn't show {Name} here ({e.Message}). Open it in another program instead.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool freed;

    // A page drawn after the preview was closed is let go at once.
    private void Keep(Bitmap page)
    {
        if (freed)
            page.Dispose();
        else
            Pages.Add(page);
    }

    /// <summary>
    /// Lets go of the drawn pages, about 7 MB each for a PDF. Called when the preview closes or the mail changes;
    /// without it every preview stayed in memory until the app closed.
    /// </summary>
    public void Free()
    {
        freed = true;
        var pages = Pages.ToList();
        Pages.Clear();
        // After the screen has let go of them, so nothing draws a freed picture.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var page in pages)
                page.Dispose();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    [RelayCommand]
    private void Open() => open();

    [RelayCommand]
    private void Save() => save?.Invoke();

    [RelayCommand]
    private void Close() => close();
}
