using MimeKit;
using MimeKit.Utils;
using Webignite.Core.Mail;

namespace Webignite.Mail;

/// <summary>Builds the message that goes out, and reads a saved draft back into something editable.</summary>
public static class MailComposer
{
    /// <summary>Header that marks the part of a draft that was typed, so a reopened draft doesn't quote the quote.</summary>
    private const string QuoteHeader = "X-Webignite-Quote";

    /// <summary>Header on drafts that Claude or ChatGPT saved, so the app can say who wrote it.</summary>
    public const string DraftedByHeader = "X-Webignite-Drafted-By";

    public static MimeMessage Build(OutgoingMail mail, MailAccount from, byte[]? source = null)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(from.DisplayName) ? null : from.DisplayName, from.Address));
        message.To.AddRange(Boxes(mail.To));
        message.Cc.AddRange(Boxes(mail.Cc));
        message.Bcc.AddRange(Boxes(mail.Bcc));
        message.ReplyTo.AddRange(Boxes(mail.ReplyTo));
        message.Subject = mail.Subject.Trim();
        message.Date = DateTimeOffset.Now;
        message.MessageId = MimeUtils.GenerateMessageId(from.Address.Split('@')[^1]);
        if (mail.InReplyTo is { Length: > 0 } inReplyTo)
            message.InReplyTo = inReplyTo;
        foreach (var reference in mail.References)
            message.References.Add(reference);
        if (mail.Quote is not null)
            message.Headers.Add(QuoteHeader, mail.Quote.IsForward ? "forward" : "reply");

        var builder = new BodyBuilder { TextBody = Replies.TextPart(mail) };
        string? imageId = null;
        foreach (var picture in mail.Images)
        {
            var part = builder.LinkedResources.Add(picture.ContentId + Extension(picture.ContentType), picture.Data, ContentType.Parse(picture.ContentType));
            part.ContentId = picture.ContentId;
        }
        // Mail written in the editor carries its signature picture in its own HTML.
        if (mail.Html is null && SignatureImages.Belongs(from, mail.Body))
        {
            var image = builder.LinkedResources.Add("signature" + (from.SignatureImageType == "image/png" ? ".png" : from.SignatureImageType == "image/gif" ? ".gif" : ".jpg"),
                from.SignatureImage!, ContentType.Parse(from.SignatureImageType ?? "image/png"));
            image.ContentId = imageId = MimeUtils.GenerateMessageId();
        }
        builder.HtmlBody = Replies.HtmlPart(mail, imageId);
        var original = source is null ? null : MailReader.Parse(source).Attachments.ToList();
        foreach (var attachment in mail.Attachments)
        {
            if (attachment.FilePath is { } path)
                builder.Attachments.Add(attachment.FileName, File.ReadAllBytes(path));
            else if (attachment.SourceIndex is { } index && original is not null && index < original.Count)
                builder.Attachments.Add(Copy(original[index]));
        }
        message.Body = builder.ToMessageBody();
        return message;
    }

    public static byte[] ToBytes(MimeMessage message)
    {
        using var stream = new MemoryStream();
        message.WriteTo(stream);
        return stream.ToArray();
    }

    /// <summary>A reply or reply all to a message, ready to type into.</summary>
    public static OutgoingMail Reply(byte[] mime, MailAccount account, IReadOnlyCollection<string> myAddresses, bool all)
    {
        var original = MailReader.Parse(mime);
        var from = MailService.People(original.From) is [var first, ..] ? first : null;
        var (to, cc) = Replies.Recipients(from, MailService.People(original.ReplyTo), MailService.People(original.To),
            MailService.People(original.Cc), myAddresses, all);
        var body = MailReader.Read(mime, allowRemoteImages: true);
        return new OutgoingMail
        {
            AccountId = account.Id,
            To = to,
            Cc = cc,
            Subject = MailText.ReplySubject(original.Subject ?? ""),
            Body = "",
            Quote = new Quoted(Replies.Header(from, original.Date), body.Text, body.Html, IsForward: false),
            InReplyTo = original.MessageId,
            References = Replies.References(original.References, original.MessageId),
        };
    }

    /// <summary>A forward, carrying the original attachments along.</summary>
    public static OutgoingMail Forward(byte[] mime, MailAccount account)
    {
        var original = MailReader.Parse(mime);
        var from = MailService.People(original.From) is [var first, ..] ? first : null;
        var body = MailReader.Read(mime, allowRemoteImages: true);
        return new OutgoingMail
        {
            AccountId = account.Id,
            Subject = MailText.ForwardSubject(original.Subject ?? ""),
            Body = "",
            Quote = new Quoted(Replies.ForwardHeader(from, MailService.People(original.To), original.Date, original.Subject ?? ""),
                body.Text, body.Html, IsForward: true),
            Attachments = body.Attachments.Select(a => new OutgoingAttachment(a.FileName, a.Size, SourceIndex: a.Index)).ToList(),
        };
    }

    private static string Extension(string type) => type switch
    {
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        _ => "",
    };

    /// <summary>
    /// Opens a saved draft for editing. Drafts made here keep the typed text apart from the quote; drafts
    /// made elsewhere (by Claude, ChatGPT or another mail program) come back as one block of text.
    /// </summary>
    public static OutgoingMail FromDraft(byte[] mime, MailAccount account, uint uid)
    {
        var draft = MailReader.Parse(mime);
        var text = MailReader.Text(draft).Replace("\r", "");
        Quoted? quote = null;
        if (draft.Headers[QuoteHeader] is { } kind)
        {
            // The quote starts at the line "On ... wrote:" or the forward marker. Keep it as plain text.
            var marker = kind == "forward" ? text.IndexOf("---------- Forwarded message", StringComparison.Ordinal) : FindReplyHeader(text);
            if (marker > 0)
            {
                var rest = text[marker..];
                var newline = kind == "forward" ? IndexOfNth(rest, '\n', 5) : rest.IndexOf('\n');
                var header = newline > 0 ? rest[..newline] : rest;
                var quoted = newline > 0 ? rest[(newline + 1)..] : "";
                if (kind != "forward")
                    quoted = string.Join('\n', quoted.Split('\n').Select(l => l.StartsWith("> ", StringComparison.Ordinal) ? l[2..] : l == ">" ? "" : l));
                quote = new Quoted(header, quoted.TrimEnd(), null, kind == "forward");
                text = text[..marker].TrimEnd();
            }
        }
        var (html, images) = Written(draft);
        return new OutgoingMail
        {
            AccountId = account.Id,
            To = MailService.People(draft.To),
            Cc = MailService.People(draft.Cc),
            Bcc = MailService.People(draft.Bcc),
            ReplyTo = MailService.People(draft.ReplyTo),
            Subject = draft.Subject ?? "",
            Body = text,
            Html = html,
            Images = images,
            Quote = quote,
            InReplyTo = draft.InReplyTo,
            References = draft.References.ToList(),
            Attachments = MailReader.Attachments(draft).Select(a => new OutgoingAttachment(a.FileName, a.Size, SourceIndex: a.Index)).ToList(),
            DraftUid = uid,
        };
    }

    /// <summary>The editor's own HTML from a draft made here, with its pictures; nothing for drafts made elsewhere.</summary>
    private static (string? Html, IReadOnlyList<InlineImage> Images) Written(MimeMessage draft)
    {
        if (draft.HtmlBody is not { } html)
            return (null, []);
        var marker = $"<div class=\"{Replies.BodyClass}\">";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return (null, []);
        start += marker.Length;
        // The matching </div>: count the divs opened and closed inside.
        var depth = 1;
        var at = start;
        while (depth > 0)
        {
            var open = html.IndexOf("<div", at, StringComparison.OrdinalIgnoreCase);
            var close = html.IndexOf("</div>", at, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
                return (null, []);
            if (open >= 0 && open < close)
            {
                depth++;
                at = open + 4;
            }
            else
            {
                depth--;
                at = close + 6;
            }
        }
        var inner = html[start..(at - 6)];
        var images = draft.BodyParts.OfType<MimePart>()
            .Where(p => p.ContentId is { Length: > 0 } && p.Content is not null && inner.Contains("cid:" + p.ContentId, StringComparison.Ordinal))
            .Select(p =>
            {
                using var bytes = new MemoryStream();
                p.Content!.DecodeTo(bytes);
                return new InlineImage(p.ContentId!, bytes.ToArray(), p.ContentType.MimeType);
            })
            .ToList();
        return (inner, images);
    }

    private static int FindReplyHeader(string text)
    {
        var lines = text.Split('\n');
        var position = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith("On ", StringComparison.Ordinal) && line.TrimEnd().EndsWith("wrote:", StringComparison.Ordinal))
                return position;
            position += line.Length + 1;
        }
        return -1;
    }

    private static int IndexOfNth(string text, char c, int n)
    {
        var index = -1;
        for (var i = 0; i < n; i++)
        {
            index = text.IndexOf(c, index + 1);
            if (index < 0)
                return -1;
        }
        return index;
    }

    private static IEnumerable<MailboxAddress> Boxes(IEnumerable<MailPerson> people) =>
        people.Select(p => new MailboxAddress(p.Name, p.Address));

    private static MimeEntity Copy(MimeEntity entity)
    {
        using var stream = new MemoryStream();
        entity.WriteTo(stream);
        stream.Position = 0;
        return MimeEntity.Load(stream);
    }
}
