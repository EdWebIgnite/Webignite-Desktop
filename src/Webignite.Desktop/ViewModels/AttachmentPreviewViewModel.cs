using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>A PDF or picture attachment shown inside the reading pane, page by page.</summary>
public partial class AttachmentPreviewViewModel(AttachmentViewModel attachment, Action close) : ViewModelBase
{
    public string Name => attachment.Name;
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
                Pages.Add(new Bitmap(stream));
            }
            else
            {
                var (pages, count) = await Task.Run(() => PdfPages.Render(data, 1100, AttachmentPreview.MaxPages));
                foreach (var page in pages)
                    Pages.Add(page);
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

    [RelayCommand]
    private void Open() => attachment.OpenCommand.Execute(null);

    [RelayCommand]
    private void Save() => attachment.SaveCommand.Execute(null);

    [RelayCommand]
    private void Close() => close();
}
