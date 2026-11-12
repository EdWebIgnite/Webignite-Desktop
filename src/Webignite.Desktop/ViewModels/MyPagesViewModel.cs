using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Layout;

namespace Webignite.Desktop.ViewModels;

/// <summary>
/// The person's own pages: each a grid of widgets (My tasks, a project, Today, a note, the calculator and so on)
/// placed, moved and sized as they like, and saved by name in the sidebar under My pages. With no page open it
/// lists them. Widgets are only built while their page is open, and let go when it closes.
/// </summary>
public sealed partial class MyPagesViewModel : ViewModelBase, IRefreshable, IHoldsWork, ITabTitle, IDisposable
{
    private readonly AppServices services;
    private readonly DispatcherTimer typing = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private MyPage? page;
    private MyPage? before;
    private ToolsViewModel? tools;
    private bool nameTyped;

    public MyPagesViewModel(AppServices services)
    {
        this.services = services;
        typing.Tick += (_, _) => KeepName();
        services.MyPagesChanged += ElsewhereChanged;
        ListPages();
    }

    /// <summary>One set of tools for the page's calculator, clipboard and colour widgets.</summary>
    public ToolsViewModel Tools => tools ??= new ToolsViewModel(services);

    public string? TabTitle => page?.Name;

    /// <summary>The id of the page on screen, or null for the list of pages. The sidebar marks the matching entry.</summary>
    public string? Showing => page?.Id;

    public bool HoldsWork => nameTyped || Widgets.Any(w => w.Body is NoteBody { HasTyped: true });

    // The list of pages

    public ObservableCollection<PageCard> Cards { get; } = [];

    public bool IsList => page is null;
    public bool IsPage => page is not null;
    public bool NoPages => IsList && Cards.Count == 0;

    private void ListPages()
    {
        Cards.Clear();
        foreach (var saved in services.Settings.MyPages)
            Cards.Add(new PageCard(saved, this));
        OnPropertyChanged(nameof(NoPages));
    }

    [RelayCommand]
    public void NewPage()
    {
        KeepAll();
        var made = MyPages.NewPage(services.Settings.MyPages);
        services.Settings.MyPages = [.. services.Settings.MyPages, made];
        services.ChangedMyPages(this);
        ListPages();
        Show(made);
        IsEditing = true;
    }

    public void OpenPage(string id)
    {
        KeepAll();
        if (services.Settings.MyPages.FirstOrDefault(p => p.Id == id) is { } saved)
            Show(saved);
    }

    [RelayCommand]
    public void ShowList()
    {
        KeepAll();
        Close();
        ListPages();
    }

    // The page on screen

    public ObservableCollection<WidgetViewModel> Widgets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    public partial string EditName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditText), nameof(ExtraRows))]
    public partial bool IsEditing { get; set; }

    public string EditText => IsEditing ? "Done" : "Arrange";

    /// <summary>Room below the widgets while arranging, to drag one lower down.</summary>
    public int ExtraRows => IsEditing ? 4 * MyPages.Fine : 0;

    public bool IsEmptyPage => IsPage && Widgets.Count == 0;

    private bool loading;

    private void Show(MyPage shown)
    {
        Close();
        page = MyPages.Reconcile(shown);
        loading = true;
        EditName = page.Name;
        loading = false;
        foreach (var widget in page.Widgets)
            Widgets.Add(new WidgetViewModel(widget, WidgetBody.For(widget, services, this), this));
        Changed();
    }

    private void Close()
    {
        foreach (var widget in Widgets)
            widget.Dispose();
        Widgets.Clear();
        page = null;
        IsEditing = false;
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(IsList));
        OnPropertyChanged(nameof(IsPage));
        OnPropertyChanged(nameof(NoPages));
        OnPropertyChanged(nameof(IsEmptyPage));
        OnPropertyChanged(nameof(Showing));
        OnPropertyChanged(nameof(TabTitle));
    }

    [RelayCommand]
    private void ToggleEditing()
    {
        KeepName();
        IsEditing = !IsEditing;
    }

    partial void OnEditNameChanged(string value)
    {
        if (loading || page is null)
            return;
        nameTyped = true;
        typing.Stop();
        typing.Start();
    }

    private void KeepName()
    {
        typing.Stop();
        if (!nameTyped || page is null)
            return;
        nameTyped = false;
        Save(MyPages.Rename(page, EditName));
    }

    /// <summary>The running projects, to pick one for a project widget.</summary>
    public IReadOnlyList<(string Id, string Name)> ProjectChoices() =>
        [.. Webignite.Core.Projects.ProjectRules.Listed(services.Projects.Projects(), showDone: false).Select(p => (p.Id, p.Name))];

    /// <summary>The clients by name, to pick one for a client widget.</summary>
    public IReadOnlyList<(string Id, string Name)> ClientChoices() =>
        [.. services.Clients.List().OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).Select(c => (c.Id, c.Name))];

    /// <summary>Adds a widget in the first free spot, and shows it.</summary>
    public void AddWidget(WidgetKind kind, string? target = null)
    {
        if (page is null)
            return;
        var added = MyPages.Add(page, kind, target);
        var widget = added.Widgets[^1];
        Widgets.Add(new WidgetViewModel(widget, WidgetBody.For(widget, services, this), this));
        Save(added);
        OnPropertyChanged(nameof(IsEmptyPage));
    }

    public void RemoveWidget(WidgetViewModel widget)
    {
        if (page is null)
            return;
        var was = page;
        widget.Body.Keep();
        Widgets.Remove(widget);
        widget.Dispose();
        Save(MyPages.Remove(page, widget.Id));
        OnPropertyChanged(nameof(IsEmptyPage));
        services.OfferUndo($"Removed {widget.Title}", () =>
        {
            if (page?.Id != was.Id)
                return;
            Save(was);
            Show(was);
        });
    }

    /// <summary>A note widget's words, kept with the page.</summary>
    public void SetText(string widgetId, string text)
    {
        if (page is not null)
            Save(MyPages.SetText(page, widgetId, text));
    }

    /// <summary>The Files widget's folder, kept with the page.</summary>
    public void SetTarget(string widgetId, string target)
    {
        if (page is not null)
            Save(MyPages.SetTarget(page, widgetId, target));
    }

    // Dragging and sizing: the page as it was when the drag started, each step worked out from it, saved on letting go.

    public void BeginArranging() => before = page;

    public void MoveTo(WidgetViewModel widget, int column, int row)
    {
        if (before is not null)
            Place(MyPages.Move(before, widget.Id, column, row));
    }

    public void ResizeTo(WidgetViewModel widget, int width, int height)
    {
        if (before is not null)
            Place(MyPages.Resize(before, widget.Id, width, height));
    }

    public void EndArranging()
    {
        if (before is null || page is null)
            return;
        before = null;
        Save(page);
    }

    private void Place(MyPage arranged)
    {
        page = arranged;
        foreach (var widget in Widgets)
        {
            if (arranged.Widgets.FirstOrDefault(w => w.Id == widget.Id) is { } placed)
                widget.Place(placed);
        }
    }

    [RelayCommand]
    private void DeletePage()
    {
        if (page is null)
            return;
        KeepAll();
        var all = services.Settings.MyPages;
        var gone = all.FirstOrDefault(p => p.Id == page.Id) ?? page;
        services.Settings.MyPages = [.. all.Where(p => p.Id != gone.Id)];
        services.ChangedMyPages(this);
        ShowList();
        services.OfferUndo($"Deleted {gone.Name}", () =>
        {
            var index = Math.Min(all.ToList().FindIndex(p => p.Id == gone.Id), services.Settings.MyPages.Count);
            var restored = services.Settings.MyPages.ToList();
            restored.Insert(Math.Max(0, index), gone);
            services.Settings.MyPages = restored;
            services.ChangedMyPages(this);
            ListPages();
            Show(gone);
        });
    }

    private void Save(MyPage changed)
    {
        page = changed;
        var all = services.Settings.MyPages.ToList();
        var at = all.FindIndex(p => p.Id == changed.Id);
        if (at < 0)
            all.Add(changed);
        else
            all[at] = changed;
        services.Settings.MyPages = all;
        services.ChangedMyPages(this);
        OnPropertyChanged(nameof(TabTitle));
    }

    // Another copy of this page, or the sidebar, changed the pages.
    private void ElsewhereChanged(object? by)
    {
        if (ReferenceEquals(by, this))
            return;
        if (page is null)
        {
            ListPages();
            return;
        }
        KeepAll();
        if (services.Settings.MyPages.FirstOrDefault(p => p.Id == page.Id) is { } fresh)
            Show(fresh);
        else
            ShowList();
    }

    public void Refresh()
    {
        if (page is null)
            ListPages();
        foreach (var widget in Widgets)
            widget.Body.Refresh();
    }

    private void KeepAll()
    {
        KeepName();
        foreach (var widget in Widgets)
            widget.Body.Keep();
    }

    /// <summary>Saves the page's name and notes as typed; called before the app closes.</summary>
    public void Keep() => KeepAll();

    public void Dispose()
    {
        KeepAll();
        services.MyPagesChanged -= ElsewhereChanged;
        typing.Stop();
        foreach (var widget in Widgets)
            widget.Dispose();
        Widgets.Clear();
        tools?.Dispose();
        tools = null;
    }
}

/// <summary>A page in the list of pages.</summary>
public sealed partial class PageCard(MyPage page, MyPagesViewModel pages)
{
    public string Name => page.Name;

    public string About => page.Widgets.Count switch
    {
        0 => "Empty",
        1 => "1 widget: " + MyPages.Label(page.Widgets[0].Kind),
        _ => $"{page.Widgets.Count} widgets: " + string.Join(", ", page.Widgets.Take(4).Select(w => MyPages.Label(w.Kind))) + (page.Widgets.Count > 4 ? " and more" : ""),
    };

    [RelayCommand]
    private void Open() => pages.OpenPage(page.Id);
}

/// <summary>A widget on the page on screen: where it sits on the grid, and what it shows.</summary>
public sealed partial class WidgetViewModel : ObservableObject, IDisposable
{
    private readonly MyPagesViewModel page;

    public WidgetViewModel(Widget widget, WidgetBody body, MyPagesViewModel page)
    {
        this.page = page;
        Body = body;
        Id = widget.Id;
        Kind = widget.Kind;
        Place(widget);
        body.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WidgetBody.Title))
                OnPropertyChanged(nameof(Title));
        };
    }

    public string Id { get; }
    public WidgetKind Kind { get; }
    public WidgetBody Body { get; }

    public string Title => Body.Title ?? MyPages.Label(Kind);

    [ObservableProperty] public partial int Column { get; private set; }
    [ObservableProperty] public partial int Row { get; private set; }
    [ObservableProperty] public partial int Width { get; private set; }
    [ObservableProperty] public partial int Height { get; private set; }

    public void Place(Widget widget)
    {
        Column = widget.Column;
        Row = widget.Row;
        Width = widget.Width;
        Height = widget.Height;
    }

    [RelayCommand]
    private void Remove() => page.RemoveWidget(this);

    public void Dispose() => Body.Dispose();
}
