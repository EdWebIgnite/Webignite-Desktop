namespace Webignite.Core.Projects;

/// <summary>
/// One piece of a file attached to a task in a shared project, small enough to travel as one locked record. The
/// task itself already lists its files; these carry what's in them, so the other PC has the file too.
/// </summary>
public sealed record AttachmentPart(string AttachmentId, string TaskId, string FileName, long Bytes, int Index, int Count, byte[] Data);

public static class AttachmentParts
{
    /// <summary>The biggest file that goes to the other PC; a bigger one stays on the PC it was attached on.</summary>
    public const long MostBytes = 25 * 1024 * 1024;

    /// <summary>Each part: the same size as a chat file's, well under the server's limit for one record.</summary>
    public const int PartBytes = Chat.ChatRules.PartBytes;

    public static bool Travels(TaskAttachment attachment) => attachment.Size is > 0 and <= MostBytes;

    public static int Count(long bytes) => (int)Math.Max(1, (bytes + PartBytes - 1) / PartBytes);

    public static string PartId(string attachmentId, int index) => $"{attachmentId}-{index}";

    /// <summary>A file's bytes cut into parts, in order.</summary>
    public static IReadOnlyList<AttachmentPart> Split(string taskId, TaskAttachment attachment, byte[] data)
    {
        var count = Count(data.Length);
        var parts = new List<AttachmentPart>(count);
        for (var index = 0; index < count; index++)
        {
            var at = index * PartBytes;
            parts.Add(new AttachmentPart(attachment.Id, taskId, attachment.FileName, data.Length, index, count,
                data[at..Math.Min(data.Length, at + PartBytes)]));
        }
        return parts;
    }

    /// <summary>
    /// Whether a part that arrived makes sense before anything is written: ids safe for a folder name, the place in
    /// range, and no bigger than a file that's allowed to travel.
    /// </summary>
    public static bool IsSound(AttachmentPart part) =>
        Sync.SyncLimits.IdIsFine(part.AttachmentId) && Sync.SyncLimits.IdIsFine(part.TaskId)
        && part.Bytes is > 0 and <= MostBytes && part.Count == Count(part.Bytes)
        && part.Index >= 0 && part.Index < part.Count && part.Data.Length <= PartBytes
        && part.Data.Length == (part.Index < part.Count - 1 ? PartBytes : part.Bytes - (long)(part.Count - 1) * PartBytes);
}
