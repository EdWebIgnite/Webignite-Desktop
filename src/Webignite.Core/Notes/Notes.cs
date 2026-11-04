using System.Globalization;
using System.Text;
using Webignite.Core.Search;
using Webignite.Core.Writing;

namespace Webignite.Core.Notes;

/// <summary>
/// A notebook: the person's own for now. Shared notebooks come once projects sync between the two of us; the
/// record then gets who it's shared with. Folders are one level deep, in the order they were made.
/// </summary>
public sealed record Notebook
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Folders { get; init; } = [];
    public DateTimeOffset Created { get; init; }
}

/// <summary>
/// What a list needs to know about a note. The writing itself is kept apart (it can hold pictures), and read only
/// when the note is opened. <see cref="Text"/> is the words, for searching and the line under the title.
/// </summary>
public sealed record Note
{
    public required string Id { get; init; }
    public required string NotebookId { get; init; }

    /// <summary>The folder in its notebook, or "" when it isn't in one.</summary>
    public string Folder { get; init; } = "";

    public string Title { get; init; } = "";
    public string Text { get; init; } = "";

    /// <summary>The client it's about, so it shows on their page.</summary>
    public string? ClientId { get; init; }

    public bool Pinned { get; init; }

    /// <summary>Checklist items in it, and how many are ticked.</summary>
    public int Checks { get; init; }
    public int Ticked { get; init; }

    public DateTimeOffset Created { get; init; }
    public DateTimeOffset Changed { get; init; }

    /// <summary>When it was put in Deleted; it goes for good <see cref="NoteRules.KeepDeletedDays"/> days later.</summary>
    public DateTimeOffset? Deleted { get; init; }
}

/// <summary>The quick note: its shortcut from any program, and the notebook it goes into.</summary>
public sealed record NoteSettings
{
    public const string DefaultShortcut = "Ctrl+Alt+N";

    /// <summary>The keys that open a quick note from any program. Empty turns the shortcut off.</summary>
    public string QuickShortcut { get; init; } = DefaultShortcut;

    /// <summary>Where quick notes go; the first notebook when this one is gone.</summary>
    public string? QuickNotebookId { get; init; }
}

/// <summary>Where the note list is looking.</summary>
public enum NotePlace { All, Notebook, Folder, Client, Deleted }

/// <summary>A note found by a search, with the bit of it that matched.</summary>
public sealed record NoteHit(Note Note, string Snippet, int Score);

public static class NoteRules
{
    public const int KeepDeletedDays = 30;
    public const string DefaultNotebook = "My notes";
    public const string Untitled = "Untitled note";

    /// <summary>The name a note goes by: its title, else its first line, else "Untitled note".</summary>
    public static string Name(Note note)
    {
        if (!string.IsNullOrWhiteSpace(note.Title))
            return note.Title.Trim();
        var first = FirstLine(note.Text);
        return first.Length > 0 ? Shorten(first, 80) : Untitled;
    }

    /// <summary>The line under the title in the list: the words after the first line when that is the name.</summary>
    public static string Preview(Note note, int length = 120)
    {
        var text = note.Text.Replace("\r", "");
        if (string.IsNullOrWhiteSpace(note.Title))
        {
            var cut = text.IndexOf('\n', StringComparison.Ordinal);
            text = cut < 0 ? "" : text[(cut + 1)..];
        }
        return Shorten(Plain(text), length);
    }

    /// <summary>"3 of 5 done", or "" with no checklist.</summary>
    public static string Progress(Note note) =>
        note.Checks == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $"{note.Ticked} of {note.Checks} done");

    /// <summary>Pinned first, then the most recently changed.</summary>
    public static IReadOnlyList<Note> Ordered(IEnumerable<Note> notes) =>
        [.. notes.OrderByDescending(n => n.Pinned).ThenByDescending(n => n.Changed).ThenBy(n => n.Id, StringComparer.Ordinal)];

    /// <summary>The notes in a place. Deleted notes show only in Deleted.</summary>
    public static IEnumerable<Note> In(IEnumerable<Note> notes, NotePlace place, string? notebookId = null, string? folder = null, string? clientId = null) =>
        place switch
        {
            NotePlace.Deleted => notes.Where(n => n.Deleted is not null),
            NotePlace.Notebook => notes.Where(n => n.Deleted is null && n.NotebookId == notebookId),
            NotePlace.Folder => notes.Where(n => n.Deleted is null && n.NotebookId == notebookId && string.Equals(n.Folder, folder, StringComparison.OrdinalIgnoreCase)),
            NotePlace.Client => notes.Where(n => n.Deleted is null && n.ClientId == clientId),
            _ => notes.Where(n => n.Deleted is null),
        };

    /// <summary>Deleted long enough ago to go for good.</summary>
    public static bool Expired(Note note, DateTimeOffset now) =>
        note.Deleted is { } when && now - when >= TimeSpan.FromDays(KeepDeletedDays);

    /// <summary>The facts a list keeps about the writing: its words and its checklist.</summary>
    public static Note Describe(Note note, RichDocument writing)
    {
        var items = writing.Blocks.OfType<TextBlock>().Where(b => b.List == ListKind.Check).ToList();
        return note with
        {
            Text = RichText.ToText(writing),
            Checks = items.Count,
            Ticked = items.Count(b => b.Checked),
        };
    }

    /// <summary>
    /// A quick note from any program: the first line is its title when there's more than one line. Lines that
    /// start "[ ]" become a checklist.
    /// </summary>
    public static (Note Note, RichDocument Writing) Quick(string typed, string id, string notebookId, DateTimeOffset now)
    {
        var text = typed.Replace("\r", "").Trim('\n');
        var lines = text.Split('\n');
        var title = lines.Length > 1 && lines[0].Trim().Length > 0 && !lines[0].TrimStart().StartsWith('[') ? lines[0].Trim() : "";
        var writing = RichText.FromText(title.Length > 0 ? string.Join('\n', lines.Skip(1)) : text);
        var note = Describe(new Note { Id = id, NotebookId = notebookId, Title = title, Created = now, Changed = now }, writing);
        return (note, writing);
    }

    /// <summary>Every word must be in the title or the words; a title match ranks first. Accents and case don't matter.</summary>
    public static IReadOnlyList<NoteHit> Search(IEnumerable<Note> notes, string query)
    {
        var words = QuickSearch.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            return [];
        var hits = new List<NoteHit>();
        foreach (var note in notes)
        {
            var title = QuickSearch.Fold(Name(note));
            var body = Fold(note.Text);
            var score = 0;
            var first = -1;
            foreach (var word in words)
            {
                if (title.StartsWith(word, StringComparison.Ordinal))
                    score += 30;
                else if (title.Contains(word, StringComparison.Ordinal))
                    score += 15;
                else if (body.IndexOf(word, StringComparison.Ordinal) is var at and >= 0)
                {
                    score += 5;
                    first = first < 0 ? at : Math.Min(first, at);
                }
                else
                {
                    score = -1;
                    break;
                }
            }
            if (score >= 0)
                hits.Add(new NoteHit(note, first < 0 ? Preview(note) : Around(note.Text, first, words[0].Length), score + (note.Pinned ? 1 : 0)));
        }
        return [.. hits.OrderByDescending(h => h.Score).ThenByDescending(h => h.Note.Changed)];
    }

    /// <summary>A folder name as typed, or null when it's empty or the notebook has one by that name already.</summary>
    public static string? NewFolder(Notebook notebook, string typed)
    {
        var name = OneLine(typed).Trim();
        if (name.Length == 0 || notebook.Folders.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase)))
            return null;
        return name;
    }

    /// <summary>Renames a folder in the notebook and moves its notes with it.</summary>
    public static (Notebook Notebook, IReadOnlyList<Note> Moved) RenameFolder(Notebook notebook, IEnumerable<Note> notes, string folder, string typed)
    {
        var name = OneLine(typed).Trim();
        if (name.Length == 0 || string.Equals(name, folder, StringComparison.Ordinal)
            || notebook.Folders.Any(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase) && string.Equals(f, name, StringComparison.OrdinalIgnoreCase)))
            return (notebook, []);
        var renamed = notebook with { Folders = [.. notebook.Folders.Select(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase) ? name : f)] };
        var moved = notes.Where(n => n.NotebookId == notebook.Id && string.Equals(n.Folder, folder, StringComparison.OrdinalIgnoreCase))
            .Select(n => n with { Folder = name }).ToList();
        return (renamed, moved);
    }

    /// <summary>Removes a folder; its notes stay in the notebook, out of any folder.</summary>
    public static (Notebook Notebook, IReadOnlyList<Note> Moved) RemoveFolder(Notebook notebook, IEnumerable<Note> notes, string folder)
    {
        var kept = notebook with { Folders = [.. notebook.Folders.Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))] };
        var moved = notes.Where(n => n.NotebookId == notebook.Id && string.Equals(n.Folder, folder, StringComparison.OrdinalIgnoreCase))
            .Select(n => n with { Folder = "" }).ToList();
        return (kept, moved);
    }

    /// <summary>A name for another notebook that isn't taken: "Notebook", then "Notebook 2".</summary>
    public static string NextNotebookName(IEnumerable<Notebook> notebooks)
    {
        var taken = notebooks.Select(n => n.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains("Notebook"))
            return "Notebook";
        var i = 2;
        while (taken.Contains(string.Create(CultureInfo.InvariantCulture, $"Notebook {i}")))
            i++;
        return string.Create(CultureInfo.InvariantCulture, $"Notebook {i}");
    }

    /// <summary>A notebook can go once nothing is in it, Deleted aside.</summary>
    public static bool CanRemove(Notebook notebook, IEnumerable<Note> notes, int notebookCount) =>
        notebookCount > 1 && !notes.Any(n => n.NotebookId == notebook.Id && n.Deleted is null);

    // Lower case without accents, keeping each character where it was so a match can be shown in the original.
    private static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var plain = c < 128 ? c : c.ToString().Normalize(NormalizationForm.FormD)[0];
            folded.Append(char.ToLowerInvariant(plain));
        }
        return folded.ToString();
    }

    private static string Around(string text, int at, int length)
    {
        var start = Math.Max(0, at - 40);
        var end = Math.Min(text.Length, at + length + 80);
        var piece = Plain(text[start..end]);
        return (start > 0 ? "…" : "") + piece + (end < text.Length ? "…" : "");
    }

    private static string FirstLine(string text) =>
        text.Replace("\r", "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) is { } line ? StripMark(line) : "";

    // The list marks plain text carries aren't part of a name.
    private static string StripMark(string line) =>
        line.StartsWith("[ ] ", StringComparison.Ordinal) || line.StartsWith("[x] ", StringComparison.Ordinal) ? line[4..]
        : line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("> ", StringComparison.Ordinal) ? line[2..] : line;

    // The words on one line, without the marks plain text gives lists.
    private static string Plain(string text) => OneLine(string.Join(' ', text.Replace("\r", "").Split('\n').Select(l => StripMark(l.Trim()))));

    private static string OneLine(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                space = builder.Length > 0;
                continue;
            }
            if (space)
                builder.Append(' ');
            space = false;
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..(length - 1)].TrimEnd() + "…";
}
