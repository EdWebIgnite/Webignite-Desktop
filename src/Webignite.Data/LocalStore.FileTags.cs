using Webignite.Core.Files;

namespace Webignite.Data;

/// <summary>Tagged files from Files. The path is inside the encrypted payload, as client names are in it.</summary>
public sealed partial class LocalStore
{
    private const string FileTagContext = "filetag:";

    public TaggedFile SaveTaggedFile(TaggedFile file) => Put("file_tags", FileTagContext, file.Id, file);

    public void DeleteTaggedFile(string id) => Remove("file_tags", id);

    public IReadOnlyList<TaggedFile> ListTaggedFiles() => ReadAll<TaggedFile>(FileTagContext, "SELECT id, payload FROM file_tags");
}
