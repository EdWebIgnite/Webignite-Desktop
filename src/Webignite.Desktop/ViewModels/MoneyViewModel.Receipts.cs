using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Finance;
using Webignite.Data;

namespace Webignite.Desktop.ViewModels;

// Receipts kept with expenses: picked when an expense is added, or added to one later, and opened from its row.
public partial class MoneyViewModel
{
    /// <summary>Set by the page: asks for a receipt file and gives its path, or null when none was picked.</summary>
    public Func<Task<string?>>? PickReceipt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutReceipt))]
    [NotifyPropertyChangedFor(nameof(OutReceiptName))]
    public partial string? OutReceiptPath { get; set; }

    public bool HasOutReceipt => OutReceiptPath is not null;
    public string OutReceiptName => OutReceiptPath is null ? "" : Path.GetFileName(OutReceiptPath);

    /// <summary>"2 expenses have no receipt yet" for the month on show, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingReceipts))]
    public partial string MissingReceipts { get; set; } = "";

    public bool HasMissingReceipts => MissingReceipts.Length > 0;

    [RelayCommand]
    private async Task PickOutReceipt()
    {
        if (PickReceipt is not null && await PickReceipt() is { } path)
            OutReceiptPath = path;
    }

    [RelayCommand]
    private void ClearOutReceipt() => OutReceiptPath = null;

    /// <summary>Keeps a copy of the picked file with the expense. Returns the expense to save, or null with a message when it couldn't.</summary>
    private MoneyEntry? WithReceipt(MoneyEntry entry, string? path, Action<string> problem)
    {
        if (path is null)
            return entry;
        try
        {
            return services.Receipts.Attach(entry, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Keeping a receipt", e);
            problem("Couldn't keep the receipt: " + e.Message);
            return null;
        }
    }

    internal async Task AddReceipt(MoneyEntry entry)
    {
        if (PickReceipt is null || await PickReceipt() is not { } path)
            return;
        if (WithReceipt(entry, path, text => services.ShowNote?.Invoke("Couldn't add the receipt", text, null)) is { } kept)
        {
            services.Store.SaveEntry(kept);
            Refresh();
        }
    }

    internal void OpenReceipt(MoneyEntry entry)
    {
        if (services.Receipts.PathOf(entry) is { } path)
            services.Go.ShowFile(path);
        else
            services.ShowNote?.Invoke("Receipt not found", $"{entry.Receipt?.FileName} isn't in the receipts folder on this PC.", null);
    }
}
