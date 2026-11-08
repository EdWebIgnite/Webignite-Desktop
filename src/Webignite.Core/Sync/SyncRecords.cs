using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webignite.Core.Projects;

namespace Webignite.Core.Sync;

/// <summary>The kinds of record that sync, as named on the server (which can't see inside them).</summary>
public static class SyncKinds
{
    public const string Project = "project";
    public const string Heading = "heading";
    public const string Task = "task";
    public const string Comment = "comment";
    public const string Activity = "activity";
    public const string Team = "team";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Project, Heading, Task, Comment, Activity, Team };
}

/// <summary>
/// Records as they travel: JSON, locked with a key made from the workspace key (AES-256-GCM), with the kind and id
/// bound in so the server can't swap one record for another or change one unnoticed.
/// </summary>
public static class RecordLock
{
    public static readonly JsonSerializerOptions Json = new();

    public static byte[] KeyFrom(byte[] workspaceKey) =>
        HKDF.Expand(HashAlgorithmName.SHA256, workspaceKey, 32, Encoding.UTF8.GetBytes("webignite records"));

    private static byte[] Bound(string kind, string id) => Encoding.UTF8.GetBytes($"{kind}:{id}");

    public static string Seal(string kind, string id, string json, byte[] key)
    {
        var plain = Encoding.UTF8.GetBytes(json);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(nonce, plain, cipher, tag, Bound(kind, id));
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    /// <summary>The JSON again, or null when it was locked with another key, for another record, or changed.</summary>
    public static string? Open(string kind, string id, string payload, byte[] key)
    {
        try
        {
            var all = Convert.FromBase64String(payload);
            if (all.Length < 28)
                return null;
            var plain = new byte[all.Length - 28];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(all.AsSpan(0, 12), all.AsSpan(28), all.AsSpan(12, 16), plain, Bound(kind, id));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// A short fingerprint of a record's JSON, to know whether it changed since it was last sent. Properties are put
    /// in order first, so the same record gives the same fingerprint however its lists of stamps were built up.
    /// </summary>
    public static string Fingerprint(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Ordered(JsonNode.Parse(json))?.ToJsonString() ?? "null")))[..32];

    private static JsonNode? Ordered(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Ordered(p.Value)))),
        JsonArray a => new JsonArray([.. a.Select(Ordered)]),
        null => null,
        _ => node.DeepClone(),
    };
}

/// <summary>
/// Two copies of the same record, one from each PC, made into one: each field keeps whichever change was made
/// later (by its stamp), so you and your partner can change different things on the same task at the same time
/// and both are kept. When both changed the same field, the later change wins.
/// </summary>
public static class RecordMerge
{
    // Which properties each stamped field covers. A property not listed goes with the copy changed last.
    private static readonly Dictionary<string, string[]> ProjectMap = new(StringComparer.Ordinal)
    {
        ["icon"] = ["Icon"], ["colour"] = ["Colour"], ["health"] = ["Health"], ["favourite"] = ["Favourite"], ["name"] = ["Name"],
        ["shared"] = ["Shared"], ["client"] = ["ClientId"], ["status"] = ["Status"], ["due"] = ["Due"], ["notes"] = ["Notes"],
        ["order"] = ["Order"], ["noteshtml"] = ["NotesHtml"], ["links"] = ["Links"], ["updates"] = ["Updates"], ["customfields"] = ["CustomFields"],
    };

    private static readonly Dictionary<string, string[]> HeadingMap = new(StringComparer.Ordinal)
    {
        ["name"] = ["Name"], ["order"] = ["Order"],
    };

    private static readonly Dictionary<string, string[]> TaskMap = new(StringComparer.Ordinal)
    {
        ["project"] = ["ProjectId"], ["heading"] = ["HeadingId"], ["parent"] = ["ParentId"], ["title"] = ["Title"], ["notes"] = ["Notes"],
        ["noteshtml"] = ["NotesHtml"], ["assigned"] = ["AssignedTo"], ["start"] = ["Start"], ["due"] = ["Due"], ["done"] = ["Done"],
        ["order"] = ["Order"], ["client"] = ["ClientId"], ["document"] = ["DocumentId"], ["mail"] = ["Mail"], ["priority"] = ["Priority"],
        ["tags"] = ["Tags"], ["repeat"] = ["Repeat"], ["attachments"] = ["Attachments"], ["cover"] = ["CoverId"], ["milestone"] = ["Milestone"],
        ["blockedby"] = ["BlockedBy"], ["values"] = ["Values"], ["repeatof"] = ["RepeatOf"], ["repeatfrom"] = ["RepeatFrom"],
    };

    private static readonly Dictionary<string, string[]> CommentMap = new(StringComparer.Ordinal) { ["text"] = ["Text", "Mentions"] };

    // Lists of people, merged person by person by their own stamps ("follower:{id}"), not as one field.
    private static readonly (string Prefix, string Property)[] TaskSets = [(Sets.Follower, "Followers"), (Sets.Like, "LikedBy")];
    private static readonly (string Prefix, string Property)[] CommentSets = [(Sets.Like, "LikedBy")];
    private static readonly (string Prefix, string Property)[] NoSets = [];

    /// <summary>Whether a stamp is later than another; at the same moment, the one by the later id, so both PCs agree.</summary>
    public static bool Later(Stamp a, Stamp b) => a.At != b.At ? a.At > b.At : string.CompareOrdinal(a.By, b.By) > 0;

    public static Project Merge(Project local, Project remote) => Merge(local, remote, ProjectMap, NoSets);
    public static Heading Merge(Heading local, Heading remote) => Merge(local, remote, HeadingMap, NoSets);
    public static ProjectTask Merge(ProjectTask local, ProjectTask remote) => Merge(local, remote, TaskMap, TaskSets);
    public static TaskComment Merge(TaskComment local, TaskComment remote) => Merge(local, remote, CommentMap, CommentSets);

    private static T Merge<T>(T local, T remote, Dictionary<string, string[]> map, (string Prefix, string Property)[] sets) where T : ITracked
    {
        var mine = JsonSerializer.SerializeToNode(local, RecordLock.Json)!.AsObject();
        var theirs = JsonSerializer.SerializeToNode(remote, RecordLock.Json)!.AsObject();
        var remoteNewer = Later(remote.Changed, local.Changed);
        var result = (remoteNewer ? theirs : mine).DeepClone().AsObject();
        var stamps = new Dictionary<string, Stamp>(local.Fields, StringComparer.Ordinal);
        foreach (var (field, properties) in map)
        {
            var l = local.Fields.TryGetValue(field, out var ls) ? ls : (Stamp?)null;
            var r = remote.Fields.TryGetValue(field, out var rs) ? rs : (Stamp?)null;
            var takeRemote = (l, r) switch
            {
                (null, null) => remoteNewer,
                (null, _) => true,
                (_, null) => false,
                _ => Later(r!.Value, l!.Value),
            };
            foreach (var property in properties)
                result[property] = (takeRemote ? theirs : mine)[property]?.DeepClone();
            if (takeRemote && r is { } stamp)
                stamps[field] = stamp;
        }
        foreach (var (prefix, property) in sets)
        {
            var mineList = Members(mine[property]);
            var theirList = Members(theirs[property]);
            var key = prefix + ":";
            var everyone = mineList.Concat(theirList)
                .Concat(local.Fields.Keys.Concat(remote.Fields.Keys).Where(k => k.StartsWith(key, StringComparison.Ordinal)).Select(k => k[key.Length..]))
                .Distinct(StringComparer.Ordinal);
            var kept = new JsonArray();
            foreach (var id in everyone)
            {
                var l = local.Fields.TryGetValue(key + id, out var ls) ? ls : (Stamp?)null;
                var r = remote.Fields.TryGetValue(key + id, out var rs) ? rs : (Stamp?)null;
                var takeRemote = (l, r) switch
                {
                    (null, null) => remoteNewer,
                    (null, _) => true,
                    (_, null) => false,
                    _ => Later(r!.Value, l!.Value),
                };
                if ((takeRemote ? theirList : mineList).Contains(id))
                    kept.Add(id);
                if (takeRemote && r is { } stamp)
                    stamps[key + id] = stamp;
            }
            result[property] = kept;
        }
        // Properties no field covers go with the copy changed last (already in result); the bookkeeping is worked out here.
        result["Created"] = JsonSerializer.SerializeToNode(Later(local.Created, remote.Created) ? remote.Created : local.Created, RecordLock.Json);
        result["Changed"] = JsonSerializer.SerializeToNode(remoteNewer ? remote.Changed : local.Changed, RecordLock.Json);
        result["Fields"] = JsonSerializer.SerializeToNode(stamps, RecordLock.Json);
        result["Deleted"] = JsonSerializer.SerializeToNode(Deleted(local, remote), RecordLock.Json);
        return result.Deserialize<T>(RecordLock.Json)!;
    }

    private static List<string> Members(JsonNode? list) =>
        list is JsonArray array ? [.. array.Select(n => n?.GetValue<string>()).OfType<string>()] : [];

    // Deleted on either side stays deleted, unless the other side changed it after the deletion (put back or edited).
    private static Stamp? Deleted(ITracked local, ITracked remote) => (local.Deleted, remote.Deleted) switch
    {
        (null, null) => null,
        ({ } l, null) => Later(remote.Changed, l) ? null : l,
        (null, { } r) => Later(local.Changed, r) ? null : r,
        ({ } l, { } r) => Later(r, l) ? r : l,
    };
}

/// <summary>
/// Who's who across the PCs. Each PC made its own ids for "me" and "my partner" before anything was shared; the
/// ids the first of the owner's PCs sends are the ones every PC then uses. A PC that finds them on the server takes
/// them on and changes its own records over, so tasks given to someone stay given to the right person.
/// </summary>
public sealed record TeamRecord(Teammate Owner, Teammate Member);

public static class TeamIds
{
    /// <summary>This PC's ids mapped to the shared ones: its "me" to whichever of the two this PC's person is, its "partner" to the other.</summary>
    public static Dictionary<string, string> Map(TeamSettings local, Teammate me, Teammate partner)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (local.Me is { } localMe && localMe.Id != me.Id)
            map[localMe.Id] = me.Id;
        if (local.Partner is { } localPartner && localPartner.Id != partner.Id)
            map[localPartner.Id] = partner.Id;
        return map;
    }

    /// <summary>
    /// The team on this PC with the shared ids. Names stay as set here, except a partner still called "Partner"
    /// takes the name the other PC uses for its own person.
    /// </summary>
    public static TeamSettings TakeOn(TeamSettings local, Teammate me, Teammate partner)
    {
        var localPartner = local.Partner!;
        var name = localPartner.Name == ProjectRules.DefaultPartner && partner.Name is not ("" or "Me" or ProjectRules.DefaultPartner)
            ? partner.Name
            : localPartner.Name;
        return local with { Me = local.Me! with { Id = me.Id }, Partner = localPartner with { Id = partner.Id, Name = name } };
    }

    /// <summary>A record's JSON with the old ids changed over. Ids are unique, so swapping them in the text is safe.</summary>
    public static string Remap(string json, IReadOnlyDictionary<string, string> map) =>
        map.Aggregate(json, (text, pair) => text.Replace($"\"{pair.Key}\"", $"\"{pair.Value}\"", StringComparison.Ordinal));
}
