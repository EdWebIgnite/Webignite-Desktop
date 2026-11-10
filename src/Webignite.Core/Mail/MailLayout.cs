namespace Webignite.Core.Mail;

/// <summary>
/// A designed frame around a mail's typed words, such as the branded layout for quotes and invoices: the HTML with
/// <see cref="BodyMarker"/> where the words go, text added under the plain-text version, and its pictures (the logo).
/// It's put on as the mail goes, so the person writes only the words.
/// </summary>
public sealed record MailLayout(string Html, string TextAfter, IReadOnlyList<InlineImage> Images)
{
    public const string BodyMarker = "<!--webignite-body-->";
}
