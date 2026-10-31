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
        message.Subject = mail.Subject.Trim();
        message.Date = DateTimeOffset.Now;
        message.MessageId = MimeUtils.GenerateMessageId(from.Address.Split('@')[^1]);
        if (mail.InReplyTo is { Length: > 0 } inReplyTo)
            message.InReplyTo = inReplyTo;
        foreach (var reference in mail.References)
            message.References.Add(reference);
        if (mail.Quote is not null)
            message.Headers.Add(QuoteHeader, mail.Quote.IsForward ? "forward" : "reply");

        var builder = new BodyBuilder
        {
            TextBody = Replies.TextPart(mail),
            HtmlBody = Replies.HtmlPart(mail),
        };
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
        var from = MailService.People(original.From).FirstOrDefault();
        var (to, cc) = Replies.Recipients(from, MailService.People(original.ReplyTo), MailService.People(original.To),
            MailService.People(original.Cc), myAddresses, all);
        var body = MailReader.Read(mime, allowRemoteImages: true);
        return new OutgoingMail
        {
            AccountId = account.Id,
            To = to,
            Cc = cc,
            Subject = MailText.ReplySubject(original.Subject ?? ""),
            Body = Replies.StartBody(account.Signature),
            Quote = new Quoted(Replies.Header(from, original.Date), body.Text, body.Html, IsForward: false),
            InReplyTo = original.MessageId,
            References = Replies.References(original.References, original.MessageId),
        };
    }

    /// <summary>A forward, carrying the original attachments along.</summary>
    public static OutgoingMail Forward(byte[] mime, MailAccount account)
    {
        var original = MailReader.Parse(mime);
        var from = MailService.People(original.From).FirstOrDefault();
        var body = MailReader.Read(mime, allowRemoteImages: true);
        return new OutgoingMail
        {
            AccountId = account.Id,
            Subject = MailText.ForwardSubject(original.Subject ?? ""),
            Body = Replies.StartBody(account.Signature),
            Quote = new Quoted(Replies.ForwardHeader(from, MailService.People(original.To), original.Date, original.Subject ?? ""),
                body.Text, body.Html, IsForward: true),
            Attachments = body.Attachments.Select(a => new OutgoingAttachment(a.FileName, a.Size, SourceIndex: a.Index)).ToList(),
        };
    }

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
                    quoted = string.Join('\n', quoted.Split('\n').Select(l => l.StartsWith("> ") ? l[2..] : l == ">" ? "" : l));
                quote = new Quoted(header, quoted.TrimEnd(), null, kind == "forward");
                text = text[..marker].TrimEnd();
            }
        }
        return new OutgoingMail
        {
            AccountId = account.Id,
            To = MailService.People(draft.To),
            Cc = MailService.People(draft.Cc),
            Bcc = MailService.People(draft.Bcc),
            Subject = draft.Subject ?? "",
            Body = text,
            Quote = quote,
            InReplyTo = draft.InReplyTo,
            References = draft.References.ToList(),
            Attachments = MailReader.Attachments(draft).Select(a => new OutgoingAttachment(a.FileName, a.Size, SourceIndex: a.Index)).ToList(),
            DraftUid = uid,
        };
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
