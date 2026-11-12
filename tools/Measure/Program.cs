using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Webignite.Core;
using Webignite.Core.Clients;
using Webignite.Core.Documents;
using Webignite.Data;
using Webignite.Desktop;
using Webignite.Desktop.ViewModels;
using Webignite.Desktop.Views;
using Webignite.Xero.Auth;

// Usage: dotnet run --project tools/Measure -c Release
// Light by design, measured: how long the app takes to open and how much memory it holds, and whether every
// page gives its memory back when it is let go. Uses a throwaway database with a year's worth of made-up work.
var limits = JsonSerializer.Deserialize<Limits>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "limits.json")),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
var data = Directory.CreateTempSubdirectory("webignite-measure-").FullName;
var key = RandomNumberGenerator.GetBytes(32);
var store = new LocalStore(Path.Combine(data, "measure.db"), new Vault(key));
var services = new AppServices(store) { TokenStore = new MemoryTokenStore(), VaultKeySource = () => key };
services.Settings.Backups = new Webignite.Core.Backups.BackupSettings { Folder = Path.Combine(data, "backups") };
services.Settings.Appearance = new AppearanceSettings { Motion = Motion.Off };
App.Services = services;

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

MadeUpWork.Fill(store);

var failures = new List<string>();
var report = new StringBuilder("| Measure | Value | Limit |\n| --- | --- | --- |\n");
void Line(string what, string value, string limit = "") => report.Append($"| {what} | {value} | {limit} |\n");
void Check(string what, bool ok)
{
    Console.WriteLine((ok ? "OK    " : "FAIL  ") + what);
    if (!ok)
        failures.Add(what);
}

// Opening: from building the main screen to its first drawn frame.
var clock = Stopwatch.StartNew();
var (vm, window) = Open();
var startMs = clock.ElapsedMilliseconds;
var startBytes = Settle();
Line("Start-up to first frame", $"{startMs} ms", $"{limits.StartMilliseconds} ms");
Line("Managed memory after start", Mb(startBytes), $"{limits.StartManagedMegabytes} MB");
Check($"Start-up took {startMs} ms (limit {limits.StartMilliseconds} ms)", startMs <= limits.StartMilliseconds);
Check($"Memory after start is {Mb(startBytes)} (limit {limits.StartManagedMegabytes} MB)", startBytes <= limits.StartManagedMegabytes * 1024L * 1024);
Check("Only Overview is built at start", vm.Open.Where(h => h.IsBuilt).Select(h => h.Key).SequenceEqual([Page.Overview]));
// A new install has the dock as its menu, so this is the app as most people start it. Starting with the dock once
// crashed, as the dock was filled before a page was picked; this would have caught it.
Check("The app starts with the dock, holding the usual pages with Overview lit",
    vm.MenuPart.IsDockMenu && vm.MenuPart.Dock.Count == Webignite.Core.Layout.DockPages.Usual.Count && vm.MenuPart.Dock.Single(e => e.IsCurrent).PageKey == "Overview");

// Every page in turn: open it, draw it, leave it, let it go, and check it is really gone. The first round also
// pays one-off costs (screens compiled, styles and fonts read); a second round shows whether anything is left
// behind each time, which is what a leak looks like.
var firstRoundBytes = 0L;
for (var round = 1; round <= 2; round++)
{
    foreach (var module in vm.Modules.Where(m => m.Key != Page.Overview).ToList())
    {
        var (page, view) = OpenAndLeave(vm, window, module);
        var kept = module.Main.IsBuilt;
        var after = Settle();
        var gone = !page.IsAlive && !view.IsAlive;
        if (round == 1)
            Line($"{module.Title}: opened and let go", kept ? "kept (holds work)" : gone ? $"freed, {Mb(after)}" : $"STILL IN MEMORY, {Mb(after)}");
        if (!kept)
            Check($"{module.Title} gives its memory back when let go (round {round})", gone);
    }
    if (round == 1)
        firstRoundBytes = Settle();
}

// Measured straight after the second round, before the checks below open tabs of their own.
var endBytes = Settle();
var firstGrowth = firstRoundBytes - startBytes;
var repeatGrowth = endBytes - firstRoundBytes;
Line("Growth after the first round of every page (one-off costs)", Mb(firstGrowth), $"{limits.FirstRoundGrowthMegabytes} MB");
Line("Growth after a second round (left behind each time)", Mb(repeatGrowth), $"{limits.RepeatGrowthMegabytes} MB");
Check($"First round of every page grew memory by {Mb(firstGrowth)} (limit {limits.FirstRoundGrowthMegabytes} MB)",
    firstGrowth <= limits.FirstRoundGrowthMegabytes * 1024L * 1024);
Check($"Opening and closing every page again left {Mb(repeatGrowth)} behind (limit {limits.RepeatGrowthMegabytes} MB)",
    repeatGrowth <= limits.RepeatGrowthMegabytes * 1024 * 1024);
// Copies opened beside the first (two Mails, two Clients) must go too when closed.
foreach (var twice in new[] { Page.Mail, Page.Clients, Page.Files })
{
    var (page, view) = OpenAnotherAndClose(vm, window, twice);
    Settle();
    Check($"A second {vm[twice].Title} gives its memory back when closed", !page.IsAlive && !view.IsAlive);
}

// A page popped out into a window of its own must go, window and all, when that window is closed.
{
    var (page, popped) = PopOutAndClose(vm, window);
    Settle();
    Check("A popped-out window gives its memory back when closed", !page.IsAlive && !popped.IsAlive);
}

// A page shown beside the main one must go when the side is closed.
{
    var page = ShowBesideAndClose(vm, window);
    Settle();
    Check("A page side by side gives its memory back when closed", !page.IsAlive);
}

// Each small tool opens in a window of its own; closing it must let go of the tool and its window.
foreach (var kind in Enum.GetValues<ToolKind>())
{
    var (tools, toolWindow) = OpenToolAndClose(window, services, kind);
    Settle();
    Check($"The {ToolWindow.TitleOf(kind)} window gives its memory back when closed", !tools.IsAlive && !toolWindow.IsAlive);
}

// The app's own Open and Save box holds an explorer; closing it must let go of it.
{
    var (picker, pickerWindow) = OpenPickerAndClose(window, services);
    Settle();
    Check("The Open and Save box gives its memory back when closed", !picker.IsAlive && !pickerWindow.IsAlive);
}

Line("Process working set (for reference)", Mb(Process.GetCurrentProcess().WorkingSet64));

Console.WriteLine();
Console.WriteLine(report);
if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
    File.AppendAllText(summary, "## Light by design\n\n" + report + "\n");
window.Close();
services.Dispose();
if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} check(s) failed: {string.Join("; ", failures)}");
    return 1;
}
return 0;

[MethodImpl(MethodImplOptions.NoInlining)]
static (WeakReference Tools, WeakReference Window) OpenToolAndClose(MainWindow window, AppServices services, ToolKind kind)
{
    var shown = window.ShowTool(kind, services);
    Dispatcher.UIThread.RunJobs();
    shown.CaptureRenderedFrame()?.Dispose();
    var refs = (new WeakReference(shown.Tools), new WeakReference(shown));
    shown.Close();
    Draw(window);
    return refs;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static (WeakReference Picker, WeakReference Window) OpenPickerAndClose(MainWindow window, AppServices services)
{
    var picker = new FilePickerViewModel(services, PickKind.Files, "Open", ["*.pdf"]);
    var shown = new FilePickerWindow { DataContext = picker };
    shown.Show(window);
    Dispatcher.UIThread.RunJobs();
    shown.CaptureRenderedFrame()?.Dispose();
    var refs = (new WeakReference(picker), new WeakReference(shown));
    picker.CancelCommand.Execute(null);
    Draw(window);
    return refs;
}

// Not inlined, so nothing in the caller keeps the page alive by accident. Both the page and its screen must go:
// a screen held by the window (a handler left on it, say) keeps everything it shows.
[MethodImpl(MethodImplOptions.NoInlining)]
static (WeakReference Page, WeakReference View) OpenAndLeave(MainViewModel vm, MainWindow window, Module module)
{
    vm.Selected = module;
    Draw(window);
    var page = new WeakReference(module.Main.Page);
    var view = new WeakReference(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
        .OfType<Avalonia.Controls.UserControl>().FirstOrDefault(v => ReferenceEquals(v.DataContext, module.Main.Page)));
    vm.Selected = vm[Page.Overview];
    Draw(window);
    vm.ReleaseIdle(TimeSpan.Zero);
    Draw(window);
    return (page, view);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static (WeakReference Page, WeakReference View) OpenAnotherAndClose(MainViewModel vm, MainWindow window, Page key)
{
    var host = vm.OpenAnother(key);
    Draw(window);
    var page = new WeakReference(host.Page);
    var view = new WeakReference(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
        .OfType<Avalonia.Controls.UserControl>().FirstOrDefault(v => ReferenceEquals(v.DataContext, host.Page)));
    if (!vm.Close(host))
        throw new InvalidOperationException($"The second {key} wouldn't close.");
    Draw(window);
    return (page, view);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static (WeakReference Page, WeakReference Window) PopOutAndClose(MainViewModel vm, MainWindow window)
{
    var host = vm.OpenInNewTab(vm[Page.Clients]);
    if (host.IsMain)
        host = vm.OpenInNewTab(vm[Page.Clients]);
    Draw(window);
    vm.PopOut(host);
    Draw(window);
    var popped = window.PopOutWindows.Single();
    Dispatcher.UIThread.RunJobs();
    popped.CaptureRenderedFrame()?.Dispose();
    var page = new WeakReference(host.Page);
    var poppedRef = new WeakReference(popped);
    popped.Close();
    Draw(window);
    return (page, poppedRef);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference ShowBesideAndClose(MainViewModel vm, MainWindow window)
{
    var host = vm.OpenInNewTab(vm[Page.Clients]);
    if (host.IsMain)
        host = vm.OpenInNewTab(vm[Page.Clients]);
    Draw(window);
    vm.ShowBeside(host, onLeft: false);
    Draw(window);
    var page = new WeakReference(host.Page);
    vm.CloseBeside();
    Draw(window);
    return page;
}

static (MainViewModel, MainWindow) Open()
{
    var vm = new MainViewModel(App.Services!);
    var window = new MainWindow { DataContext = vm, Width = 1280, Height = 860 };
    window.Show();
    Draw(window);
    return (vm, window);
}

static void Draw(MainWindow window)
{
    Dispatcher.UIThread.RunJobs();
    window.CaptureRenderedFrame()?.Dispose();
    Dispatcher.UIThread.RunJobs();
}

// Memory held once everything that can be freed has been.
static long Settle()
{
    for (var i = 0; i < 3; i++)
    {
        Dispatcher.UIThread.RunJobs();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
    }
    return GC.GetTotalMemory(forceFullCollection: true);
}

static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:0.0} MB";

internal sealed record Limits(int StartMilliseconds, int StartManagedMegabytes, int FirstRoundGrowthMegabytes, double RepeatGrowthMegabytes);

/// <summary>About a year of work for a small studio: clients, quotes, invoices and to-dos.</summary>
internal static class MadeUpWork
{
    public static void Fill(LocalStore store)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var random = new Random(4);
        var clients = Enumerable.Range(1, 60).Select(i => new Client
        {
            Id = $"c{i}",
            Name = $"Client {i:00} (Pty) Ltd",
            Email = $"accounts@client{i:00}.co.za",
            ContactPerson = $"Person {i}",
        }).ToList();
        foreach (var client in clients)
            store.SaveClient(client);
        for (var i = 0; i < 400; i++)
        {
            var client = clients[random.Next(clients.Count)];
            var kind = i % 4 == 0 ? DocumentKind.Quote : DocumentKind.Invoice;
            var document = DocumentDefaults.Complete(new Document
            {
                Kind = kind,
                Origin = DocumentOrigin.New,
                Customer = new Customer { Name = client.Name, Email = client.Email },
                Date = today.AddDays(-random.Next(365)),
                Lines = Enumerable.Range(0, 1 + random.Next(4))
                    .Select(l => new Line { Description = $"Work item {l + 1}", Quantity = 1 + random.Next(5), UnitPrice = 250 * (1 + random.Next(20)) })
                    .ToList(),
            }, new DocumentSettings(), today);
            store.Save(new StoredDocument
            {
                Id = StoredDocument.NewId(),
                Document = document,
                Status = i % 3 == 0 ? DocumentStatus.Draft : DocumentStatus.InXero,
                Xero = i % 3 == 0 ? new XeroLink() : new XeroLink { Id = $"x{i}", Number = $"{(kind == DocumentKind.Quote ? "QU" : "INV")}-{i:0000}", Status = "AUTHORISED" },
            });
        }
        for (var i = 0; i < 40; i++)
            store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = $"t{i}", Title = $"Follow up {i}", Due = today.AddDays(random.Next(-10, 20)) });
    }
}
