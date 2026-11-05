using CommunityToolkit.Mvvm.ComponentModel;
using Webignite.Core.Clients;

namespace Webignite.Desktop.ViewModels;

/// <summary>A kind of file people are written to.</summary>
public enum ContactFileKind { VCard, Csv }

// Contacts to and from files: vCard for phones, Outlook and Google, CSV for spreadsheets. Not synced; once each time.
public sealed partial class ContactsViewModel
{
    [ObservableProperty] public partial string FileMessage { get; private set; } = "";

    public bool HasGroupPicked => Group != AllGroups;

    partial void OnGroupChanged(string value) => OnPropertyChanged(nameof(HasGroupPicked));

    /// <summary>Reads a .vcf or .csv file and adds the people who aren't in Contacts yet.</summary>
    public async Task ImportAsync(string path)
    {
        if (!Keep())
            return;
        try
        {
            var text = await File.ReadAllTextAsync(path);
            var now = DateTimeOffset.UtcNow;
            var read = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? ContactFiles.FromCsv(text, now)
                : ContactFiles.FromVCard(text, now);
            if (read.Count == 0)
            {
                FileMessage = $"No people were found in {Path.GetFileName(path)}.";
                return;
            }
            var plan = ContactFiles.Plan(services.Contacts.List(), read, services.Clients.List());
            services.Contacts.Import(plan.ToAdd);
            FileMessage = ContactFiles.Summary(plan);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            FileMessage = $"Couldn't read {Path.GetFileName(path)}: {e.Message}";
        }
    }

    /// <summary>Everyone, or the group picked, written to a file.</summary>
    public async Task ExportAsync(string path, ContactFileKind kind, bool groupOnly)
    {
        if (!Keep())
            return;
        var people = services.Contacts.List().Where(p => !groupOnly || p.Groups.Contains(Group, StringComparer.CurrentCultureIgnoreCase)).ToList();
        var clients = services.Clients.List().ToDictionary(c => c.Id, c => c.Name);
        string? ClientName(string? id) => id is not null && clients.TryGetValue(id, out var name) ? name : null;
        var text = kind == ContactFileKind.Csv ? ContactFiles.ToCsv(people, ClientName) : ContactFiles.ToVCard(people, ClientName);
        try
        {
            await File.WriteAllTextAsync(path, text);
            FileMessage = (people.Count == 1 ? "1 person" : $"{people.Count} people") + $" saved to {Path.GetFileName(path)}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            FileMessage = $"Couldn't save it: {e.Message}";
        }
    }

    /// <summary>A file name for an export: "Contacts.vcf" or "Hosting clients.csv".</summary>
    public string ExportName(ContactFileKind kind, bool groupOnly) =>
        Webignite.Core.Files.FileNames.Safe(groupOnly ? Group : "Contacts") + (kind == ContactFileKind.Csv ? ".csv" : ".vcf");
}
