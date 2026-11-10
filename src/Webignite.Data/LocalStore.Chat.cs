using Webignite.Core.Chat;

namespace Webignite.Data;

/// <summary>Chat between the two of you: messages, and the parts of files sent with them. All encrypted.</summary>
public sealed partial class LocalStore
{
    private const string ChatContext = "chat:";
    private const string ChatPartContext = "chatpart:";

    /// <summary>Keeps a message; one already here is left as it is, as messages don't change once sent.</summary>
    public ChatMessage SaveChatMessage(ChatMessage message)
    {
        Run("INSERT INTO chat_messages (id, sent, payload) VALUES ($id, $sent, $payload) ON CONFLICT (id) DO NOTHING",
            ("$id", message.Id), ("$sent", message.Sent.ToString("O")), ("$payload", Seal(ChatContext + message.Id, message)));
        return message;
    }

    /// <summary>Oldest first, the latest <paramref name="most"/>.</summary>
    public IReadOnlyList<ChatMessage> ListChatMessages(int most = 500)
    {
        var newestFirst = ReadAll<ChatMessage>(ChatContext, "SELECT id, payload FROM chat_messages ORDER BY sent DESC LIMIT $most", ("$most", most));
        newestFirst.Reverse();
        return newestFirst;
    }

    public ChatMessage? GetChatMessage(string id) => Find<ChatMessage>("chat_messages", ChatContext, id);

    public void SaveChatPart(ChatPart part) =>
        Run("INSERT INTO chat_parts (id, file_id, payload) VALUES ($id, $file, $payload) ON CONFLICT (id) DO NOTHING",
            ("$id", part.Id), ("$file", part.FileId), ("$payload", Seal(ChatPartContext + part.Id, part)));

    public IReadOnlyList<ChatPart> ListChatParts(string fileId) =>
        ReadAll<ChatPart>(ChatPartContext, "SELECT id, payload FROM chat_parts WHERE file_id = $file", ("$file", fileId));

    public bool HasChatPart(string id) => Find<ChatPart>("chat_parts", ChatPartContext, id) is not null;

    /// <summary>The ids of every message and part, to know which haven't been sent yet without reading them all.</summary>
    public IReadOnlyList<string> ChatIds(bool parts)
    {
        using var command = Command(parts ? "SELECT id FROM chat_parts" : "SELECT id FROM chat_messages");
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }
}
