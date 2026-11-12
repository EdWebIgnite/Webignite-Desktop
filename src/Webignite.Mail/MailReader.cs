using MimeKit;
using Webignite.Core.Mail;

namespace Webignite.Mail;

/// <summary>Turns a raw message into what the reader shows, and gets attachments out of it.</summary>
public static class MailReader
{
    public static MimeMessage Parse(byte[] mime)
    {
        using var stream = new MemoryStream(mime);
        return MimeMessage.Load(stream);
    }

    public static MailBody Read(byte[] mime, bool allowRemoteImages)
    {
        var message = Parse(mime);
        var attachments = Attachments(message);
        var text = message.TextBody;
        var html = message.HtmlBody;

        var calendar = Calendar(message);
        var draftedBy = message.Headers[MailComposer.DraftedByHeader];
        var repliesElsewhere = RepliesElsewhere(message);
        if (html is null)
            return new MailBody
            {
                Html = MailHtml.FromText(text ?? ""),
                Text = text ?? "",
                Attachments = attachments,
                Calendar = calendar,
                DraftedBy = draftedBy,
                RepliesElsewhere = repliesElsewhere,
            };

        html = InlineImages(message, html);
        return new MailBody
        {
            Html = MailHtml.Sanitise(html, allowRemoteImages),
            Text = string.IsNullOrWhiteSpace(text) ? MailHtml.ToText(html) : text,
            Attachments = attachments,
            HasRemoteImages = MailHtml.HasRemoteImages(html),
            Calendar = calendar,
            DraftedBy = draftedBy,
            RepliesElsewhere = repliesElsewhere,
        };
    }

    /// <summary>
    /// The calendar invite in a message, as text. Outlook and Teams send it as another form of the body
    /// (text/calendar); others attach an .ics file. Null when there is none, or it is too big to be an invite.
    /// </summary>
    public static string? Calendar(MimeMessage message)
    {
        foreach (var part in message.BodyParts.OfType<MimePart>())
        {
            var isCalendar = part.ContentType.IsMimeType("text", "calendar") || part.ContentType.IsMimeType("application", "ics")
                || (part.FileName ?? "").EndsWith(".ics", StringComparison.OrdinalIgnoreCase);
            if (!isCalendar || part.Content is null)
                continue;
            if (part is TextPart text)
                return text.Text;
            using var bytes = new MemoryStream();
            part.Content.DecodeTo(bytes);
            if (bytes.Length <= 512 * 1024)
                return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
        }
        return null;
    }

    // The Reply-To addresses whose domain isn't the sender's, or null when replies go back where the mail came from.
    private static string? RepliesElsewhere(MimeMessage message)
    {
        static string Domain(string address) => address[(address.LastIndexOf('@') + 1)..].Trim().ToLowerInvariant();
        var from = message.From.Mailboxes.Select(m => Domain(m.Address)).ToHashSet();
        var elsewhere = message.ReplyTo.Mailboxes.Where(m => !from.Contains(Domain(m.Address))).Select(m => m.Address).ToList();
        return elsewhere.Count == 0 ? null : string.Join(", ", elsewhere);
    }

    /// <summary>Plain text of a message, for previews and for Claude and ChatGPT to read.</summary>
    public static string Text(MimeMessage message) =>
        message.TextBody is { Length: > 0 } text ? text
        : message.HtmlBody is { } html ? MailHtml.ToText(html)
        : "";

    public static IReadOnlyList<MailAttachment> Attachments(MimeMessage message) =>
        message.Attachments
            .Select((entity, index) => new MailAttachment(
                FileName(entity, index),
                entity.ContentType.MimeType,
                entity is MimePart part && part.Content is not null ? EstimateSize(part) : 0,
                index))
            .ToList();

    /// <summary>Writes one attachment, by its position in the list, to a stream.</summary>
    public static void SaveAttachment(byte[] mime, int index, Stream output)
    {
        var entity = Parse(mime).Attachments.ElementAt(index);
        switch (entity)
        {
            case MimePart { Content: { } content }:
                content.DecodeTo(output);
                break;
            case MessagePart { Message: { } attached }:
                attached.WriteTo(output);
                break;
        }
    }

    /// <summary>A safe file name: no folders, no characters Windows refuses.</summary>
    public static string FileName(MimeEntity entity, int index)
    {
        var name = entity switch
        {
            MimePart part => part.FileName,
            MessagePart attached => (attached.Message?.Subject ?? "message") + ".eml",
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(name))
            name = $"attachment-{index + 1}";
        name = Path.GetFileName(name.Replace('\\', '/'));
        foreach (var bad in Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']))
            name = name.Replace(bad, '_');
        return name.Trim().TrimEnd('.') is { Length: > 0 } clean ? clean : $"attachment-{index + 1}";
    }

    private static long EstimateSize(MimePart part)
    {
        using var counter = new MemoryStream();
        part.Content?.DecodeTo(counter);
        return counter.Length;
    }

    /// <summary>Pictures sent inside the mail (cid: links) are shown from the message itself.</summary>
    private static string InlineImages(MimeMessage message, string html)
    {
        foreach (var part in message.BodyParts.OfType<MimePart>())
        {
            if (string.IsNullOrEmpty(part.ContentId) || part.Content is null
                || !part.ContentType.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase))
                continue;
            using var data = new MemoryStream();
            part.Content.DecodeTo(data);
            var uri = $"data:{part.ContentType.MimeType};base64,{Convert.ToBase64String(data.ToArray())}";
            html = html.Replace("cid:" + part.ContentId, uri, StringComparison.OrdinalIgnoreCase);
        }
        return html;
    }
}
