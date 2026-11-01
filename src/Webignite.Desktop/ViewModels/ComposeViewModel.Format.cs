using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

/// <summary>Bold, italic, lists and links, and a look at how the mail will arrive.</summary>
public partial class ComposeViewModel
{
    [ObservableProperty] public partial bool ShowPreview { get; set; }
    [ObservableProperty] public partial bool AskingForLink { get; set; }
    [ObservableProperty] public partial string LinkAddress { get; set; } = "";

    /// <summary>The typed text as the other person will see it.</summary>
    public string PreviewHtml => ShowPreview ? MailStyle.Wrap("<div style=\"font-family: Arial, sans-serif\">" + MailFormat.ToHtml(Body.TrimEnd()) + "</div>") : "";

    partial void OnShowPreviewChanged(bool value) => OnPropertyChanged(nameof(PreviewHtml));

    [RelayCommand]
    private void TogglePreview() => ShowPreview = !ShowPreview;

    /// <summary>Applies a format button to the body, given what was selected. Returns the selection to put back.</summary>
    public FormatEdit Format(string action, int start, int end)
    {
        var edit = action switch
        {
            "bold" => MailFormat.Wrap(Body, start, end, "**"),
            "italic" => MailFormat.Wrap(Body, start, end, "*"),
            "bullets" => MailFormat.List(Body, start, end, numbered: false),
            "numbers" => MailFormat.List(Body, start, end, numbered: true),
            _ => MailFormat.Link(Body, start, end, LinkAddress),
        };
        Body = edit.Text;
        AskingForLink = false;
        LinkAddress = "";
        return edit;
    }
}
