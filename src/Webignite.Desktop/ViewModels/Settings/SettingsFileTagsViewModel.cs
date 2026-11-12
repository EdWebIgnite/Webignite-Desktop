using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Files;
using Webignite.Core.Mail;

namespace Webignite.Desktop.ViewModels;

// Settings, Files: the tags for files and folders, named and coloured as you like.
public sealed partial class SettingsFileTagsViewModel : ViewModelBase
{
    private readonly AppServices services;

    public SettingsFileTagsViewModel(AppServices services)
    {
        this.services = services;
    }

    public ObservableCollection<FileTagRow> FileTagRows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFileTagCommand))]
    public partial string NewFileTag { get; set; } = "";

    [ObservableProperty] public partial string FileTagMessage { get; set; } = "";

    public void LoadFileTags()
    {
        FileTagRows.Clear();
        foreach (var tag in services.FileTags.Tags)
            FileTagRows.Add(new FileTagRow(tag, this));
    }

    private bool CanAddFileTag() => NewFileTag.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanAddFileTag))]
    private void AddFileTag()
    {
        if (FileTags.Check(services.FileTags.Tags, NewFileTag) is { } problem)
        {
            FileTagMessage = problem;
            return;
        }
        services.FileTags.AddTag(NewFileTag);
        NewFileTag = "";
        FileTagMessage = "";
        LoadFileTags();
    }

    internal void ChangeFileTag(FileTagRow row, string name, string colour)
    {
        if (FileTags.Check(services.FileTags.Tags, name, row.Tag.Id) is { } problem)
        {
            FileTagMessage = problem;
            row.Name = row.Tag.Name;
            return;
        }
        services.FileTags.ChangeTag(row.Tag.Id, name, colour);
        FileTagMessage = "";
        LoadFileTags();
    }

    internal void RemoveFileTag(FileTagRow row)
    {
        var count = services.FileTags.Count(row.Tag.Id);
        services.FileTags.RemoveTag(row.Tag.Id);
        FileTagMessage = count switch
        {
            0 => $"Removed {row.Tag.Name}.",
            1 => $"Removed {row.Tag.Name}, and took it off the one file that had it.",
            _ => $"Removed {row.Tag.Name}, and took it off the {count} files that had it.",
        };
        LoadFileTags();
    }
}

/// <summary>One tag in Settings: its name to type over, its colour to pick, and a way to remove it.</summary>
public sealed partial class FileTagRow(FileTag tag, SettingsFileTagsViewModel owner) : ViewModelBase
{
    public FileTag Tag => tag;

    [ObservableProperty] public partial string Name { get; set; } = tag.Name;

    public IReadOnlyList<FileTagColour> Colours => [.. MailCategories.Colours.Select(c => new FileTagColour(c, c == tag.Colour, this))];

    /// <summary>Keeps a new name: on Enter, or on leaving the box.</summary>
    [RelayCommand]
    private void Keep()
    {
        if (Name.Trim() != tag.Name)
            owner.ChangeFileTag(this, Name, tag.Colour);
    }

    internal void Recolour(string colour) => owner.ChangeFileTag(this, tag.Name, colour);

    [RelayCommand]
    private void Remove() => owner.RemoveFileTag(this);
}

public sealed partial class FileTagColour(string colour, bool chosen, FileTagRow row) : ViewModelBase
{
    public string Colour => colour;
    public bool Chosen => chosen;

    [RelayCommand]
    private void Pick() => row.Recolour(colour);
}
