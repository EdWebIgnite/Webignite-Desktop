using Webignite.Core.Files;
using Webignite.Data;

namespace Webignite.Desktop.Files;

/// <summary>
/// Which files and folders carry which tags. Read once, kept in memory (a few hundred small records at most) and
/// written straight through, so every Files tab sees the same. Tags follow a file renamed or moved in Files at
/// once, and one moved in Explorer on the same drive the next time its tag is looked at.
/// </summary>
public sealed class FileTagBook(LocalStore store, AppSettings settings)
{
    private Dictionary<string, TaggedFile>? byPath;

    private Dictionary<string, TaggedFile> Files => byPath ??= store.ListTaggedFiles()
        .GroupBy(f => FileTags.Key(f.Path)).ToDictionary(g => g.Key, g => g.First());

    /// <summary>Raised after tags change, so other open Files tabs can show it.</summary>
    public event EventHandler? Changed;

    /// <summary>The tag list, in order.</summary>
    public IReadOnlyList<FileTag> Tags => settings.FileTags;

    public IReadOnlyList<FileTag> TagsOf(string path) =>
        Files.TryGetValue(FileTags.Key(path), out var file) ? FileTags.Of(Tags, file) : [];

    /// <summary>Puts a tag on these files and folders, or takes it off.</summary>
    public void Set(IEnumerable<string> paths, string tagId, bool on)
    {
        foreach (var path in paths)
        {
            var key = FileTags.Key(path);
            Files.TryGetValue(key, out var file);
            var changed = FileTags.With(file, path, tagId, on, () => Guid.NewGuid().ToString("N"));
            if (changed is null)
            {
                if (file is not null)
                {
                    store.DeleteTaggedFile(file.Id);
                    Files.Remove(key);
                }
                continue;
            }
            if (changed.FileKey is null && OperatingSystem.IsWindows())
                changed = changed with { FileKey = FileIdentity.Key(path) };
            Files[key] = store.SaveTaggedFile(changed);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A file or folder moved or renamed in Files: its tags, and those of anything inside it, go with it.</summary>
    public void Moved(string from, string to)
    {
        var moved = FileTags.Moved(Files.Values, from, to);
        foreach (var file in moved)
            Repath(file);
        if (moved.Count > 0)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A copy made in Files gets the same tags as the file it was copied from, as in Finder.</summary>
    public void Copied(string from, string to)
    {
        var copies = FileTags.Moved(Files.Values, from, to);
        foreach (var copy in copies)
        {
            var made = copy with { Id = Guid.NewGuid().ToString("N"), FileKey = OperatingSystem.IsWindows() ? FileIdentity.Key(copy.Path) : null };
            Files[FileTags.Key(made.Path)] = store.SaveTaggedFile(made);
        }
        if (copies.Count > 0)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Every file and folder with this tag that is still there. One moved outside the app on the same drive is
    /// found again by its Windows id; one gone for good (deleted, or moved to another drive) is left out.
    /// </summary>
    public IReadOnlyList<string> With(string tagId)
    {
        var found = new List<string>();
        foreach (var file in Files.Values.Where(f => f.Tags.Contains(tagId)).ToList())
        {
            if (File.Exists(file.Path) || Directory.Exists(file.Path))
            {
                found.Add(file.Path);
                continue;
            }
            if (OperatingSystem.IsWindows() && file.FileKey is { } key && FileIdentity.Find(key, file.Path) is { } now)
            {
                Repath(file with { Path = now });
                found.Add(now);
            }
        }
        return found;
    }

    /// <summary>How many files and folders carry each tag, for the side list.</summary>
    public int Count(string tagId) => Files.Values.Count(f => f.Tags.Contains(tagId));

    // The tag list itself, from Settings.

    public void AddTag(string name, string? colour = null) => SaveTags(FileTags.Add(Tags, name, colour));

    public void ChangeTag(string id, string name, string colour) => SaveTags(FileTags.Change(Tags, id, name, colour));

    /// <summary>Takes a tag off the list and off every file that had it.</summary>
    public void RemoveTag(string id)
    {
        var had = Files.Values.Where(f => f.Tags.Contains(id)).Select(f => f.Path).ToList();
        SaveTags(FileTags.Remove(Tags, id));
        Set(had, id, false);
    }

    private void SaveTags(IReadOnlyList<FileTag> tags)
    {
        settings.FileTags = tags;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Repath(TaggedFile file)
    {
        var old = Files.FirstOrDefault(p => p.Value.Id == file.Id).Key;
        if (old is not null)
            Files.Remove(old);
        Files[FileTags.Key(file.Path)] = store.SaveTaggedFile(file);
    }
}
