using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Web;
using Webignite.Services;

namespace Webignite.Desktop.ViewModels;

/// <summary>Which rows of the crawl's table are shown.</summary>
public enum CrawlFilter
{
    ToFix,
    BrokenLinks,
    All,
}

/// <summary>
/// The site crawler: walks a website from one address and lists every page with what is wrong with it (a missing
/// title or description, broken links, pictures with no alt text), to fix or to save as a spreadsheet. Read-only.
/// The rules are in Core (HtmlFacts, SiteCrawl) and the fetching in Services (SiteCrawler).
/// </summary>
public sealed partial class CrawlerViewModel(AppServices services) : ViewModelBase, IHoldsWork, IDisposable
{
    private CancellationTokenSource? stop;
    private IReadOnlyList<CrawlRow> all = [];

    public IReadOnlyList<int> PageLimits { get; } = [50, SiteCrawl.UsualPages, 500, SiteCrawl.MostPages];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string Address { get; set; } = "";

    [ObservableProperty] public partial int PageLimit { get; set; } = SiteCrawl.UsualPages;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(HoldsWork))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    public partial bool IsCrawling { get; set; }

    [ObservableProperty] public partial string Progress { get; set; } = "";
    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial bool MessageIsProblem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsToFix), nameof(ShowsBroken), nameof(ShowsAll))]
    public partial CrawlFilter Filter { get; set; }

    public ObservableCollection<CrawlRowView> Rows { get; } = [];

    public bool IsIdle => !IsCrawling;
    public bool HasResults => all.Count > 0;
    public bool HasRows => Rows.Count > 0;
    public bool NothingToShow => HasResults && Rows.Count == 0;
    public bool ShowsToFix => Filter == CrawlFilter.ToFix;
    public bool ShowsBroken => Filter == CrawlFilter.BrokenLinks;
    public bool ShowsAll => Filter == CrawlFilter.All;

    public string NothingText => Filter switch
    {
        CrawlFilter.ToFix => "Nothing to fix on any page.",
        CrawlFilter.BrokenLinks => "No broken links.",
        _ => "",
    };

    /// <summary>A crawl in progress keeps the page from being let go.</summary>
    public bool HoldsWork => IsCrawling;

    /// <summary>The table as a spreadsheet, for the view to save.</summary>
    public string Csv => SiteCrawl.Csv(all);

    /// <summary>A name for the spreadsheet: the site and the day, so two crawls don't overwrite each other.</summary>
    public string CsvName => string.Create(CultureInfo.InvariantCulture, $"{Domains.Of(Address) ?? "site"} crawl {DateTime.Today:yyyy-MM-dd}.csv");

    private bool CanStart() => IsIdle && SiteCrawl.Start(Address) is not null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Start()
    {
        if (SiteCrawl.Start(Address) is not { } start)
            return;
        stop?.Dispose();
        stop = new CancellationTokenSource();
        IsCrawling = true;
        Message = "";
        MessageIsProblem = false;
        Summary = "";
        Progress = "Starting…";
        Show([]);
        try
        {
            var progress = new Progress<CrawlProgress>(p => Progress = p.LinksChecked > 0
                ? $"{p.PagesRead:N0} pages read. Checking links: {p.LinksChecked:N0} done."
                : $"{p.PagesRead:N0} pages read, {p.PagesWaiting:N0} waiting.");
            var result = await services.Crawler.CrawlAsync(start, PageLimit, progress, stop.Token);
            ShowResult(result);
        }
        finally
        {
            IsCrawling = false;
            Progress = "";
        }
    }

    private bool CanStop() => IsCrawling;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => stop?.Cancel();

    [RelayCommand]
    private void ShowOnly(CrawlFilter filter)
    {
        Filter = filter;
        Show(all);
    }

    /// <summary>Shows what a crawl found.</summary>
    public void ShowResult(CrawlResult result)
    {
        Summary = SiteCrawl.Summary(result.Rows, result.StoppedAtLimit) + (result.Cancelled ? " Stopped before the end." : "");
        var first = result.Rows.Count > 0 ? result.Rows[0] : null;
        if (result.Rows.Count == 1 && first is { Page.Status: 0 })
        {
            Message = "The site didn't answer: " + (first.Page.Problem ?? "no reply") + ".";
            MessageIsProblem = true;
        }
        else
        {
            Message = "";
            MessageIsProblem = false;
        }
        // Start on what needs doing; when nothing does, show everything rather than an empty table.
        Filter = result.Rows.Any(r => r.Problems.Count > 0) ? CrawlFilter.ToFix : CrawlFilter.All;
        Show(result.Rows);
    }

    private void Show(IReadOnlyList<CrawlRow> rows)
    {
        all = rows;
        var home = rows.Count > 0 ? rows[0].Page.Url.Host : "";
        Rows.Clear();
        foreach (var row in rows.Where(Fits))
            Rows.Add(new CrawlRowView(row, home));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(NothingToShow));
        OnPropertyChanged(nameof(NothingText));
    }

    private bool Fits(CrawlRow row) => Filter switch
    {
        CrawlFilter.ToFix => row.Problems.Count > 0,
        CrawlFilter.BrokenLinks => row.BrokenLinks.Count > 0,
        _ => true,
    };

    public void SavedAs(string name)
    {
        Message = $"Saved as {name}.";
        MessageIsProblem = false;
    }

    public void Dispose()
    {
        stop?.Cancel();
        stop?.Dispose();
        stop = null;
    }
}

/// <summary>A page in the crawl's table, worded for the screen.</summary>
public sealed class CrawlRowView(CrawlRow row, string home)
{
    public CrawlRow Row { get; } = row;
    public string Address => Row.Page.Url.ToString();

    /// <summary>The part after the domain, which is what tells pages apart down a list.
    /// A page under another form of the domain (www, or without) keeps its host, so it doesn't read as the same page.</summary>
    public string Path => string.Equals(Row.Page.Url.Host, home, StringComparison.OrdinalIgnoreCase) ? Row.Page.Url.PathAndQuery : Row.Page.Url.Host + Row.Page.Url.PathAndQuery;

    public string Title => Row.Page.Facts?.Title is { Length: > 0 } title ? title : Row.Page.RedirectedTo is not null ? "Sends visitors elsewhere" : "No title";
    public bool Untitled => Row.Page.Facts is { Title.Length: 0 };
    public string Status => Row.Page.Status == 0 ? "No answer" : string.Create(CultureInfo.InvariantCulture, $"{Row.Page.Status} {SiteCrawl.StatusWords(Row.Page.Status)}");
    public bool StatusIsProblem => Row.Page.Status is 0 or >= 400;
    public IReadOnlyList<string> Problems => Row.Problems;
    public bool IsFine => Row.Problems.Count == 0;
    public string FoundOn => Row.Page.FoundOn is { } on && StatusIsProblem ? $"Linked from {on.PathAndQuery}" : "";
    public bool HasFoundOn => FoundOn.Length > 0;
}
