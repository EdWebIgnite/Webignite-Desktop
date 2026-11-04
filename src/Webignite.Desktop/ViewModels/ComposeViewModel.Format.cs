using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Writing;

namespace Webignite.Desktop.ViewModels;

/// <summary>The bits of the format toolbar that need asking: a link's address. The rest acts on the editor directly.</summary>
public partial class ComposeViewModel
{
    [ObservableProperty] public partial bool AskingForLink { get; set; }
    [ObservableProperty] public partial string LinkAddress { get; set; } = "";

    /// <summary>The text colours offered, as in Thunderbird's small set. Sent in the mail as they are.</summary>
    public static IReadOnlyList<string> TextColours => MailColours.Text;

    public static IReadOnlyList<string> HighlightColours => MailColours.Highlight;

    public static IReadOnlyList<TextSize> Sizes => TextSizes.All;
}
