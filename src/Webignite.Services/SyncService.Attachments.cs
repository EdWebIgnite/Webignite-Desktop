using System.Text.Json;
using Webignite.Core.Projects;
using Webignite.Core.Sync;
using Webignite.Data;

namespace Webignite.Services;

// Files on tasks in shared projects. The task lists its files and syncs as it always has; these send what's in
// them, in parts, with each full sync, and put them back together on the other PC beside its own attachments.
// Each part goes once; a file is read only while some of its parts haven't gone yet.
public sealed partial class SyncService
{
    /// <summary>Where attached files are kept on this PC (the same folder as ProjectService's); null sends and takes none.</summary>
    public string? AttachmentsFolder { get; set; }

    private string FileOf(string taskId, string attachmentId, string fileName) =>
        Path.Combine(AttachmentsFolder!, taskId, attachmentId, Attachments.SafeName(fileName));

    private List<Out> OutgoingAttachments(IEnumerable<ProjectTask> sharedTasks)
    {
        if (AttachmentsFolder is null)
            return [];
        var sent = store.SentFingerprints();
        var all = new List<Out>();
        foreach (var task in sharedTasks.Where(t => t.Deleted is null && SyncLimits.IdIsFine(t.Id)))
        {
            foreach (var attachment in task.Attachments.Where(a => AttachmentParts.Travels(a) && SyncLimits.IdIsFine(a.Id)))
            {
                var count = AttachmentParts.Count(attachment.Size);
                if (Enumerable.Range(0, count).All(i => sent.ContainsKey((SyncKinds.AttachPart, AttachmentParts.PartId(attachment.Id, i)))))
                    continue;
                var path = FileOf(task.Id, attachment.Id, attachment.FileName);
                byte[] data;
                try
                {
                    if (!File.Exists(path))
                        continue; // not here yet (still coming from the other PC), or taken off meanwhile
                    data = File.ReadAllBytes(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    AppLog.Write($"Reading {attachment.FileName} to sync it", e);
                    continue;
                }
                if (data.Length != attachment.Size)
                    continue;
                foreach (var part in AttachmentParts.Split(task.Id, attachment, data))
                {
                    var id = AttachmentParts.PartId(attachment.Id, part.Index);
                    if (!sent.ContainsKey((SyncKinds.AttachPart, id)))
                        all.Add(Make(SyncKinds.AttachPart, id, part));
                }
            }
        }
        return all;
    }

    private Taken TakeInAttachmentPart(string id, string json, string fingerprint, List<(string, string, string)> sent)
    {
        if (JsonSerializer.Deserialize<AttachmentPart>(json, RecordLock.Json) is not { } part || !AttachmentParts.IsSound(part)
            || id != AttachmentParts.PartId(part.AttachmentId, part.Index))
            return Taken.Unreadable;
        sent.Add((SyncKinds.AttachPart, id, fingerprint));
        if (AttachmentsFolder is null)
            return Taken.Same;
        var target = FileOf(part.TaskId, part.AttachmentId, part.FileName);
        try
        {
            if (File.Exists(target) && new FileInfo(target).Length == part.Bytes)
                return Taken.Same;
            // Parts wait in a folder of their own until all have come, then make the file in one go.
            var folder = Path.GetDirectoryName(target)!;
            var parts = Path.Combine(folder, ".parts");
            Directory.CreateDirectory(parts);
            File.WriteAllBytes(Path.Combine(parts, part.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)), part.Data);
            var pieces = Enumerable.Range(0, part.Count)
                .Select(i => Path.Combine(parts, i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
            if (!pieces.All(File.Exists))
                return Taken.Same;
            var making = target + ".coming";
            using (var file = File.Create(making))
            {
                foreach (var piece in pieces)
                {
                    using var source = File.OpenRead(piece);
                    source.CopyTo(file);
                }
            }
            if (new FileInfo(making).Length != part.Bytes)
            {
                File.Delete(making);
                return Taken.Same;
            }
            File.Move(making, target, overwrite: true);
            Directory.Delete(parts, recursive: true);
            return Taken.Changed; // the task pane and Files tab show it now
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"Putting {part.FileName} together from sync", e);
            return Taken.Same;
        }
    }
}
