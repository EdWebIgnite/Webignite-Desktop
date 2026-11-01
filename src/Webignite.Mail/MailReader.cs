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

        var draftedBy = message.Headers[MailComposer.DraftedByHeader];
        if (html is null)
            return new MailBody
            {
                Html = MailHtml.FromText(text ?? ""),
                Text = text ?? "",
                Attachments = attachments,
                DraftedBy = draftedBy,
            };

        html = InlineImages(message, html);
        return new MailBody
        {
            Html = MailHtml.Sanitise(html, allowRemoteImages),
            Text = string.IsNullOrWhiteSpace(text) ? MailHtml.ToText(html) : text,
            Attachments = attachments,
            HasRemoteImages = MailHtml.HasRemoteImages(html),
            DraftedBy = draftedBy,
        };
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
