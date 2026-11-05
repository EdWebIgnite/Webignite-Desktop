using System.Security.Cryptography;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Webignite.Core.Documents;
using Webignite.Data;
using Webignite.Desktop;
using Webignite.Desktop.ViewModels;
using Webignite.Desktop.Views;
using Webignite.Xero.Auth;
using Webignite.Core;

// Usage: dotnet run --project tools/Snapshot -- <output folder>
// Uses a throwaway database with sample documents, never the real one.
var output = args.Length > 0 ? args[0] : "screenshots";
Directory.CreateDirectory(output);
var data = Directory.CreateTempSubdirectory("webignite-snapshot-").FullName;
var snapshotKey = RandomNumberGenerator.GetBytes(32);
var store = new LocalStore(Path.Combine(data, "snapshot.db"), new Vault(snapshotKey));
var services = new AppServices(store) { TokenStore = new MemoryTokenStore(), VaultKeySource = () => snapshotKey };
services.Settings.Backups = new Webignite.Core.Backups.BackupSettings { Folder = Path.Combine(data, "OneDrive", "Webignite backups") };
// Client folders as on Eddie's PC (C:\Projects), with a few files in them, for Files and the client pages.
var projects = Path.Combine(data, "Projects");
services.Settings.Files = new Webignite.Core.Files.FileSettings { ClientRoot = projects };
App.Services = services;

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

Sample("Karoo Coffee Roasters", DocumentKind.Invoice, ("Website hosting, November", 1, 450m), ("Content updates (hours)", 3.5m, 650m));
Sample("Fynbos Physio", DocumentKind.Invoice, ("Monthly SEO", 1, 2800m));
store.Save(store.List(DocumentKind.Invoice)[0] with { Status = DocumentStatus.InXero, Xero = store.List(DocumentKind.Invoice)[0].Xero with { Number = "INV-0107" } });
Sample("Fynbos Physio", DocumentKind.Invoice, ("Monthly SEO, September", 1, 2800m));
Sample("Bosveld Builders", DocumentKind.Invoice, ("Website deposit", 1, 7250m));
{
    var invoices = store.List(DocumentKind.Invoice).Where(d => d.Status == DocumentStatus.Draft).ToList();
    var fynbos = invoices.First(d => d.Document.Lines[0].Description.Contains("September"));
    store.Save(fynbos with {
        Status = DocumentStatus.InXero,
        Document = fynbos.Document with { DueOrExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-9) },
        Xero = fynbos.Xero with
        {
            Number = "INV-0103",
            Id = "x103",
            Status = "AUTHORISED",
            AmountDue = 1300m,
            AmountPaid = 1500m,
            Checked = DateTimeOffset.Now,
        },
        Delivery = fynbos.Delivery with
        {
            Emailed = DateTimeOffset.Now.AddDays(-39),
            EmailedTo = "Lerato Dlamini",
        },
});
    var bosveldInvoice = invoices.First(d => d.Document.Customer.Name == "Bosveld Builders");
    store.Save(bosveldInvoice with {
        Status = DocumentStatus.InXero,
        Settled = new SettledState { On = DateTimeOffset.Now, As = Settlement.Paid },
        Xero = bosveldInvoice.Xero with
        {
            Number = "INV-0105",
            Id = "x105",
            Status = "PAID",
            AmountDue = 0m,
            AmountPaid = 7250m,
            Checked = DateTimeOffset.Now,
            PaidOn = DateOnly.FromDateTime(DateTime.Today),
        },
});
}
Sample("Bosveld Builders", DocumentKind.Quote, ("New website, 6 pages", 1, 14500m), ("Logo refresh", 1, 3200m));

services.Settings.PriceList = PriceList.Save(PriceList.Save(PriceList.Save([], "Website, 6 pages",
    [new Line { Description = "Design and build, 6 pages", Quantity = 1, UnitPrice = 12500m }, new Line { Description = "Hosting, first year", Quantity = 1, UnitPrice = 2000m }]),
    "Monthly SEO", [new Line { Description = "Monthly SEO", Quantity = 1, UnitPrice = 2800m }]),
    "Hosting, yearly", [new Line { Description = "Website hosting, 12 months", Quantity = 1, UnitPrice = 2400m }]);
// Pages change at once here: the script doesn't wait for fades to finish.
services.Settings.Appearance = new AppearanceSettings { Motion = Motion.Off };
var vm = new MainViewModel(services);
var window = new MainWindow { DataContext = vm, Width = 1280, Height = 860 };
window.Show();

void Open(Page page) => vm.Selected = vm[page];

// Checks along the way. Each failure is listed, and the run ends with an error so CI shows it.
var failures = new List<string>();
void Check(string what, bool ok)
{
    Console.WriteLine((ok ? "OK    " : "FAIL  ") + what);
    if (!ok)
        failures.Add(what);
}

// Methods of their own, so the tools assembly is only read when they run, as in the app.
static int PdfPageCount(string path) => Webignite.Tools.PdfText.Pages(File.ReadAllBytes(path));

static string PdfReadText(string path) => Webignite.Tools.PdfText.Read(File.ReadAllBytes(path));

// A method of its own, so the tools assembly is only read when this runs, as in the app.
static void ShrinkPhotos(string photos, ToolsViewModel tools)
{
    long before = 0, after = 0;
    foreach (var photo in Directory.GetFiles(photos).Order())
    {
        var shrunk = Webignite.Tools.ImageShrinker.Shrink(photo, new Webignite.Core.Tools.ImageJob());
        if (shrunk.Problem is not null)
            throw new InvalidOperationException(shrunk.Problem);
        Console.WriteLine($"Shrunk {Path.GetFileName(photo)} to {shrunk.Output} {shrunk.Width}x{shrunk.Height}");
        before += shrunk.Before;
        after += shrunk.After;
        tools.ImageResults.Add(new ImageResultRow(shrunk));
        tools.ImageFolder = Path.GetDirectoryName(shrunk.Output);
    }
    tools.ImageMessage = $"2 images saved in the web folder: {Webignite.Core.Tools.ImageRules.Saving(before, after)}.";
}

// Light by design: opening the app builds Overview and nothing else, and reads no heavy tool from disk.
Dispatcher.UIThread.RunJobs();
Check("Opening the app builds Overview and no other page", vm.Open.Where(h => h.IsBuilt).Select(h => h.Key).SequenceEqual([Page.Overview]));
{
    string[] heavy = ["Webignite.Tools", "Docnet.Core", "QRCoder", "HtmlRenderer", "Avalonia.HtmlRenderer"];
    var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "").Where(heavy.Contains).ToList();
    Check("Opening the app loads no heavy tool" + (loaded.Count > 0 ? " (loaded: " + string.Join(", ", loaded) + ")" : ""), loaded.Count == 0);
}

// Waits for work that finishes off the screen's thread, keeping the screen's own jobs running meanwhile.
void Wait(Task task, string what)
{
    var until = DateTime.UtcNow.AddSeconds(10);
    while (!task.IsCompleted && DateTime.UtcNow < until)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
    Dispatcher.UIThread.RunJobs();
    Check(what, task.IsCompletedSuccessfully);
}

// Runs the screen's jobs until something is true, or ten seconds pass. Says whether it came true.
bool Until(Func<bool> done)
{
    var until = DateTime.UtcNow.AddSeconds(10);
    while (!done() && DateTime.UtcNow < until)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(15);
    }
    Dispatcher.UIThread.RunJobs();
    return done();
}

void Save(string name)
{
    Dispatcher.UIThread.RunJobs();
    using var frame = window.CaptureRenderedFrame();
    using var file = File.Create(Path.Combine(output, name + ".png"));
    frame?.Save(file, new PngBitmapEncoderOptions());
    Console.WriteLine("Saved " + name);
}

Save("overview-start");

vm.ToggleShortcutsCommand.Execute(null);
Save("shortcuts");
vm.HideShortcutsCommand.Execute(null);

// Working states: a background job in the sidebar and the payments check on Overview.
using (services.Work.Start("Checking mail"))
{
    vm.Overview.CheckingPayments = true;
    Save("working");
    vm.Overview.CheckingPayments = false;
}

// Empty pages, before the sample data for them exists.
Open(Page.Retainers);
Save("empty-retainers");
Open(Page.Renewals);
Save("empty-domains");

Open(Page.Invoices);
Save("invoices");
vm.Invoices.Search = "karoo";
vm.Invoices.Show = DocumentShow.NotInXero;
Save("invoices-filtered");
vm.Invoices.Search = "nothing like this";
Save("invoices-no-match");
vm.Invoices.ClearFiltersCommand.Execute(null);

// Keys on the list: Down twice from the search box, then Enter opens the row and Esc goes back.
{
    Dispatcher.UIThread.RunJobs();
    var searchBox = window.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().First(t => t.IsEffectivelyVisible);
    searchBox.Focus();
    window.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowDown, null);
    window.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowDown, null);
    Save("invoices-keys");
    window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
    Dispatcher.UIThread.RunJobs();
    Check("Enter opened a row", !vm.Invoices.IsListing);
    window.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
    Dispatcher.UIThread.RunJobs();
    Check("Esc went back", vm.Invoices.IsListing);
}
Open(Page.Quotes);
Save("quotes");
vm.Quotes.OpenDocument(store.List(DocumentKind.Quote)[0]);
Save("quote-editor");
vm.Quotes.Editor!.MakeInvoiceCommand.Execute(null);
Save("invoice-from-quote");
vm.Invoices.Editor = null;
vm.Quotes.Editor = null;
store.DeleteDocument(store.List(DocumentKind.Invoice).First(d => d.Document.Customer.Name == "Bosveld Builders").Id);
Open(Page.Invoices);

vm.Invoices.NewCommand.Execute(null);
var invoice = vm.Invoices.Editor!;
invoice.CustomerName = "Karoo Coffee Roasters";
invoice.Reference = "Website November";
invoice.Lines[0].Description = "Website hosting, November";
invoice.Lines[0].Price = "450";
invoice.AddLineCommand.Execute(null);
invoice.Lines[1].Description = "Content updates (hours)";
invoice.Lines[1].Quantity = "3,5";
invoice.Lines[1].Price = "R650";
invoice.Notes = "Please pay by EFT to FNB, account 62xxxxxxx. Thank you!";
invoice.SaveCommand.Execute(null);
Save("invoice-editor");

// The Xero part of the editor: a customer Xero doesn't know, a broken write, and a checked draft.
invoice.ContactQuestion = "Karoo Coffee Roasters isn't in Xero yet. Add them as a new contact, or search in case they are there under another name.";
invoice.OfferNewContact = true;
Save("invoice-xero-contact");
var karoo = store.List(DocumentKind.Invoice).First(d => d.Document.Reference == "Website November");
var uncertain = store.Save(karoo with {
    Xero = karoo.Xero with
    {
        State = Webignite.Core.Xero.XeroWriteState.Uncertain,
        ContactId = "k1",
        Problems = ["The connection to Xero broke during the write. Use Check Xero."],
    },
});
vm.Invoices.Editor = new DocumentEditorViewModel(uncertain, services, () => vm.Invoices.Editor = null);
Save("invoice-xero-uncertain");
var verified = store.Save(uncertain with {
    Status = DocumentStatus.InXero,
    Xero = uncertain.Xero with
    {
        State = Webignite.Core.Xero.XeroWriteState.Verified,
        Id = "3f2a",
        Number = "INV-0111",
        Problems = [],
    },
});
vm.Invoices.Editor = new DocumentEditorViewModel(verified, services, () => vm.Invoices.Editor = null);
Save("invoice-in-xero");
vm.Invoices.Editor.StartEmailCommand.Execute(null);
Save("invoice-approve");
vm.Invoices.Editor = null;

Open(Page.Settings);
Save("settings");

// The sidebar folded and tucked away.
vm.Sidebar.Items.OfType<NavGroup>().First(g => g.Key == "billing").Toggle();
vm.Sidebar.Items.OfType<NavGroup>().First(g => g.Key == "web-tools").Toggle();
Save("sidebar-folded");
vm.Sidebar.Items.OfType<NavGroup>().First(g => g.Key == "billing").Toggle();
vm.Sidebar.Items.OfType<NavGroup>().First(g => g.Key == "web-tools").Toggle();
vm.Sidebar.Toggle();
Save("sidebar-icons");
vm.Sidebar.Toggle();
Save("sidebar-hidden");
vm.Sidebar.Toggle();
vm.Sidebar.Items.OfType<NavGroup>().First(g => g.Key == "planning").MoveUpCommand.Execute(null);
vm[Page.Mail].MoveDownCommand.Execute(null);
vm[Page.Mail].MoveDownCommand.Execute(null);
Save("sidebar-reordered");
vm.Sidebar.ResetCommand.Execute(null);

// Run synchronously here: the headless dispatcher isn't pumped while this script waits.
services.Secrets.Write(AppServices.BackupPasswordName, "a long sample password");
var backupAt = DateTimeOffset.Now.AddMinutes(-20);
var backupFile = Backups.Seal(Backups.Snapshot(store), snapshotKey, "a long sample password", services.BackupFolder, backupAt, "0.2.2", 1000);
services.Settings.Backups = services.Settings.Backups with { LastBackup = backupAt, LastFile = backupFile };
var settingsPage = (SettingsViewModel)vm.Current.Page;
settingsPage.Refresh();
settingsPage.Open("backups");
Save("settings-backups");
settingsPage.Open("emails");
Save("settings-emails");
settingsPage.Open("xero");

// A short to-do list, before the sample to-dos below, so the hint under it shows.
Open(Page.Todo);
Save("todo-short");

var today = DateOnly.FromDateTime(DateTime.Today);
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t1", Title = "Send Bosveld the sitemap for sign-off", Due = today, Created = DateTimeOffset.UtcNow });
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t2", Title = "Renew webignite.co.za SSL certificate", Due = today.AddDays(3), Created = DateTimeOffset.UtcNow });
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t0", Title = "Send Karoo the logo files", Due = today.AddDays(-2), Created = DateTimeOffset.UtcNow });
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t3", Title = "Update portfolio with Fynbos Physio site", Created = DateTimeOffset.UtcNow });
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t4", Title = "Back up client sites", Due = today.AddDays(-1), Done = true, DoneAt = DateTimeOffset.UtcNow, Created = DateTimeOffset.UtcNow });
var overdue = store.List(DocumentKind.Invoice).First(d => d.Xero.Number is not null);
store.Save(overdue with { Document = overdue.Document with { Date = today.AddDays(-21), DueOrExpiryDate = today.AddDays(-14) } });
var quote = store.List(DocumentKind.Quote)[0];
store.Save(quote with { Status = DocumentStatus.InXero, Xero = quote.Xero with { Number = "QU-0031", Status = "SENT" } });
Open(Page.Overview); // away and back, so To-do reads the sample to-dos above
Open(Page.Todo);
Save("todo");

store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e1", Title = "Kick-off with Bosveld Builders", Date = today, Start = new TimeOnly(10, 0), Location = "Teams" });
store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e2", Title = "Fynbos Physio photo shoot", Date = today.AddDays(2), Start = new TimeOnly(14, 30) });
store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e3", Title = "Public holiday", Date = today.AddDays(-5) });
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t5", Title = "Write blog post on site speed", Created = DateTimeOffset.UtcNow });
Open(Page.Calendar);
Save("calendar");
Open(Page.Planner);
Save("planner");
store.SaveClient(new Webignite.Core.Clients.Client
{
    Id = "c1", Name = "Bosveld Builders", ContactPerson = "Pieter van Wyk", Email = "pieter@bosveldbuilders.co.za",
    Phone = "082 555 0142", Website = "bosveldbuilders.co.za", Notes = "Prefers WhatsApp. Site on our xneelo server.",
});
store.SaveClient(new Webignite.Core.Clients.Client { Id = "c2", Name = "Fynbos Physio", Email = "admin@fynbosphysio.co.za" });
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t6", Title = "Get photos of finished projects", Customer = "Bosveld Builders", Due = today.AddDays(4), Created = DateTimeOffset.UtcNow });
store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e4", Title = "Sitemap review", Date = today.AddDays(1), Start = new TimeOnly(9, 0), Customer = "Bosveld Builders" });
var bosveld = store.ListClients().First(c => c.Name == "Bosveld Builders");
bosveld = Webignite.Core.Clients.Onboarding.Start(bosveld, today.AddDays(-12));
foreach (var item in new[] { "Deposit paid", "Logo (SVG or large PNG)", "Brand colours and fonts", "Sitemap signed off" })
    bosveld = Webignite.Core.Clients.Onboarding.Tick(bosveld, item, true, today.AddDays(-9));
var brief = Path.Combine(data, "Bosveld website brief.pdf");
File.WriteAllText(brief, "brief");
bosveld = Webignite.Core.Clients.ClientRecords.AddFiles(bosveld, [brief, Path.Combine(data, "Old contract.docx")], today.AddDays(-12));
bosveld = Webignite.Core.Clients.ClientRecords.AddNote(bosveld, "Kick-off with Pieter. Six pages to start, online booking for site visits in phase two. Budget agreed at R 17 700.", today.AddDays(-12));
bosveld = Webignite.Core.Clients.ClientRecords.AddNote(bosveld, "Pieter sent the logo on WhatsApp. Asked for the photos again.", today.AddDays(-1));
bosveld = bosveld with { HourlyRate = 650m, Projects = [new() { Id = "p1", Name = "New website" }, new() { Id = "p2", Name = "Logo refresh", Finished = true, HourlyRate = 800m }] };
store.SaveClient(bosveld);

// Time: a few days of entries, for a client, a project and nobody, then a running timer.
void Time(DateOnly date, int minutes, string note, string? client = null, string? project = null) =>
    store.SaveTime(new Webignite.Core.Time.TimeEntry { Id = Webignite.Core.Time.TimeEntry.NewId(), Date = date, Minutes = minutes, Note = note,
        ClientId = client, ProjectId = project, Created = DateTimeOffset.Now.AddMinutes(-minutes) });
var weekStart = Dates.WeekStart(today);
Time(today, 95, "Homepage layout", "c1", "p1");
Time(today, 30, "Bookkeeping");
Time(today.AddDays(-1) < weekStart ? today : today.AddDays(-1), 140, "Services pages", "c1", "p1");
Time(today.AddDays(-1) < weekStart ? today : today.AddDays(-1), 45, "Booking page fix on phones", "c2");
Time(today.AddDays(-2) < weekStart ? today : today.AddDays(-2), 60, "Logo options", "c1", "p2");
Time(today.AddDays(-2) < weekStart ? today : today.AddDays(-2), 40, "Learning: Avalonia animations");
vm.Time.Refresh();
Open(Page.Time);
Save("time");
// A draft invoice from Bosveld's time this month, then take it away so later shots are unchanged.
{
    var before = store.List(DocumentKind.Invoice).Select(d => d.Id).ToHashSet();
    vm.Time.InvoiceClient = vm.Time.InvoiceClients.First(c => c.Label == "Bosveld Builders");
    vm.Time.InvoiceMonth = vm.Time.InvoiceMonths[0];
    vm.Time.DraftInvoiceCommand.Execute(null);
    Save("invoice-from-time");
    vm.Invoices.Editor?.BackCommand.Execute(null);
    foreach (var made in store.List(DocumentKind.Invoice).Where(d => !before.Contains(d.Id)))
        store.DeleteDocument(made.Id);
    Open(Page.Time);
    vm.Time.Refresh();
}
vm.Time.ClientChoice = vm.Time.ClientChoices.First(c => c.Label == "Bosveld Builders");
vm.Time.Note = "Contact form";
vm.Time.StartCommand.Execute(null);
var started = store.RunningTimer! with { Started = DateTimeOffset.Now.AddMinutes(-23).AddSeconds(-14) };
services.Timer.Start(started);
Save("time-running");
Open(Page.Overview);
Check("The running timer shows in the sidebar with Time let go", vm.ReleaseIdle(TimeSpan.Zero) > 0 && !vm[Page.Time].Main.IsBuilt && vm.Timer.IsRunning);
Open(Page.Time);
services.Timer.Stop();
vm.Time.Note = "";
Open(Page.Clients);
Save("clients");
Wait(vm.Clients.BringInFromXeroCommand.ExecuteAsync(null), "Bringing clients in from Xero by hand");
Check("Bringing clients in without Xero says to connect it", vm.Clients.XeroMessage == "Connect Xero in Settings first.");
vm.Clients.XeroMessage = "";
vm.Clients.OpenCommand.Execute(vm.Clients.Rows[0]);
Save("client");
window.Height = 1250;
Save("client-tall");
window.Height = 860;
vm.Clients.Detail!.BackCommand.Execute(null);
// Pages that can be open more than once: a second Clients beside the first, each with its own client open.
{
    var first = vm.Clients;
    var second = vm.OpenAnother(Page.Clients);
    Dispatcher.UIThread.RunJobs();
    var copy = (ClientsViewModel)second.Page;
    copy.OpenCommand.Execute(copy.Rows[1]);
    first.OpenCommand.Execute(first.Rows[0]);
    Check("A second Clients opens beside the first, each with its own client",
        !ReferenceEquals(first, copy) && vm.Current == second && first.Detail is { } a && copy.Detail is { } b && a.Title != b.Title);
    Open(Page.Overview);
    Open(Page.Clients);
    Check("The sidebar goes back to the copy used last", vm.Current == second && vm.Clients == copy);
    Check("A copy with a client open isn't closed", !vm.Close(second));
    copy.Detail!.BackCommand.Execute(null);
    // That a closed copy is freed is checked by tools/Measure, which runs in Release where that can be seen.
    Check("Closing the extra copy goes back to the first", vm.Close(second) && vm.Current == vm[Page.Clients].Main && !vm.Open.Contains(second));
    Check("The sidebar's own copy is never closed", !vm.Close(vm[Page.Clients].Main));
    first.Detail!.BackCommand.Execute(null);
}
// Tabs: Ctrl+click (or a middle click) opens a page or a client in a tab of its own.
{
    Open(Page.Overview);
    vm.OpenInNewTab(vm[Page.Mail]);
    services.Go.ClientInNewTab(bosveld);
    vm.OpenInNewTab(vm[Page.Invoices]);
    Dispatcher.UIThread.RunJobs();
    Check("Pages and a client open in tabs of their own, and the strip shows",
        vm.ShowTabs && vm.Tabs.Select(t => t.Key).SequenceEqual([Page.Overview, Page.Mail, Page.Clients, Page.Invoices]));
    Check("A client's tab is called by its name", vm.Tabs[2].Title == "Bosveld Builders");
    Save("tabs");
    window.KeyPress(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.Tab, null);
    Dispatcher.UIThread.RunJobs();
    Check("Ctrl+Tab goes round to the first tab", vm.Current == vm.Tabs[0]);
    vm.Current = vm.Tabs[3];
    Open(Page.Retainers);
    Check("The sidebar changes what the tab on screen shows", vm.Tabs.Count == 4 && vm.Tabs[3].Key == Page.Retainers && vm.Current == vm.Tabs[3]);
    Open(Page.Mail);
    Check("The sidebar goes to a tab already showing that page", vm.Current == vm.Tabs[1] && vm.Tabs[3].Key == Page.Retainers);
    var saved = vm.TabState();
    vm.RestoreTabs(new Webignite.Core.Layout.TabSettings());
    Check("With nothing saved the app opens on one Overview tab", vm.Tabs.Count == 1 && vm.Current.Key == Page.Overview && !vm.ShowTabs);
    vm.RestoreTabs(saved);
    Check("Tabs come back as they were, on the same tab", vm.Tabs.Select(t => t.Key).SequenceEqual([Page.Overview, Page.Mail, Page.Clients, Page.Retainers]) && vm.Current == vm.Tabs[1]);
    vm.Current = vm.Tabs[3];
    window.KeyPress(Avalonia.Input.Key.W, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.W, null);
    Dispatcher.UIThread.RunJobs();
    Check("Ctrl+W closes the tab and the one before it shows", vm.Tabs.Count == 3 && vm.Current == vm.Tabs[2]);
    vm.OpenInNewTab(vm[Page.Clients]);
    services.Go.ClientInNewTab(bosveld);
    var open = vm.Tabs.Last(t => t.Key == Page.Clients);
    Check("A tab with a client open isn't closed until it's put away", !vm.CloseTab(open));
    vm.CloseOtherTabsCommand.Execute(vm.Tabs[0]);
    Check("Closing the other tabs keeps the one with work open", vm.Tabs.Count == 2 && vm.Tabs.Contains(open));
    ((ClientsViewModel)open.Page).Detail!.BackCommand.Execute(null);
    vm.CloseOtherTabsCommand.Execute(vm.Tabs[0]);
    // A client copy put out of the tabs by the restore above still has Bosveld open: put it away too.
    foreach (var host in vm.Open.Where(h => h.IsBuilt && h.Page is ClientsViewModel { Detail: not null }).ToList())
        ((ClientsViewModel)host.Page).Detail!.BackCommand.Execute(null);
    Check("After putting the client away, one tab is left and the strip goes", vm.Tabs.Count == 1 && !vm.ShowTabs && vm.Current.Key == Page.Overview);
}
// Pop-out windows: a tab taken into a window of its own, put back, and closed with work open.
{
    Open(Page.Overview);
    services.Go.ClientInNewTab(bosveld);
    var clientTab = vm.Current;
    vm.PopOutTabCommand.Execute(clientTab);
    Dispatcher.UIThread.RunJobs();
    var popped = window.PopOutWindows.SingleOrDefault();
    Check("A tab opens in a window of its own and leaves the strip",
        popped?.Host == clientTab && !vm.Tabs.Contains(clientTab) && vm.PoppedOut.Contains(clientTab) && vm.Current.Key == Page.Overview);
    if (popped is not null)
    {
        popped.Width = 1100;
        popped.Height = 760;
        Dispatcher.UIThread.RunJobs();
        using var frame = popped.CaptureRenderedFrame();
        frame?.Save(Path.Combine(output, "pop-out.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Console.WriteLine("Saved pop-out");
    }
    Open(Page.Clients);
    Check("The sidebar opens Clients in the main window and leaves the popped-out one be",
        vm.Current.Key == Page.Clients && vm.Current != clientTab && vm.PoppedOut.Contains(clientTab));
    vm.DockBack(clientTab);
    Dispatcher.UIThread.RunJobs();
    Check("Put back, it is a tab again and its window is gone", vm.Tabs.Contains(clientTab) && vm.Current == clientTab && window.PopOutWindows.Count == 0);
    vm.PopOut(clientTab);
    Dispatcher.UIThread.RunJobs();
    window.PopOutWindows.Single().Close();
    Dispatcher.UIThread.RunJobs();
    Check("Closing a popped-out window with a client open puts it back instead", vm.Tabs.Contains(clientTab) && !vm.PoppedOut.Contains(clientTab));
    var state = vm.TabState(h => new Webignite.Core.Layout.PopOutPlacement(h.Key.ToString(), 1, 2, 3, 4));
    ((ClientsViewModel)clientTab.Page).Detail!.BackCommand.Execute(null);
    vm.PopOut(clientTab);
    Dispatcher.UIThread.RunJobs();
    var saved = vm.TabState(h => new Webignite.Core.Layout.PopOutPlacement(h.Key.ToString(), 100, 80, 900, 700));
    Check("A popped-out window is saved with where it is", saved.PoppedOut.SingleOrDefault() is { Page: "Clients", X: 100, Width: 900 } && state.PoppedOut.Count == 0);
    window.PopOutWindows.Single().Close();
    Dispatcher.UIThread.RunJobs();
    // This was Clients' own copy, which stays to be let go when idle; an extra copy would be closed outright.
    Check("Closing a popped-out window with nothing open closes it", !vm.PoppedOut.Contains(clientTab) && !vm.Tabs.Contains(clientTab) && window.PopOutWindows.Count == 0);
    vm.CloseOtherTabsCommand.Execute(vm.Tabs[0]);
}
// Side by side: a client beside the invoices, in the same window.
{
    Open(Page.Invoices);
    services.Go.ClientInNewTab(bosveld);
    var clientTab = vm.Current;
    vm.ShowOnRightCommand.Execute(clientTab);
    Dispatcher.UIThread.RunJobs();
    Check("A tab shown on the right leaves the strip and the main side keeps its page",
        vm.IsSplit && vm.Beside == clientTab && !vm.Tabs.Contains(clientTab) && vm.Current.Key == Page.Invoices);
    // Side by side is for a wide screen: drawn at the width of a desktop monitor.
    window.Width = 1900;
    Save("side-by-side");
    window.Width = 1280;
    Open(Page.Clients);
    Check("The sidebar opens Clients on the main side and leaves the one beside be", vm.Current.Key == Page.Clients && vm.Current != clientTab && vm.Beside == clientTab);
    vm.SwapSidesCommand.Execute(null);
    var saved = vm.TabState();
    Check("Swapped to the left and saved that way", vm.BesideOnLeft && saved.Beside == "Clients" && saved.BesideOnLeft);
    vm.CloseBesideCommand.Execute(null);
    Check("Closing the side with a client open puts it back as a tab", !vm.IsSplit && vm.Tabs.Contains(clientTab));
    ((ClientsViewModel)clientTab.Page).Detail!.BackCommand.Execute(null);
    vm.RestoreTabs(saved);
    Dispatcher.UIThread.RunJobs();
    Check("The split comes back as it was", vm.IsSplit && vm.Beside?.Key == Page.Clients && vm.BesideOnLeft);
    vm.PutBesideBackCommand.Execute(null);
    Check("Back to a tab: the window is whole again", !vm.IsSplit && vm.Tabs.Any(t => t.Key == Page.Clients && t != vm.Current));
    vm.CloseOtherTabsCommand.Execute(vm.Tabs[0]);

    // Each side has its own header: close either side, or send either into a window of its own.
    Open(Page.Invoices);
    vm.OpenInNewTab(vm[Page.Renewals]);
    var renewals = vm.Current;
    vm.ShowOnRightCommand.Execute(renewals);
    Dispatcher.UIThread.RunJobs();
    window.Width = 1900;
    Save("side-by-side-headers");
    window.Width = 1280;
    vm.CloseMainSideCommand.Execute(null);
    Check("Closing the main side leaves the page beside in the whole window", !vm.IsSplit && vm.Current == renewals && vm.Tabs.All(t => t.Key != Page.Invoices));
    vm.OpenInNewTab(vm[Page.Quotes]);
    vm.ShowOnRightCommand.Execute(vm.Current);
    var quotes = vm.Beside!;
    vm.PopOutBesideCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    Check("The page beside goes into a window of its own", !vm.IsSplit && vm.PoppedOut.Contains(quotes) && window.PopOutWindows.Count == 1);
    window.PopOutWindows.Single().Close();
    Dispatcher.UIThread.RunJobs();
    vm.OpenInNewTab(vm[Page.Money]);
    vm.ShowOnLeftCommand.Execute(vm.Current);
    var main = vm.Current;
    vm.PopOutMainSideCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    Check("The main side goes into a window of its own and the page beside takes the window",
        !vm.IsSplit && vm.PoppedOut.Contains(main) && vm.Current.Key == Page.Money);
    window.PopOutWindows.Single().Close();
    Dispatcher.UIThread.RunJobs();
    vm.CloseOtherTabsCommand.Execute(vm.Tabs[0]);
}
var month = Dates.MonthStart(today);
void Entry(Webignite.Core.Finance.EntryKind kind, DateOnly date, decimal amount, string party, string? category = null, string? note = null) =>
    store.SaveEntry(new Webignite.Core.Finance.MoneyEntry { Id = Webignite.Core.Finance.MoneyEntry.NewId(), Kind = kind, Date = date, Amount = amount, Party = party, Category = category, Note = note });
var received = Webignite.Core.Finance.EntryKind.Received;
var expense = Webignite.Core.Finance.EntryKind.Expense;
for (var i = 1; i <= 11; i++)
{
    var m = month.AddMonths(-i);
    Entry(received, m.AddDays(4), 2800m, "Fynbos Physio", note: "Monthly SEO");
    Entry(received, m.AddDays(9), 450m + i * 35, "Karoo Coffee Roasters");
    if (i % 3 == 0) Entry(received, m.AddDays(14), 8000m + i * 400, "Bosveld Builders", note: "Website deposit");
    if (i % 4 == 1) Entry(received, m.AddDays(19), 3500m, "Kalahari Kitchens");
    Entry(expense, m.AddDays(11), 400m + i * 20, "Takealot", "Equipment");
    if (i % 5 == 0) Entry(expense, m.AddDays(16), 6200m, "Freelance designer", "Contractors");
}
Entry(received, month.AddDays(2), 2800m, "Fynbos Physio", note: "INV-0104");
Entry(received, month.AddDays(5), 7250m, "Bosveld Builders", note: "Deposit, QU-0031");
Entry(expense, month.AddDays(6), 349.99m, "Takealot", "Equipment", "USB-C hub");
store.SaveRecurring(new Webignite.Core.Finance.RecurringExpense { Id = "r1", Name = "xneelo dedicated server", Amount = 1899m, DayOfMonth = 1, Category = "Hosting and servers", Starts = month.AddMonths(-11) });
store.SaveRecurring(new Webignite.Core.Finance.RecurringExpense { Id = "r2", Name = "Adobe Creative Cloud", Amount = 1150m, DayOfMonth = 15, Category = "Software and subscriptions", Starts = month.AddMonths(-11) });
store.SaveRecurring(new Webignite.Core.Finance.RecurringExpense { Id = "r3", Name = "Fibre", Amount = 899m, DayOfMonth = 31, Category = "Internet and phone", Starts = month.AddMonths(-11) });
Open(Page.Money);
Save("money");

Webignite.Core.Documents.Line Priced(string description, decimal price) => new() { Description = description, Quantity = 1, UnitPrice = price };
var seo = store.SaveRetainer(new Webignite.Core.Retainers.Retainer
{
    Id = "ret1", ClientName = "Fynbos Physio", Title = "Monthly SEO", DayOfMonth = 1, Starts = month.AddMonths(1),
    Lines = [Priced("SEO and Google Business updates, [Month]", 2800m), Priced("Website hosting, [Month]", 450m)],
});
store.SaveRetainer(new Webignite.Core.Retainers.Retainer
{
    Id = "ret2", ClientName = "Karoo Coffee Roasters", Title = "Hosting", DayOfMonth = 25, Starts = month.AddMonths(1),
    Lines = [Priced("Website hosting and backups, [Month]", 450m)],
});
store.SaveRetainer(new Webignite.Core.Retainers.Retainer
{
    Id = "ret3", ClientName = "Kalahari Kitchens", Title = "Social media", DayOfMonth = 31, Starts = month, Paused = true,
    Lines = [Priced("Social media posts, [Month]", 3500m)],
});
Open(Page.Retainers);
Save("retainers");
var retainersPage = (RetainersViewModel)vm.Current.Page;
retainersPage.Edit(seo);
Save("retainer-editor");
retainersPage.Editor!.CancelCommand.Execute(null);

Webignite.Core.Renewals.Renewal Renewal(string id, string name, Webignite.Core.Renewals.RenewalKind kind, string? client, string provider, DateOnly on, decimal? cost, decimal? price) =>
    store.SaveRenewal(new Webignite.Core.Renewals.Renewal { Id = id, Name = name, Kind = kind, ClientName = client, Provider = provider, RenewsOn = on, Cost = cost, Price = price });
var domain = Webignite.Core.Renewals.RenewalKind.Domain;
Renewal("n1", "karoocoffee.co.za", domain, "Karoo Coffee Roasters", "domains.co.za", today.AddDays(12), 99m, 250m);
Renewal("n2", "bosveldbuilders.co.za", domain, "Bosveld Builders", "domains.co.za", today.AddDays(-2), 99m, 250m);
Renewal("n3", "webignite.co.za", domain, null, "xneelo", today.AddDays(140), 99m, null);
Renewal("n4", "Fynbos Physio hosting", Webignite.Core.Renewals.RenewalKind.Hosting, "Fynbos Physio", "xneelo", today.AddDays(75), null, 1800m);
Renewal("n5", "fynbosphysio.co.za", domain, "Fynbos Physio", "domains.co.za", today.AddDays(200), 99m, 250m);
Open(Page.Renewals);
Save("domains");

// Income projection, once retainers and renewals exist, in each chart style and with typed-in amounts.
Open(Page.Money);
var moneyPage = (MoneyViewModel)vm.Current.Page;
moneyPage.Refresh();
window.Height = 1180;
Save("income-default"); // Lines until a style is picked
moneyPage.ShowChartCommand.Execute(Webignite.Core.Finance.ChartStyle.Bars);
Save("income-bars");
moneyPage.ShowChartCommand.Execute(Webignite.Core.Finance.ChartStyle.Filled);
Save("income-filled");
moneyPage.ShowChartCommand.Execute(Webignite.Core.Finance.ChartStyle.Lines);
Save("income-lines");
moneyPage.UseManualCommand.Execute(null);
foreach (var row in moneyPage.IncomeRows.Take(10))
    row.ManualText = "16000";
Save("income-manual");
moneyPage.UseAutoCommand.Execute(null);
moneyPage.ShowChartCommand.Execute(Webignite.Core.Finance.ChartStyle.Bars);
window.Height = 860;

// A short window scrolled down Invoices: the column titles stay at the top with a shadow under them.
Open(Page.Invoices);
window.MinHeight = 0;
window.Height = 400;
Dispatcher.UIThread.RunJobs();
var pageScroller = window.GetVisualDescendants().OfType<Avalonia.Controls.TransitioningContentControl>()
    .First(c => c.Name == "ScrollingHost").FindAncestorOfType<Avalonia.Controls.ScrollViewer>()!;
pageScroller.Offset = new Vector(0, 260);
Save("invoices-scrolled");
pageScroller.Offset = default;
window.Height = 860;

// Dark mode: each dark look on Overview, Money and Settings, then back to light.
foreach (var look in new[] { DarkPalette.Graphite, DarkPalette.Midnight, DarkPalette.Warm })
{
    services.Settings.Appearance = services.Settings.Appearance with { Theme = Theme.Dark, DarkPalette = look };
    var name = look.ToString().ToLowerInvariant();
    Open(Page.Overview);
    Save($"dark-{name}-overview");
    Open(Page.Money);
    Save($"dark-{name}-money");
    Open(Page.Invoices);
    Save($"dark-{name}-invoices");
}
Open(Page.Settings);
Save("dark-settings");
services.Settings.Appearance = services.Settings.Appearance with { Theme = Theme.Light };
Open(Page.Settings);
settingsPage.Open("look");
Save("settings-looks");
settingsPage.Open("xero");
window.Height = 860;
Open(Page.Reports);
Save("reports");
window.Height = 1300;
Save("reports-tall");
window.Height = 860;

// Mail: the first-time setup, then two mailboxes with a few messages kept on this PC. No network is used.
Open(Page.Mail);
vm.Mail.Setup.Address = "eddie@webignite.co.za";
vm.Mail.Setup.DisplayName = "Eddie, Webignite";
vm.Mail.Setup.ShowServers = true;
Save("mail-setup");
SeedMail();
vm.Mail.Setup.CancelCommand.Execute(null);
vm.Mail.Reload();
vm.Mail.Status = "Up to date at 09:12";
vm.Mail.StatusIsProblem = false;
var opened = vm.Mail.Messages.First(m => m.Summary.Uid == 41);
Wait(vm.Mail.Select(opened), "The mail opened");
Save("mail");
{
    // A long line in the conversation must not widen the page past the window and push the list off screen.
    var mailView = window.GetVisualDescendants().OfType<MailView>().First();
    Check("A conversation's long lines keep the mail page inside the window", vm.Mail.Reader?.HasThread == true && mailView.Bounds.Width <= window.Bounds.Width);
    vm.Mail.Reader!.ToggleThreadCommand.Execute(null);
    Check("The conversation folds up, and stays folded for the next mail", !vm.Mail.Reader.ThreadOpen && services.Settings.MailView.ThreadFolded);
    Save("mail-thread-folded");
    vm.Mail.Reader.ToggleThreadCommand.Execute(null);
    var menu = vm.Mail.MessageMenu().Select(i => i.Header).ToList();
    Check("A mail's right-click menu has the new choices", menu.Contains("Open in a new window") && menu.Contains("Make a to-do of it")
        && menu.Contains("Save as a file…") && menu.Any(h => h.StartsWith("Write to", StringComparison.Ordinal)) && menu.Contains("Select all"));
    vm.Mail.Reader.ToggleAttachmentsCommand.Execute(null);
    Check("The attachments fold up and stay folded", !vm.Mail.Reader.AttachmentsOpen && services.Settings.MailView.AttachmentsFolded);
    vm.Mail.Reader.ToggleRecipientsCommand.Execute(null);
    Save("mail-folded-details");
    vm.Mail.Reader.ToggleAttachmentsCommand.Execute(null);
    vm.Mail.Reader.ToggleRecipientsCommand.Execute(null);
}
// The same mail in a dark look: it stays on white paper so it can be read.
services.Settings.Appearance = services.Settings.Appearance with { Theme = Theme.Dark, DarkPalette = DarkPalette.Graphite };
Save("dark-mail");
services.Settings.Appearance = services.Settings.Appearance with { Theme = Theme.Light };

// The PDF attachment shown in the reading pane. The drawing runs off the screen's thread, so wait for it here.
{
    Wait(vm.Mail.Reader!.Attachments.First(a => a.Name == "booking-report.pdf").PreviewCommand.ExecuteAsync(null), "The PDF preview finished");
    Check("The PDF preview drew its page", vm.Mail.Reader.Preview?.Pages.Count > 0);
    Save("mail-preview-pdf");
    vm.Mail.Reader.Preview?.CloseCommand.Execute(null);
}

// Only one client's mail.
vm.Mail.ClientFilter = vm.Mail.ClientChoices.First(c => c.Label == "Fynbos Physio");
Save("mail-client-filter");
vm.Mail.ClientFilter = vm.Mail.ClientChoices[0];
Wait(vm.Mail.Select(vm.Mail.Messages.First(m => m.Summary.Uid == 41)), "The booking mail opened");

// Snooze until a picked day: the small form above the list.
Wait(vm.Mail.SnoozeMenu().Last().Run!(), "The snooze form opened");
Save("mail-snooze-pick");
vm.Mail.CancelSnoozePickCommand.Execute(null);

// Mail rules: two rules, then the page with the form.
{
    var work = services.Store.ListMailAccounts().First(a => a.Address.StartsWith("eddie", StringComparison.Ordinal));
    services.Settings.MailRules = new Webignite.Core.Mail.MailRuleSettings
    {
        Rules =
        [
            new() { Id = "r1", AccountId = work.Id, Match = Webignite.Core.Mail.RuleMatch.Domain, Text = "karoocoffee.co.za", MoveTo = "INBOX.Clients.Karoo", Category = "clients" },
            new() { Id = "r2", Match = Webignite.Core.Mail.RuleMatch.Subject, Text = "Proof of payment", Category = "accounts", MarkImportant = true },
        ],
    };
    vm.Mail.ManageRulesCommand.Execute(null);
    vm.Mail.RulesPage.Text = "fnb.co.za";
    Save("mail-rules");
    vm.Mail.RulesPage.CloseCommand.Execute(null);
    services.Settings.MailRules = new Webignite.Core.Mail.MailRuleSettings();
}
vm.Mail.Reader!.ReplyCommand.Execute(null);
// The reply's opening comes from a saved reply, with Lerato's name filled in.
services.Settings.SavedReplies =
[
    new Webignite.Core.Mail.SavedReply("sr1", "Looking into it", "Hi {name},\n\nThanks for letting me know. I'm looking at it now and will come back to you before lunch."),
    new Webignite.Core.Mail.SavedReply("sr2", "Payment received", "Hi {name},\n\nThank you, the payment from {client} came through."),
];
{
    Dispatcher.UIThread.RunJobs();
    var editor = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Webignite.Desktop.Controls.RichTextEditor>().First();
    editor.StartTyping();
    editor.InsertDocument(vm.Mail.Composer!.Writing(services.Settings.SavedReplies[0]));
    Dispatcher.UIThread.RunJobs();
    Check("A saved reply goes in above the signature, with the name filled in",
        vm.Mail.Composer.Body.StartsWith("Hi Lerato,", StringComparison.Ordinal) && vm.Mail.Composer.Document.HasSignature);
}
vm.Mail.Composer.ShowQuote = true;
Save("mail-reply");
vm.Mail.Composer.StartSaveReply("Thanks for letting me know.");
vm.Mail.Composer.ReplyName = "Thanks for letting me know";
Save("mail-save-reply");
vm.Mail.Composer.CancelSaveReplyCommand.Execute(null);
// Send: the mail waits ten seconds with Undo. The headless clock doesn't tick, so it waits until Undo here.
vm.Mail.Composer.SendCommand.Execute(null);
Save("mail-undo-send");
vm.Mail.UndoSendCommand.Execute(null);
vm.Mail.Composer.DiscardCommand.Execute(null);
vm.Mail.Open(vm.Mail.Mailboxes[0].Folders[0]);
vm.Mail.Status = "Up to date at 09:12";
Save("mail-all-inboxes");
// Organising: a category across both mailboxes, several messages picked, a quick filter, the editors.
vm.Mail.Open(vm.Mail.Categories.First(c => c.Keyword == "clients"));
Save("mail-category");
vm.Mail.Open(vm.Mail.Mailboxes[0].Folders[0]);
foreach (var i in new[] { 3, 4, 5 })
    vm.Mail.Selection.Select(i);
Save("mail-many-picked");
vm.Mail.Selection.Clear();
vm.Mail.Filter = Webignite.Core.Mail.MailFilter.Unread;
Save("mail-filter-unread");
vm.Mail.Filter = Webignite.Core.Mail.MailFilter.All;
vm.Mail.StartNewCategory([]);
vm.Mail.CategoryEditor!.Name = "Later";
Save("mail-new-category");
vm.Mail.Mailboxes[1].NewFolderCommand.Execute(null);
vm.Mail.FolderEditor!.Name = "Banking";
Save("mail-new-folder");
vm.Mail.FolderEditor.CancelCommand.Execute(null);
vm.Mail.Open(vm.Mail.Mailboxes[0].Folders[0]);
vm.Mail.Panels.Show("folders", false);
vm.Mail.Panels.Resized("messages", 460);
Wait(vm.Mail.Select(vm.Mail.Messages.First(m => m.Summary.Uid == 41)), "The booking mail opened");
Save("mail-panels-no-folders");
vm.Mail.Panels.Move("reader", -1);
Save("mail-panels-reader-left");
vm.Mail.Panels.ResetCommand.Execute(null);
vm.Mail.Open(vm.Mail.Mailboxes[0].Folders[0]);
Open(Page.Clients);
vm.Clients.OpenCommand.Execute(vm.Clients.Rows.First(r => r.Client.Name == "Fynbos Physio"));
Save("client-mail");

// Files: the client folders first, the three views, and each change it makes put back with Undo.
{
    foreach (var folder in new[] { "Bosveld Builders", "Fynbos", "Lerato Designs" })
        Directory.CreateDirectory(Path.Combine(projects, folder));
    var site = Path.Combine(projects, "Bosveld Builders");
    Directory.CreateDirectory(Path.Combine(site, "Photos"));
    Directory.CreateDirectory(Path.Combine(site, "Logo"));
    File.WriteAllBytes(Path.Combine(site, "Bosveld website brief.pdf"), SamplePdf("Bosveld Builders", "Website brief"));
    File.WriteAllBytes(Path.Combine(site, "Quote QU-0042.pdf"), SamplePdf("Quote QU-0042", "R 17 700.00"));
    File.WriteAllText(Path.Combine(site, "Sitemap.docx"), "sitemap");
    File.WriteAllText(Path.Combine(site, "Copy for the home page.txt"), "Building the Bosveld since 1998.");
    var logoPng = Path.Combine(RepoRoot(), "src", "Webignite.Desktop", "Assets", "webignite-logo.png");
    foreach (var picture in new[] { "Site visit 1.png", "Site visit 2.png", "Finished house.png" })
        File.Copy(logoPng, Path.Combine(site, picture));
    File.SetLastWriteTime(Path.Combine(site, "Sitemap.docx"), DateTime.Now.AddDays(-20));

    Open(Page.Files);
    var files = vm.Files;
    Check("Files starts in the client folders", files.Folder == projects);
    Check("Client folders are listed by client in the side list", files.Places[0].Places.Any(p => p.Name == "Bosveld Builders" && p.Path == site));
    files.GoTo(site);
    Check("Folders come first, then files by name", files.Items.OfType<FileRowViewModel>().Select(r => r.Name).Take(3).SequenceEqual(["Logo", "Photos", "Bosveld website brief.pdf"]));
    files.ShowAsCommand.Execute(Webignite.Core.Files.FileView.Details);
    Save("files-details");
    {
        // The right-click menus: on a picture, on a folder, and on empty space.
        var filesView = window.GetVisualDescendants().OfType<FilesView>().First();
        IEnumerable<string> Headers(IEnumerable<Avalonia.Controls.Control> items) =>
            items.OfType<Avalonia.Controls.MenuItem>().Select(i => i.Header as string ?? "");
        files.Selected = [files.Items.OfType<FileRowViewModel>().First(r => r.Name == "Site visit 1.png")];
        var onPicture = Headers(filesView.MenuItems(files)).ToList();
        files.Selected = [files.Items.OfType<FileRowViewModel>().First(r => r.Name == "Logo")];
        var onFolder = Headers(filesView.MenuItems(files)).ToList();
        files.Selected = [];
        var onSpace = Headers(filesView.MenuItems(files)).ToList();
        Check("A picture's right-click has Mark-up, Mail it, Link to a client and Copy as path",
            onPicture.Contains("Draw on it in Mark-up") && onPicture.Contains("Mail it") && onPicture.Contains("Link to a client") && onPicture.Contains("Copy as path"));
        Check("A folder's right-click opens it in a new window", onFolder.Contains("Open in a new window"));
        Check("Right-click on empty space has View, Refresh and Favourites",
            onSpace.Contains("View") && onSpace.Contains("Refresh") && onSpace.Any(h => h.Contains("Favourites", StringComparison.Ordinal)));
    }
    files.GroupByCommand.Execute(Webignite.Core.Files.FileGrouping.Kind);
    Save("files-grouped");
    files.GroupByCommand.Execute(Webignite.Core.Files.FileGrouping.None);
    files.ShowAsCommand.Execute(Webignite.Core.Files.FileView.List);
    Save("files-list");
    files.ShowAsCommand.Execute(Webignite.Core.Files.FileView.Thumbnails);
    var until = DateTime.UtcNow.AddSeconds(5);
    while (files.Items.OfType<FileRowViewModel>().Any(r => r.IsPicture && !r.HasThumbnail) && DateTime.UtcNow < until)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
    Check("Pictures show as thumbnails", files.Items.OfType<FileRowViewModel>().Where(r => r.IsPicture).All(r => r.HasThumbnail));
    Save("files-thumbnails");
    files.ShowAsCommand.Execute(Webignite.Core.Files.FileView.Details);

    FileRowViewModel Row(string name) => files.Items.OfType<FileRowViewModel>().First(r => r.Name == name);
    files.NewFolderCommand.Execute(null);
    var made = files.Items.OfType<FileRowViewModel>().FirstOrDefault(r => r.IsRenaming);
    Check("A new folder is made and ready to name", made?.Name == "New folder" && Directory.Exists(Path.Combine(site, "New folder")));
    made!.RenameText = "Invoices";
    Save("files-renaming");
    Check("Renaming keeps the new name", files.FinishRename(made) && Directory.Exists(Path.Combine(site, "Invoices")));
    made = Row("Invoices");
    made.IsRenaming = true;
    made.RenameText = "Bad:name";
    Check("A name Windows can't take is refused, with why", !files.FinishRename(made) && made.RenameProblem.Length > 0);
    FilesViewModel.CancelRename(made);
    files.UndoCommand.Execute(null);
    Check("Undo puts the old name back", Directory.Exists(Path.Combine(site, "New folder")) && !Directory.Exists(Path.Combine(site, "Invoices")));
    files.UndoCommand.Execute(null);
    Check("Undo takes the new folder away again", !Directory.Exists(Path.Combine(site, "New folder")));

    files.Selected = [Row("Quote QU-0042.pdf")];
    files.CopyCommand.Execute(null);
    files.GoTo(Path.Combine(projects, "Fynbos"));
    files.Paste();
    Check("Copy and paste puts a copy in the other folder", File.Exists(Path.Combine(projects, "Fynbos", "Quote QU-0042.pdf")) && File.Exists(Path.Combine(site, "Quote QU-0042.pdf")));
    files.Paste();
    Check("Pasting again doesn't overwrite: the copy gets (2)", File.Exists(Path.Combine(projects, "Fynbos", "Quote QU-0042 (2).pdf")));
    files.UndoCommand.Execute(null);
    files.UndoCommand.Execute(null);
    Check("Undo takes both copies away", !Directory.EnumerateFiles(Path.Combine(projects, "Fynbos")).Any());

    files.BackCommand.Execute(null);
    Check("Back returns to the folder before", files.Folder == site);
    files.Selected = [Row("Copy for the home page.txt")];
    files.CutCommand.Execute(null);
    Check("Cut files show faded until pasted", Row("Copy for the home page.txt").IsCut);
    files.GoTo(Path.Combine(site, "Photos"));
    files.Paste();
    Check("Cut and paste moves it", File.Exists(Path.Combine(site, "Photos", "Copy for the home page.txt")) && !File.Exists(Path.Combine(site, "Copy for the home page.txt")));
    files.UndoCommand.Execute(null);
    Check("Undo moves it back", File.Exists(Path.Combine(site, "Copy for the home page.txt")));

    files.UpCommand.Execute(null);
    files.Selected = [Row("Sitemap.docx")];
    files.DeleteCommand.Execute(null);
    Check("Delete sends it away", !File.Exists(Path.Combine(site, "Sitemap.docx")));

    files.Open(Row("Quote QU-0042.pdf"));
    Wait(Task.Run(async () =>
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (files.Preview is { IsLoading: true } && DateTime.UtcNow < until)
            await Task.Delay(20);
    }), "The PDF opens in the preview");
    Check("A PDF opens in the preview, page by page", files.Preview is { Pages.Count: > 0 });
    Save("files-preview");
    files.ClosePreviewCommand.Execute(null);

    // Tags: put on from the right-click menu, shown as dots and in the Tags column, kept through a rename and a
    // move, with a spot in the side list that gathers every tagged file from all the client folders.
    files.Selected = [Row("Bosveld website brief.pdf"), Row("Logo")];
    var final = files.AllTags.First(t => t.Id == "final");
    var needsOk = files.AllTags.First(t => t.Id == "needs-ok");
    files.ToggleTagCommand.Execute(final);
    files.Selected = [Row("Quote QU-0042.pdf")];
    files.ToggleTagCommand.Execute(needsOk);
    files.ToggleTagCommand.Execute(final);
    Check("Tagged files show their tags", Row("Bosveld website brief.pdf").TagText == "Final" && Row("Logo").HasTags
        && Row("Quote QU-0042.pdf").TagText == "Final, Needs client OK");
    Save("files-tags");
    files.Selected = [Row("Quote QU-0042.pdf")];
    files.ToggleTagCommand.Execute(final);
    Check("Picking a tag again takes it off", Row("Quote QU-0042.pdf").TagText == "Needs client OK");
    files.ToggleTagCommand.Execute(final);

    var briefRow = Row("Bosveld website brief.pdf");
    files.StartRename(briefRow);
    briefRow.RenameText = "Website brief v2.pdf";
    files.FinishRename(briefRow);
    Check("A tag stays on a renamed file", Row("Website brief v2.pdf").TagText == "Final");
    files.Selected = [Row("Website brief v2.pdf")];
    files.CutCommand.Execute(null);
    files.GoTo(Path.Combine(projects, "Fynbos"));
    files.Paste();
    Check("A tag goes with a file moved to another folder", Row("Website brief v2.pdf").TagText == "Final");
    files.UndoCommand.Execute(null);
    files.GoTo(site);
    Check("And comes back with it on Undo", Row("Website brief v2.pdf").TagText == "Final");

    files.Selected = [Row("Site visit 1.png")];
    files.ToggleTagCommand.Execute(needsOk);
    files.GroupByCommand.Execute(Webignite.Core.Files.FileGrouping.Tag);
    Check("Grouping by tag follows the tag list, untagged last",
        files.Items.OfType<FileHeaderRow>().Select(h => h.Heading).SequenceEqual(["Final", "Needs client OK", Webignite.Core.Files.FileLists.NoTag]));
    Save("files-by-tag");
    files.GroupByCommand.Execute(Webignite.Core.Files.FileGrouping.None);
    files.FilterByCommand.Execute(final);
    Check("The Tags button shows only files with that tag", files.Items.OfType<FileRowViewModel>().Select(r => r.Name).SequenceEqual(["Logo", "Quote QU-0042.pdf", "Website brief v2.pdf"]));
    files.FilterByCommand.Execute(null);

    File.WriteAllText(Path.Combine(projects, "Lerato Designs", "Final logo.svg"), "<svg/>");
    files.GoTo(Path.Combine(projects, "Lerato Designs"));
    files.Selected = [Row("Final logo.svg")];
    files.ToggleTagCommand.Execute(final);
    Check("Each tag has a spot in the side list", files.Places.Any(g => g.Title == "TAGS" && g.Places.Any(p => p.Name == "Final" && p.HasColour)));
    files.Places.First(g => g.Title == "TAGS").Places.First(p => p.Name == "Final").GoCommand.Execute(null);
    Check("A tag's spot gathers its files from every client folder", files.IsTagView
        && files.Items.OfType<FileRowViewModel>().Select(r => r.Name).Order().SequenceEqual(["Final logo.svg", "Logo", "Quote QU-0042.pdf", "Website brief v2.pdf"]));
    Save("files-tag-view");
    files.ShowInItsFolder(Row("Final logo.svg"));
    Check("Show in its folder opens the folder with the file picked", files.Folder == Path.Combine(projects, "Lerato Designs") && files.Selected.Single().Name == "Final logo.svg");
    files.BackCommand.Execute(null);
    Check("Back returns to the tag's files", files.IsTagView);
    files.GoTo(site);

    // Settings: tags renamed and recoloured keep their files; a removed tag comes off them.
    Open(Page.Settings);
    var tagSettings = (SettingsViewModel)vm[Page.Settings].Main.Page;
    tagSettings.Open("files");
    tagSettings.FileTagRows.First(r => r.Tag.Id == "needs-ok").Colours.First(c => c.Colour == "pink").PickCommand.Execute(null);
    tagSettings.NewFileTag = "Logo files";
    tagSettings.AddFileTagCommand.Execute(null);
    Check("A tag can be added and recoloured in Settings", services.FileTags.Tags.Any(t => t.Name == "Logo files")
        && services.FileTags.Tags.First(t => t.Id == "needs-ok").Colour == "pink");
    Save("settings-file-tags");
    Open(Page.Files);
    Check("Files shows a tag's new colour straight away", Row("Quote QU-0042.pdf").Tags.Any(t => t.Colour == "pink"));
    tagSettings.FileTagRows.First(r => r.Tag.Id == "invoice-sent").RemoveCommand.Execute(null);
    Check("A removed tag leaves the side list", !files.Places.First(g => g.Title == "TAGS").Places.Any(p => p.Name == "Invoice sent"));

    // Finder extras: search as you type (and inside files), the details pane, Quick Look, column view,
    // Favourites and Recents, and quick actions on pictures, PDFs and zips.
    using (var docx = new System.IO.Compression.ZipArchive(File.Create(Path.Combine(site, "Logo", "Sitemap.docx")), System.IO.Compression.ZipArchiveMode.Create))
    using (var xml = new StreamWriter(docx.CreateEntry("word/document.xml").Open()))
        xml.Write("<w:document><w:body><w:p><w:r><w:t>Home, About, Projects, and online booking for site visits.</w:t></w:r></w:p></w:body></w:document>");
    files.GoTo(site);
    files.SearchText = "visit";
    files.StartSearch(); // as Enter does; the pause after typing needs a clock the headless screen doesn't run
    Check("Search finds names as you type, in the folders below too",
        Until(() => !files.StatusText.Contains("Searching", StringComparison.Ordinal) && files.Items.OfType<FileRowViewModel>().Count() == 2)
        && files.Items.OfType<FileRowViewModel>().Select(r => r.Name).Order().SequenceEqual(["Site visit 1.png", "Site visit 2.png"]));
    Save("files-search");
    files.SearchText = "online booking";
    files.SearchInside = true;
    files.StartSearch();
    Check("Words inside files finds a Word document by what it says",
        Until(() => files.Items.OfType<FileRowViewModel>().Any(r => r.Name == "Sitemap.docx" && r.Snippet?.Contains("online booking", StringComparison.Ordinal) == true)));
    files.SearchText = "17 700";
    files.StartSearch();
    Check("And a PDF by its words", Until(() => files.Items.OfType<FileRowViewModel>().Any(r => r.Name == "Quote QU-0042.pdf" && r.Snippet is not null)));
    Save("files-search-inside");
    files.ClearSearchCommand.Execute(null);
    files.SearchInside = false;
    Check("Clearing the search shows the folder again", !files.IsSearching && files.Items.OfType<FileRowViewModel>().Any(r => r.Name == "Logo"));

    files.ShowDetails = true;
    files.Selected = [Row("Quote QU-0042.pdf")];
    files.PickChanged();
    Check("The details pane shows a PDF's pages", Until(() => files.Details is { IsLoading: false, Measure: "1 page", HasPictures: true }));
    Save("files-details-pane");
    files.Selected = [Row("Site visit 1.png")];
    files.PickChanged();
    Check("And a picture's size", Until(() => files.Details is { IsLoading: false } d && d.Measure.Contains('×')));

    files.Selected = [Row("Copy for the home page.txt")];
    files.PickChanged();
    files.ToggleQuickLook();
    Check("Quick Look shows a text file's words", Until(() => files.QuickLook is { IsLoading: false, HasText: true }));
    Save("files-quick-look");
    files.Selected = [Row("Quote QU-0042.pdf")];
    files.PickChanged();
    Check("Moving the pick moves Quick Look with it", Until(() => files.QuickLook is { Name: "Quote QU-0042.pdf", IsLoading: false, HasPictures: true }));
    files.ToggleQuickLook();
    Check("Space again closes it", files.QuickLook is null);
    files.ShowDetails = false;

    files.ShowAsCommand.Execute(Webignite.Core.Files.FileView.Columns);
    files.GoTo(Path.Combine(site, "Logo"));
    Check("Column view shows each folder on the way in a column", files.Columns.Select(c => Path.GetFileName(c.Folder)).SequenceEqual(["Projects", "Bosveld Builders"])
        && files.Columns[1].Rows.Single(r => r.IsOnPath).Name == "Logo");
    Save("files-columns");
    files.UpAndPick();
    Check("Left goes back a column with the folder picked", files.Folder == site && files.Selected.Single().Name == "Logo");
    files.ShowAsCommand.Execute(Webignite.Core.Files.FileView.Details);

    files.AddFavourite(Path.Combine(site, "Photos"));
    Check("A folder can be added to Favourites", files.Places.First(g => g.Title == "FAVOURITES").Places.Any(p => p.Name == "Photos" && p.IsFavourite));
    files.Open(Row("Quote QU-0042.pdf"));
    files.ClosePreviewCommand.Execute(null);
    files.Places.First(g => g.Title == "FAVOURITES").Places.First(p => p.Name == "Recent files").GoCommand.Execute(null);
    Check("Recents lists files opened from here", files.ShowingRecents && files.Items.OfType<FileRowViewModel>().First().Name == "Quote QU-0042.pdf");
    Save("files-recents");
    files.RemoveFavourite(Path.Combine(site, "Photos"));
    files.GoTo(site);

    var visit = Path.Combine(site, "Site visit 1.png");
    (int W, int H) Size(string picture)
    {
        using var bitmap = new Bitmap(picture);
        return (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
    }
    var wide = Size(visit);
    files.Selected = [Row("Site visit 1.png")];
    Wait(files.RotateRightCommand.ExecuteAsync(null), "Turning a picture");
    Check("Turn right stands a wide picture on its end", Size(visit) == (wide.H, wide.W));
    files.UndoCommand.Execute(null);
    Check("Undo turns it back", Size(visit) == wide);
    files.Selected = [Row("Quote QU-0042.pdf"), Row("Site visit 2.png")];
    Wait(files.CombineCommand.ExecuteAsync(null), "Putting files into one PDF");
    var combined = Path.Combine(site, "Combined.pdf");
    Check("Put into one PDF makes one PDF with a page for each, ready to name" + (files.MessageIsProblem ? " (" + files.Message + ")" : ""),
        File.Exists(combined) && PdfPageCount(combined) == 2 && files.Items.OfType<FileRowViewModel>().Any(r => r.Name == "Combined.pdf" && r.IsRenaming));
    if (files.Items.OfType<FileRowViewModel>().FirstOrDefault(r => r.Name == "Combined.pdf") is { } combinedRow)
        FilesViewModel.CancelRename(combinedRow);
    files.UndoCommand.Execute(null);
    Check("Undo takes the new PDF away", !File.Exists(combined));
    files.Selected = [Row("Logo"), Row("Quote QU-0042.pdf")];
    Wait(files.ZipCommand.ExecuteAsync(null), "Zipping");
    var zipped = Path.Combine(site, "Bosveld Builders.zip");
    using (var zip = System.IO.Compression.ZipFile.OpenRead(zipped))
        Check("Zip puts folders and files in one zip", zip.Entries.Any(z => z.FullName == "Logo/Sitemap.docx") && zip.Entries.Any(z => z.FullName == "Quote QU-0042.pdf"));
    files.UndoCommand.Execute(null);

    // Power tools: Show by kind and date, Space (folder sizes), renaming many at once, and the copy queue.
    files.GoTo(site);
    File.SetLastWriteTime(Path.Combine(site, "Finished house.png"), DateTime.Now.AddYears(-2));
    files.Refresh();
    files.ShowOnlyCommand.Execute(Webignite.Core.Files.KindFilter.Pictures);
    Check("Show, Pictures keeps only the pictures", files.Items.OfType<FileRowViewModel>().All(r => r.IsPicture) && files.Items.OfType<FileRowViewModel>().Count() == 3);
    files.ShowFromCommand.Execute(Webignite.Core.Files.DateFilter.OlderThanAYear);
    Check("And older than a year keeps the old one", files.Items.OfType<FileRowViewModel>().Select(r => r.Name).SequenceEqual(["Finished house.png"])
        && files.FilterLabel == "Pictures, older than a year");
    Save("files-show-filter");
    files.ShowEverythingCommand.Execute(null);

    File.WriteAllBytes(Path.Combine(site, "Photos", "Drone shot.jpg"), new byte[3_000_000]);
    File.WriteAllBytes(Path.Combine(site, "Logo", "logo-print.pdf"), new byte[400_000]);
    files.ToggleSpaceCommand.Execute(null);
    Check("Space adds up the folders and puts the biggest first",
        Until(() => files.StatusText.Contains("in all", StringComparison.Ordinal))
        && files.Items.OfType<FileRowViewModel>().First().Name == "Photos" && files.Items.OfType<FileRowViewModel>().First().SizeText == "2.9 MB".Replace('.', System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0]));
    Save("files-space");
    files.ToggleSpaceCommand.Execute(null);

    files.Selected = [Row("Site visit 1.png"), Row("Site visit 2.png"), Row("Finished house.png")];
    files.StartBulkRename();
    var bulk = files.BulkRename!;
    bulk.Find = "Site visit 1";
    bulk.Replace = "Site visit 2";
    Check("Bulk rename won't give two files one name", !bulk.CanApply && bulk.Problem.Length > 0);
    bulk.Find = "";
    bulk.Numbered = true;
    bulk.NumberedName = "Bosveld";
    Check("Name and number them shows each new name first", bulk.Changes.Select(c => c.To).SequenceEqual(["Bosveld 01.png", "Bosveld 02.png", "Bosveld 03.png"]) && bulk.CanApply);
    Save("files-bulk-rename");
    bulk.ApplyCommand.Execute(null);
    Check("Renaming many at once renames them all, tags and all", files.BulkRename is null
        && File.Exists(Path.Combine(site, "Bosveld 01.png")) && !File.Exists(Path.Combine(site, "Site visit 1.png")) && Row("Bosveld 01.png").TagText == "Needs client OK");
    files.UndoCommand.Execute(null);
    Check("Undo puts every name back", File.Exists(Path.Combine(site, "Site visit 1.png")) && File.Exists(Path.Combine(site, "Finished house.png")) && Row("Site visit 1.png").HasTags);

    var fynbosFolder = Path.Combine(projects, "Fynbos");
    files.Bring([Path.Combine(site, "Photos")], move: false, into: fynbosFolder);
    Check("A folder goes to the copy queue and arrives", files.CopyJobs.Count > 0 && Until(() => files.CopyJobs.Count == 0)
        && File.Exists(Path.Combine(fynbosFolder, "Photos", "Drone shot.jpg")));
    files.UndoCommand.Execute(null);
    Check("Undo takes the queued copy away again", !Directory.Exists(Path.Combine(fynbosFolder, "Photos")));
    var big = Path.Combine(site, "Walkthrough.mp4");
    File.WriteAllBytes(big, new byte[120_000_000]);
    files.Bring([big], move: false, into: fynbosFolder);
    var job = files.CopyJobs.Last();
    job.PauseCommand.Execute(null);
    Thread.Sleep(300);
    Dispatcher.UIThread.RunJobs();
    Check("A big copy can be paused part way", job.IsPaused && job.BytesDone < job.BytesTotal);
    Save("files-copy-queue");
    job.ResumeCommand.Execute(null);
    Check("And carries on to the end", Until(() => !files.CopyJobs.Contains(job)) && new FileInfo(Path.Combine(fynbosFolder, "Walkthrough.mp4")).Length == 120_000_000);
    File.Delete(Path.Combine(fynbosFolder, "Walkthrough.mp4"));
    files.Bring([big], move: false, into: fynbosFolder);
    job = files.CopyJobs.Last();
    job.PauseCommand.Execute(null);
    job.StopCommand.Execute(null);
    Check("Stop ends it and drops the half-copied file", Until(() => job.State == Webignite.Desktop.Files.CopyState.Stopped) && !File.Exists(Path.Combine(fynbosFolder, "Walkthrough.mp4")));
    job.DismissCommand.Execute(null);
    File.Delete(big);

    // PDF tools: two PDFs together, a page turned and moved, pages taken out, filled in and signed, saved,
    // a smaller copy and the pages as pictures. PDFium does the work, on Linux as on Windows.
    var quotePdf = Path.Combine(site, "Quote QU-0042.pdf");
    var briefPdf = Path.Combine(site, "Website brief v2.pdf");
    services.Go.Pdf([quotePdf, briefPdf]);
    var pdf = vm.Pdf;
    Check("PDF tools opens both files' pages together", Until(() => pdf.Pages.Count == 2 && pdf.Pages.All(p => p.Thumbnail is not null)) && pdf.Changed);
    pdf.Picked = [pdf.Pages[1]];
    pdf.TurnRightCommand.Execute(null);
    pdf.MovePicked(0);
    Check("A page can be turned and moved to the front", pdf.Pages[0].From == "Website brief v2.pdf" && pdf.Pages[0].Angle == 90 && pdf.Pages[0].Number == 1);
    Save("pdf-pages");
    pdf.RangeText = "2";
    Wait(pdf.TakeOutCommand.ExecuteAsync(null), "Taking a page out into a new PDF");
    Check("A page can be taken out into a PDF of its own", File.Exists(Path.Combine(site, "Quote QU-0042 (page 2).pdf")));
    pdf.RangeText = "";

    pdf.Picked = [pdf.Pages[1]];
    Wait(pdf.FillInCommand.ExecuteAsync(null), "Opening a page to fill in");
    var fill = pdf.Filling!;
    var typed = fill.PlaceAt(0.12, 0.3)!;
    typed.Text = "Approved by Pieter van Wyk";
    fill.UseCommand.Execute(Webignite.Desktop.ViewModels.FillTool.Date);
    fill.PlaceAt(0.12, 0.34);
    var pad = new Avalonia.Controls.Canvas { Width = 520, Height = 180 };
    var line = new Avalonia.Controls.Shapes.Polyline { Stroke = Avalonia.Media.Brushes.Navy, StrokeThickness = 2.5 };
    foreach (var (x, y) in new[] { (40, 120), (80, 40), (120, 130), (160, 50), (200, 120), (260, 90), (320, 100) })
        line.Points.Add(new Point(x, y));
    pad.Children.Add(line);
    fill.KeepSignature(PdfView.SignaturePng(pad));
    fill.PlaceAt(0.3, 0.42);
    Check("Text, the date and a signature go on the page", fill.Stamps.Count == 3 && fill.Stamps.Any(s => s.IsSignature) && services.Settings.Signature is { Length: > 0 });
    Save("pdf-fill");
    fill.DoneCommand.Execute(null);
    var signed = Path.Combine(data, "signed.pdf");
    Wait(pdf.SaveAs(signed), "Saving the signed PDF");
    var signedText = PdfReadText(signed);
    Check("The saved PDF has its pages and the words typed on", PdfPageCount(signed) == 2 && signedText.Contains("Approved by Pieter van Wyk", StringComparison.Ordinal)
        && signedText.Contains(DateTime.Today.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
    Wait(pdf.SavePicturesCommand.ExecuteAsync(null), "Saving the pages as pictures");
    Check("The pages can be saved as pictures", Directory.GetFiles(Path.Combine(site, "Quote QU-0042 pages"), "*.png").Length == 2);
    Wait(pdf.MakeSmallerCommand.ExecuteAsync(null), "Making a smaller copy");
    Check("Make smaller says what it did", pdf.Message.Length > 0 && !pdf.MessageIsProblem);
    Open(Page.Files);

    services.Go.Folder(Path.Combine(site, "Logo"), newTab: true);
    Check("A folder opens in a Files tab of its own", vm.Open.Count(h => h.Key == Page.Files) == 2 && vm.Files.Folder == Path.Combine(site, "Logo"));
    vm.Close(vm.Current);

    // The client's page finds their folder by name, and asks about one that only looks like it.
    Open(Page.Clients);
    vm.Clients.OpenCommand.Execute(vm.Clients.Rows.First(r => r.Client.Name == "Bosveld Builders"));
    Check("A client's folder is found by name", vm.Clients.Detail is { FolderPath: var bosveldFolder, FolderIsGuess: false } && bosveldFolder == site);
    vm.Clients.OpenCommand.Execute(vm.Clients.Rows.First(r => r.Client.Name == "Fynbos Physio"));
    Check("A folder that only looks like the client's is asked about", vm.Clients.Detail is { FolderIsGuess: true } fynbos && fynbos.FolderPath.EndsWith("Fynbos", StringComparison.Ordinal));
    Save("client-folder");
    vm.Clients.Detail!.ConfirmFolderCommand.Execute(null);
    Check("Saying yes keeps it on the client", store.ListClients().First(c => c.Name == "Fynbos Physio").Folder?.EndsWith("Fynbos", StringComparison.Ordinal) == true
        && !vm.Clients.Detail.FolderIsGuess);

    // The app's own Open and Save box, in place of Windows' boxes: the explorer, showing only what fits.
    var openPick = new FilePickerViewModel(services, PickKind.Files, "Open PDFs or pictures", ["*.pdf"], many: true, kindName: "PDFs", start: site);
    var pickWindow = new FilePickerWindow { DataContext = openPick };
    pickWindow.Show();
    Dispatcher.UIThread.RunJobs();
    var pickRows = openPick.Files.Items.OfType<FileRowViewModel>().ToList();
    Check("The Open box shows folders and only the files that fit", pickRows.Count > 0 && pickRows.All(r => r.IsFolder || r.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)));
    using (var pickFrame = pickWindow.CaptureRenderedFrame())
    using (var pickFile = File.Create(Path.Combine(output, "picker-open.png")))
        pickFrame?.Save(pickFile, new PngBitmapEncoderOptions());
    openPick.Files.Open(pickRows.First(r => !r.IsFolder));
    Check("Double-clicking a file picks it", openPick.Done.Task.IsCompletedSuccessfully && openPick.Done.Task.Result is [var picked] && picked.EndsWith(".pdf", StringComparison.Ordinal));
    var savePick = new FilePickerViewModel(services, PickKind.Save, "Save the PDF", ["*.pdf"], kindName: "PDF", suggestedName: "Quote QU-0042", start: site);
    savePick.ChooseCommand.Execute(null);
    Check("Saving over a file asks first", !savePick.Done.Task.IsCompleted && savePick.Message.Contains("already", StringComparison.Ordinal));
    savePick.ChooseCommand.Execute(null);
    Check("Saving again replaces it, with the ending added", savePick.Done.Task.Result is [var savedTo] && savedTo == Path.Combine(site, "Quote QU-0042.pdf"));
    savePick.Dispose();
    Check("The next pick starts where the last one was", services.Settings.Files.LastPicked == site);

    // "Show in folder" opens the app's Files, with the file picked.
    services.Go.ShowFile(Path.Combine(site, "Quote QU-0042.pdf"));
    Check("Show in folder opens Files with the file picked", vm.Current.Key == Page.Files && vm.Files.Selected is [{ Name: "Quote QU-0042.pdf" }]);
}

// An invoice email as Approve and email prepares it, from accounts@ with the signature picture.
var logo = File.ReadAllBytes(Path.Combine(RepoRoot(), "src", "Webignite.Desktop", "Assets", "webignite-logo.png"));
var invoiceBox = store.SaveMailAccount(store.ListMailAccounts().Single(a => a.SendsInvoices) with
{
    Signature = "Webignite Accounts\naccounts@webignite.co.za", SignatureImage = logo, SignatureImageType = "image/png",
});
var pdfPath = Path.Combine(data, "Invoice INV-0111.pdf");
File.WriteAllBytes(pdfPath, new byte[48_000]);
var filled = DocumentEmail.Fill(new EmailTemplates().Invoice, new DocumentEmailValues
{
    Kind = DocumentKind.Invoice, ContactName = "Karoo Coffee Roasters", FirstName = "Thandi", Number = "INV-0111", Total = 2725m, AmountDue = 2725m,
    DueOrExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)), OnlineLink = "https://in.xero.com/7Hx2kQ", TradingName = "Webignite",
});
services.Go.ComposeMail(new Webignite.Core.Mail.OutgoingMail
{
    AccountId = invoiceBox.Id,
    To = [new("Thandi Mokoena", "accounts@karoocoffee.co.za")],
    Subject = filled.Subject,
    Body = filled.Body,
    Attachments = [new Webignite.Core.Mail.OutgoingAttachment("Invoice INV-0111.pdf", 48_000, FilePath: pdfPath)],
}, null, null);
Save("invoice-email");
Check("The signature is in the mail once, picture and all",
    vm.Mail.Composer!.Document.Blocks.Count(b => b.IsSignature && b is Webignite.Core.Writing.PictureBlock) == 1
    && vm.Mail.Composer.Body.Split("Webignite Accounts").Length == 2);
// Address suggestions: type in To and check the list fills in.
{
    var toBox = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.TextBox>().First(b => b.Name == "ToBox");
    toBox.Focus();
    Dispatcher.UIThread.RunJobs();
    toBox.Text = "Thandi Mokoena <accounts@karoocoffee.co.za>, pie";
    var until = DateTime.UtcNow.AddSeconds(5);
    while (!vm.Mail.Composer!.HasSuggestions && DateTime.UtcNow < until)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
    Check("Typing in To suggests addresses", vm.Mail.Composer!.HasSuggestions);
    vm.Mail.Composer.AcceptSuggestion();
    Check("Picking a suggestion fills it in", vm.Mail.Composer.To.Contains("pieter", StringComparison.OrdinalIgnoreCase));
}
// Formatting: a mail using the format bar's styles, typed straight onto the paper.
{
    var writing = Webignite.Core.Writing.RichText.Starting(
        "Hi Thandi,\n\nHere's what I still need **before Friday**:\n- your logo\n- three photos of the shop\n- the *new* opening hours\n\nThe draft site is up at [this link](https://karoo.webignite.co.za).",
        Webignite.Core.Writing.RichText.Signature(invoiceBox.Signature, invoiceBox.SignatureImage, invoiceBox.SignatureImageType));
    var hi = (Webignite.Core.Writing.TextBlock)writing.Blocks[0];
    hi.Style = Webignite.Core.Writing.BlockStyle.Heading2;
    var friday = (Webignite.Core.Writing.TextBlock)writing.Blocks[2];
    friday.Format(25, 38, f => f with { Colour = "#c0392b", Highlight = "#fff3a0" });
    var table = Webignite.Core.Writing.TableBlock.Empty(2, 2);
    table.Rows[0][0].Insert(0, "Hosting", default);
    table.Rows[0][1].Insert(0, "R 450 a month", default);
    table.Rows[1][0].Insert(0, "Domain", default);
    table.Rows[1][1].Insert(0, "R 150 a year", new(Bold: true));
    writing.Blocks.Insert(7, table);
    writing.Blocks.Insert(8, new Webignite.Core.Writing.TextBlock("Pricing as agreed on the call.") { Style = Webignite.Core.Writing.BlockStyle.Quote });
    vm.Mail.Composer!.Document = writing;
}
Save("mail-format");
// Typing goes into the editor, at the caret.
{
    Dispatcher.UIThread.RunJobs();
    var editor = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Webignite.Desktop.Controls.RichTextEditor>().First();
    editor.StartTyping();
    Dispatcher.UIThread.RunJobs();
    window.KeyTextInput("Morning! ");
    Dispatcher.UIThread.RunJobs();
    Check("Typing goes into the mail editor", vm.Mail.Composer!.Body.StartsWith("Morning! Hi Thandi", StringComparison.Ordinal) && vm.Mail.Composer.HasChanges);
    window.KeyPress(Avalonia.Input.Key.Z, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.Z, null);
    Dispatcher.UIThread.RunJobs();
    Check("Ctrl+Z takes the typing back", vm.Mail.Composer.Body.StartsWith("Hi Thandi", StringComparison.Ordinal));
}
// Another mailbox swaps the signature where it stands.
{
    var other = vm.Mail.Composer!.Accounts.FirstOrDefault(a => a.Id != invoiceBox.Id);
    if (other is not null)
    {
        vm.Mail.Composer.From = other;
        Dispatcher.UIThread.RunJobs();
        Check("Changing From swaps the signature", !vm.Mail.Composer.Body.Contains("Webignite Accounts", StringComparison.Ordinal)
            && !vm.Mail.Composer.Document.Blocks.Any(b => b is Webignite.Core.Writing.PictureBlock));
        vm.Mail.Composer.From = vm.Mail.Composer.Accounts.First(a => a.Id == invoiceBox.Id);
        Dispatcher.UIThread.RunJobs();
    }
}
vm.Mail.Composer!.DiscardCommand.Execute(null);
vm.Mail.ManageMailboxesCommand.Execute(null);
vm.Mail.Setup.Edit(invoiceBox);
Save("mail-signature-picture");
vm.Mail.Setup.CancelCommand.Execute(null);
vm.Clients.Detail!.BackCommand.Execute(null);
// To approve: a quote Claude prepared, alongside the Xero drafts from earlier.
var assistant = new Webignite.Data.AssistantDocuments(store);
assistant.Draft("quote", "Fynbos Physio", [new("Booking page rebuild", 1, 6500m), new("Mobile testing and fixes (hours)", 4, 650m)],
    DocumentSource.Claude, reference: "Booking page");
assistant.Draft("invoice", "Bosveld Builders", [new("Deposit: new website, 50%", 1, 7250m)], DocumentSource.ChatGpt, reference: "Website deposit");
Open(Page.Approvals);
Save("to-approve");
vm.Approvals.Rows[1].OpenCommand.Execute(null);
Save("to-approve-open");
vm.Approvals.Editor!.BackCommand.Execute(null);

Open(Page.Overview);
Save("overview");

// Tools: each opens in a small window of its own from the sidebar. The calculator, then its answer on an invoice.
void SaveWindow(Avalonia.Controls.Window shown, string name)
{
    for (var i = 0; i < 5; i++)
        Dispatcher.UIThread.RunJobs();
    using var frame = shown.CaptureRenderedFrame();
    using var file = File.Create(Path.Combine(output, name + ".png"));
    frame?.Save(file, new PngBitmapEncoderOptions());
    Console.WriteLine("Saved " + name);
}
Check("Tools are in the sidebar under Computer and Web tools, not on a page of their own",
    vm.Sidebar.Items.OfType<ToolLink>().Select(t => t.Group?.Key).SequenceEqual(["computer", "computer", "computer", "web-tools", "web-tools"])
    && vm.Modules.All(m => m.Title != "Tools") && vm[Page.Files].Group?.Key == "computer" && vm[Page.Notes].Group?.Key == "computer");
vm.SelectedNav = vm.Sidebar.Items.OfType<ToolLink>().First(t => t.Kind == ToolKind.Calculator);
var calculatorWindow = window.ShowTool(ToolKind.Calculator, services);
Check("Clicking a tool opens its window and leaves the page as it was", vm.Selected.Key == Page.Overview && calculatorWindow.IsVisible);
var calc = calculatorWindow.Tools;
calc.Sum = "3.5 x 650";
calc.WorkOutCommand.Execute(null);
calc.Sum = "14 500 + 3 200";
calc.WorkOutCommand.Execute(null);
calc.Sum = "2400 - 10%";
calc.LineDescription = "Hosting, 12 months, returning client";
SaveWindow(calculatorWindow, "tools-calculator");
var qrWindow = window.ShowTool(ToolKind.Qr, services);
qrWindow.Tools.QrInput = "karoocoffee.co.za/menu";
SaveWindow(qrWindow, "tools-qr");
qrWindow.Close();
var passwordWindow = window.ShowTool(ToolKind.Passwords, services);
SaveWindow(passwordWindow, "tools-passwords");
passwordWindow.Close();
var colourWindow = window.ShowTool(ToolKind.Colours, services);
SaveWindow(colourWindow, "tools-colours");
colourWindow.Close();
// Website images: two made-up photos, shrunk for real so the sizes are true.
var photos = Path.Combine(data, "photos");
Directory.CreateDirectory(photos);
foreach (var (name, w, h) in new[] { ("Shop Front (1).jpg", 4032, 3024), ("Team photo.png", 2400, 1600) })
{
    using var bmp = new SkiaSharp.SKBitmap(w, h);
    using (var c = new SkiaSharp.SKCanvas(bmp))
    using (var paint = new SkiaSharp.SKPaint { Shader = SkiaSharp.SKShader.CreateLinearGradient(new(0, 0), new(w, h),
               [new SkiaSharp.SKColor(232, 89, 12), new SkiaSharp.SKColor(28, 29, 33)], SkiaSharp.SKShaderTileMode.Clamp) })
    {
        c.DrawRect(0, 0, w, h, paint);
        var rng = new Random(4);
        for (var i = 0; i < 4000; i++)
            c.DrawCircle(rng.Next(w), rng.Next(h), rng.Next(2, 30), new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)) });
    }
    using var img = SkiaSharp.SKImage.FromBitmap(bmp);
    using var encoded = img.Encode(name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? SkiaSharp.SKEncodedImageFormat.Png : SkiaSharp.SKEncodedImageFormat.Jpeg, 95);
    File.WriteAllBytes(Path.Combine(photos, name), encoded.ToArray());
}
var imagesWindow = window.ShowTool(ToolKind.Images, services);
ShrinkPhotos(photos, imagesWindow.Tools);
SaveWindow(imagesWindow, "tools-images");
imagesWindow.Close();
calc.AddToInvoiceCommand.Execute(null);
calculatorWindow.Close();
Save("tools-calculator-invoice");
vm.Invoices.Editor!.Lines[0].Price = "3 x 650";
Save("invoice-sum");

// Site check, with made-up results (no network here)
Open(Page.SiteCheck);
services.Settings.SpeedHistory = new Dictionary<string, IReadOnlyList<Webignite.Core.Web.SpeedRun>>
{
    ["karoocoffee.co.za"] = [new(DateOnly.FromDateTime(DateTime.Today.AddDays(-29)), 38, "7.9 s")],
};
vm.SiteCheck.Show("karoocoffee.co.za", Webignite.Core.Web.DomainReport.Build(new Webignite.Core.Web.DomainLookup
{
    Domain = "karoocoffee.co.za",
    Today = DateOnly.FromDateTime(DateTime.Today),
    Addresses = ["41.203.18.10"],
    NameServers = ["ns1.host-h.net", "ns2.dns-h.com"],
    MailServers = ["10 karoocoffee-co-za.mail.host-h.net"],
    Txt = ["v=spf1 include:spf.host-h.net ~all"],
    CertificateExpires = DateTimeOffset.Now.AddDays(9),
    CertificateIssuer = "Let's Encrypt",
    Dkim = new Dictionary<string, IReadOnlyList<string>> { ["default"] = ["v=DKIM1; k=rsa; p=MIIBIjANBg"], ["google"] = [] },
    RdapJson = $$"""{"events":[{"eventAction":"expiration","eventDate":"{{DateTime.Today.AddDays(200):yyyy-MM-dd}}T00:00:00Z"}],"entities":[{"roles":["registrar"],"vcardArray":["vcard",[["fn",{},"text","xneelo (Pty) Ltd"]]]}]}""",
}), "https://karoocoffee.co.za/", Webignite.Core.Web.SpeedReport.Parse("""
    {"lighthouseResult":{"categories":{"performance":{"score":0.47}},"audits":{
      "largest-contentful-paint":{"displayValue":"5.8 s","numericValue":5800},
      "first-contentful-paint":{"displayValue":"1.6 s","numericValue":1600},
      "total-blocking-time":{"displayValue":"280 ms","numericValue":280},
      "cumulative-layout-shift":{"displayValue":"0.04","numericValue":0.04},
      "speed-index":{"displayValue":"3.9 s","numericValue":3900},
      "modern-image-formats":{"title":"Serve images in modern formats","details":{"type":"opportunity","overallSavingsMs":2400}},
      "offscreen-images":{"title":"Defer offscreen images","details":{"type":"opportunity","overallSavingsMs":1100}},
      "render-blocking-resources":{"title":"Eliminate render-blocking resources","details":{"type":"opportunity","overallSavingsMs":620}}}}}
    """));
Save("site-check");
// Watched sites, with made-up visits.
{
    var now = DateTimeOffset.Now;
    var day = DateOnly.FromDateTime(DateTime.Today);
    var watch = services.Settings.Uptime with { Extra = ["webignite.co.za", "fynbosphysio.co.za", "karoocoffee.co.za"] };
    var sites = new Dictionary<string, Webignite.Core.Web.WatchedSite>();
    void Visit(string domain, params Webignite.Core.Web.UptimePing[] pings)
    {
        Webignite.Core.Web.WatchedSite? site = new() { Domain = domain, Days = [new(day.AddDays(-3), 96, 96), new(day.AddDays(-2), 96, 95), new(day.AddDays(-1), 96, 96)] };
        foreach (var ping in pings)
            site = Webignite.Core.Web.Uptime.Record(site, domain, ping, day).Site;
        sites[domain] = site!;
    }
    Visit("bosveldbuilders.co.za", new Webignite.Core.Web.UptimePing(now, true, 200, 380, now.AddDays(71)));
    Visit("webignite.co.za", new Webignite.Core.Web.UptimePing(now, true, 200, 290, now.AddDays(44)));
    Visit("fynbosphysio.co.za", new Webignite.Core.Web.UptimePing(now.AddMinutes(-30), false, 503, 900, now.AddDays(40), "The site answered with a server error (503)."),
        new(now.AddMinutes(-15), false, null, 20000, null, "The site took longer than 20 seconds to answer."));
    Visit("karoocoffee.co.za", new Webignite.Core.Web.UptimePing(now, true, 200, 6100, now.AddDays(9)));
    services.Settings.Uptime = watch with { Sites = sites };
    vm.SiteCheck.RefreshWatched();
    window.Height = 1500;
    Save("site-watch");
    window.Height = 860;
}

// Mark-up: a made-up client home page with notes on it
{
    const int w = 1440, h = 900;
    using var site = new SkiaSharp.SKBitmap(w, h);
    using (var c = new SkiaSharp.SKCanvas(site))
    {
        c.Clear(new SkiaSharp.SKColor(250, 247, 242));
        using var dark = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(60, 40, 30) };
        using var orange = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(200, 110, 50) };
        using var grey = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(205, 198, 190) };
        c.DrawRect(0, 0, w, 90, dark);
        using var font = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, 30);
        using var big = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, 64);
        using var white = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White };
        c.DrawText("Karoo Coffee Roasters", 40, 56, font, white);
        foreach (var (label, x) in new[] { ("Menu", 900), ("Shop", 1020), ("Visit", 1140), ("Contact", 1260) })
            c.DrawText(label, x, 56, font, white);
        c.DrawText("Small-batch coffee", 80, 260, big, dark);
        c.DrawText("from the Karoo", 80, 340, big, dark);
        c.DrawRoundRect(80, 400, 260, 70, 12, 12, orange);
        c.DrawText("Shop now", 140, 446, font, white);
        c.DrawRect(760, 160, 600, 420, grey);
        for (var i = 0; i < 3; i++)
            c.DrawRect(80 + i * 440, 640, 400, 220, grey);
    }
    using var encoded = SkiaSharp.SKImage.FromBitmap(site).Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
    using var siteStream = new MemoryStream(encoded.ToArray());
    Open(Page.Markup);
    vm.Markup.Show(new Avalonia.Media.Imaging.Bitmap(siteStream));
    var R = Webignite.Core.Tools.MarkColour.Red;
    vm.Markup.Add(new Webignite.Core.Tools.Mark(Webignite.Core.Tools.MarkKind.Box, R, [new(60, 220), new(720, 360)]));
    vm.Markup.Add(new Webignite.Core.Tools.Mark(Webignite.Core.Tools.MarkKind.Pin, R, [new(730, 215)]));
    vm.Markup.Add(new Webignite.Core.Tools.Mark(Webignite.Core.Tools.MarkKind.Arrow, R, [new(560, 520), new(360, 445)]));
    vm.Markup.Add(new Webignite.Core.Tools.Mark(Webignite.Core.Tools.MarkKind.Pin, R, [new(580, 530)]));
    vm.Markup.Add(new Webignite.Core.Tools.Mark(Webignite.Core.Tools.MarkKind.Highlight, Webignite.Core.Tools.MarkColour.Yellow, [new(880, 20), new(1400, 75)]));
    vm.Markup.Add(new Webignite.Core.Tools.Mark(Webignite.Core.Tools.MarkKind.Pin, Webignite.Core.Tools.MarkColour.Blue, [new(860, 50)]));
    vm.Markup.Pins[0].Comment = "Make the heading one line on phones";
    vm.Markup.Pins[1].Comment = "Button should go to the shop, not the menu";
    vm.Markup.Pins[2].Comment = "Add WhatsApp to the top menu";
    vm.Markup.LoadClients();
    vm.Markup.Client = vm.Markup.Clients.FirstOrDefault();
    vm.Markup.Page = "the home page";
    Save("markup");
    File.WriteAllBytes(Path.Combine(output, "markup-export.png"), vm.Markup.Png()!);
    Console.WriteLine(Webignite.Core.Tools.Markup.Mail("Karoo Coffee Roasters", "Thandi", "the home page", vm.Markup.Marks, "Webignite").Body);

    // Screenshots: the screen frozen under a picker, an area dragged or a window clicked, then text and blur in Mark-up.
    var frozen = window.CaptureRenderedFrame()!;
    var screenArea = new PixelRect(0, 0, frozen.PixelSize.Width, frozen.PixelSize.Height);
    Webignite.Core.Tools.ScreenGrab.Area[] openWindows = [new(232, 0, 600, 400)];
    var picker = new ScreenGrabWindow(frozen, screenArea, 1, openWindows);
    picker.Show();
    picker.Canvas.Press(new Point(300, 150));
    picker.Canvas.Hover(new Point(900, 600));
    Dispatcher.UIThread.RunJobs();
    using (var pickFrame = picker.CaptureRenderedFrame())
    using (var pickFile = File.Create(Path.Combine(output, "screenshot-picking.png")))
        pickFrame?.Save(pickFile, new PngBitmapEncoderOptions());
    picker.Canvas.Release(new Point(900, 600));
    Check("Dragging over the frozen screen picks that area", picker.Picked.IsCompletedSuccessfully && picker.Picked.Result == new Webignite.Core.Tools.ScreenGrab.Area(300, 150, 600, 450));
    var clicker = new ScreenGrabWindow(frozen, screenArea, 1, openWindows);
    clicker.Show();
    clicker.Canvas.Press(new Point(400, 200));
    clicker.Canvas.Release(new Point(401, 201));
    Check("A click takes the whole window under it", clicker.Picked.IsCompletedSuccessfully && clicker.Picked.Result == openWindows[0]);
    var stopper = new ScreenGrabWindow(frozen, screenArea, 1, openWindows);
    stopper.Show();
    stopper.Canvas.Stop();
    Check("Esc stops without a screenshot", stopper.Picked.IsCompletedSuccessfully && stopper.Picked.Result is null);

    var grabbed = ScreenGrabWindow.Crop(frozen, picker.Picked.Result!.Value);
    frozen.Dispose();
    vm.Markup.Show(grabbed);
    Check("The picked area opens in Mark-up at its own size", vm.Markup.ImageSize == "600 × 450");
    vm.Markup.Add(new Webignite.Core.Tools.Mark(Webignite.Core.Tools.MarkKind.Blur, R, [new(20, 20), new(200, 120)]));
    vm.Markup.AddText(new Webignite.Core.Tools.MarkPoint(240, 40), "Password hidden");
    vm.Markup.Add(new Webignite.Core.Tools.Mark(Webignite.Core.Tools.MarkKind.Arrow, R, [new(380, 80), new(210, 70)]));
    Check("Text and blur are kept as marks", vm.Markup.Marks.Select(m => m.Kind).SequenceEqual([Webignite.Core.Tools.MarkKind.Blur, Webignite.Core.Tools.MarkKind.Text, Webignite.Core.Tools.MarkKind.Arrow]));
    Save("markup-screenshot");
    using (var saved = new Bitmap(new MemoryStream(vm.Markup.Png()!)))
    {
        var pixels = new byte[saved.PixelSize.Width * saved.PixelSize.Height * 4];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            saved.CopyPixels(new PixelRect(saved.PixelSize), pin.AddrOfPinnedObject(), pixels.Length, saved.PixelSize.Width * 4);
        }
        finally
        {
            pin.Free();
        }
        int At(int x, int y) => BitConverter.ToInt32(pixels, (y * saved.PixelSize.Width + x) * 4);
        Check("A blur turns what's under it into squares in the saved picture", At(21, 21) == At(27, 27) && At(21, 21) == At(21, 27));
    }
    vm.Markup.Client = vm.Markup.Clients.First(c => c.Name == "Bosveld Builders");
    vm.Markup.SaveToFolderCommand.Execute(null);
    Check("Save puts it in the client's folder", vm.Markup.SavedPath is { } shotPath && Path.GetDirectoryName(shotPath) == Path.Combine(projects, "Bosveld Builders") && File.Exists(shotPath));
    vm.Markup.AddToTodoCommand.Execute(null);
    Check("Add to my to-do list makes a to-do that says where it is",
        store.ListTasks().Any(t => t.Customer == "Bosveld Builders" && t.Notes?.Contains(vm.Markup.SavedPath!, StringComparison.Ordinal) == true));

    var shortcutSettings = (SettingsViewModel)vm[Page.Settings].Main.Page;
    shortcutSettings.SetShortcut("Shift+S");
    Check("A shortcut that would fire while typing is refused", shortcutSettings.ShortcutProblem && services.Settings.Screenshots.Shortcut == "Ctrl+Alt+S");
    shortcutSettings.SetShortcut("Ctrl+Alt+P");
    Check("Another shortcut is kept", services.Settings.Screenshots.Shortcut == "Ctrl+Alt+P" && !shortcutSettings.ShortcutProblem);
    shortcutSettings.Open("screenshots");
    Open(Page.Settings);
    Save("settings-screenshots");
    shortcutSettings.ShortcutBackCommand.Execute(null);
    shortcutSettings.SetRecordShortcut("Ctrl+Alt+S");
    Check("The recording shortcut can't be the screenshot one", shortcutSettings.ShortcutProblem && services.Settings.Screenshots.RecordShortcut == "Ctrl+Alt+R");
    Open(Page.Markup);

    // Screen recording: the bar while recording, then the recording in Mark-up to trim and save. The video
    // itself is Windows' Media Foundation (tested on the Windows build); here a stand-in gives frames.
    var bar = new RecordingBar(() => TimeSpan.FromSeconds(73), () => { }, sound: true);
    bar.Show();
    Dispatcher.UIThread.RunJobs();
    Check("The bar shows how long it has been recording", Until(() => bar.TimeText == "1:13"));
    using (var barFrame = bar.CaptureRenderedFrame())
    using (var barFile = File.Create(Path.Combine(output, "recording-bar.png")))
        barFrame?.Save(barFile, new PngBitmapEncoderOptions());
    bar.Close();

    var video = Path.Combine(data, "recording.mp4");
    File.WriteAllBytes(video, new byte[2_400_000]);
    var standIn = new StandInVideo(site);
    Check("The client picked in Mark-up stays picked after going to another page", vm.Markup.Client?.Name == "Bosveld Builders");
    vm.Markup.ShowRecording(video, TimeSpan.FromSeconds(42), standIn, null);
    var take = vm.Markup.Recording!;
    take.Start = 3.2;
    take.End = 30;
    Check("Moving a handle shows the frame there", Until(() => take.Frame is not null && take.FrameText == "0:30"));
    Check("The part kept is spelled out", take.KeptText == "Keeping 0:03 to 0:30, 0:26 of 0:42");
    Save("markup-recording");
    Wait(take.SaveCommand.ExecuteAsync(null), "Saving the recording");
    Check("Saving cuts it and puts it in the client's folder",
        take.SavedPath is { } savedVideo && Path.GetDirectoryName(savedVideo) == Path.Combine(projects, "Bosveld Builders")
        && standIn.Trimmed == (TimeSpan.FromSeconds(3.2), TimeSpan.FromSeconds(30)));
    take.CloseCommand.Execute(null);
    Check("Done throws the temporary recording away", vm.Markup.Recording is null && !File.Exists(video));
}

// Notes: notebooks and folders, a note with headings, a checklist, a table and a link, search, the client's
// page, the quick note, and nothing typed lost on closing.
{
    var notes = services.Notes;
    var mine = notes.Notebooks()[0];
    notes.AddFolder(mine.Id, "Meetings");
    notes.AddFolder(mine.Id, "Ideas");
    var personal = notes.AddNotebook();
    notes.RenameNotebook(personal.Id, "Personal");
    Webignite.Core.Writing.RichDocument Writing(string html) => Webignite.Core.Writing.RichHtml.Read(html);
    var kickoff = notes.New(mine.Id, "Meetings", clientId: "c1");
    notes.Save(kickoff.Id, "Bosveld Builders website kick-off", Writing(
        "<h2>What Pieter wants</h2><ul><li>A gallery of finished houses, by area</li><li>A quote request form that asks for the plot size</li>"
        + "<li>Their xneelo hosting kept as it is</li></ul><h2>Next steps</h2>"
        + "<ul data-check><li data-checked>Send the sitemap</li><li data-checked>Ask for the logo files</li><li>Get photos of finished projects</li><li>Quote for phase two</li></ul>"
        + "<h3>Pages</h3><table><tr><th>Page</th><th>Who writes it</th></tr><tr><td>Home</td><td>Webignite</td></tr><tr><td>Gallery</td><td>Pieter sends photos</td></tr></table>"
        + "<p>Their current site: <a href=\"https://bosveldbuilders.co.za\">bosveldbuilders.co.za</a></p>"));
    var plan = notes.New(mine.Id);
    notes.Save(plan.Id, "This week", Writing("<ul data-check><li data-checked>Invoices out</li><li>Fynbos booking page on phones</li><li>Back up the laptop</li></ul>"));
    notes.Change(plan.Id, n => n with { Pinned = true });
    var portfolio = notes.New(mine.Id, "Ideas");
    notes.Save(portfolio.Id, "Portfolio page ideas", Writing("<p>Show before and after for each site, with the speed score next to it. Invoice clients for the photos?</p>"));
    var braai = notes.New(personal.Id);
    notes.Save(braai.Id, "Braai on Saturday", Writing("<ul data-check><li>Boerewors</li><li>Charcoal</li><li>Ice</li></ul>"));

    Open(Page.Notes);
    var page = vm.Notes;
    Check("Notes opens on the first notebook with its notes, pinned first", page.Place?.Title == "My notes" && page.Rows.Count == 3 && page.Rows[0].Name == "This week");
    page.OpenNote(kickoff.Id);
    Check("A note opens with its title, checklist and table", page.Title == "Bosveld Builders website kick-off"
        && page.Document!.Blocks.OfType<Webignite.Core.Writing.TextBlock>().Count(b => b.List == Webignite.Core.Writing.ListKind.Check) == 4
        && page.Document.Blocks.OfType<Webignite.Core.Writing.TableBlock>().Any());
    Check("The list says how much of a checklist is done", page.Rows.Any(r => r.Note.Id == kickoff.Id && r.Detail.Contains("2 of 4 done", StringComparison.Ordinal)));
    Check("A note about a client says who", page.OpenClient?.Name == "Bosveld Builders");
    Save("notes");

    page.Query = "invoice";
    Check("Search finds words in titles and in the writing", page.Rows.Select(r => r.Note.Id).ToHashSet().SetEquals([plan.Id, portfolio.Id]));
    Save("notes-search");
    page.Query = "";

    // Typing is saved: on switching notes, and when the app closes.
    page.OpenNote(portfolio.Id);
    page.Title = "Portfolio page";
    page.Document!.Blocks.Add(new Webignite.Core.Writing.TextBlock("Ask Lerato for a quote we can show."));
    page.Revision++;
    Check("Something typed holds the page open", page.HoldsWork);
    page.OpenNote(plan.Id);
    Check("Opening another note saves the one before", notes.Get(portfolio.Id)!.Title == "Portfolio page" && notes.Get(portfolio.Id)!.Text.Contains("Lerato", StringComparison.Ordinal));
    page.Title = "This week, typed before closing";
    Check("Closing the app saves the open note", vm.PrepareToCloseAsync().GetAwaiter().GetResult() is null && notes.Get(plan.Id)!.Title == "This week, typed before closing");

    // The undo note would stay in every picture after this one, as the snapshot clock stands still.
    var showNote = services.ShowNote;
    services.ShowNote = null;
    page.DeleteOpenCommand.Execute(null);
    services.ShowNote = showNote;
    Check("A deleted note waits in Deleted", notes.Get(plan.Id)!.Deleted is not null && page.Rows.All(r => r.Note.Id != plan.Id));
    page.PickPlace(page.Places.First(p => p.Kind == Webignite.Core.Notes.NotePlace.Deleted));
    page.OpenNote(plan.Id);
    Check("It can be read there but not changed", page.OpenIsDeleted && !page.CanEdit);
    page.RestoreOpenCommand.Execute(null);
    Check("Put it back and it's in its notebook again", notes.Get(plan.Id)!.Deleted is null && page.Place?.Kind == Webignite.Core.Notes.NotePlace.Notebook);

    // On the client's page.
    Open(Page.Clients);
    vm.Clients.OpenCommand.Execute(vm.Clients.Rows.First(r => r.Client.Name == "Bosveld Builders"));
    Check("Notes about a client show on their page", vm.Clients.Detail!.NoteLinks.Any(l => l.Name == "Bosveld Builders website kick-off"));
    Dispatcher.UIThread.RunJobs();
    if (window.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().FirstOrDefault(t => t.Text == "Notebook notes") is { } notesCard)
        Avalonia.Controls.ControlExtensions.BringIntoView(notesCard.GetVisualAncestors().OfType<Avalonia.Controls.Border>().First(b => b.Classes.Contains("card")));
    Save("client-notes");
    vm.Clients.Detail.NoteLinks.First().OpenCommand.Execute(null);
    Check("Clicking one opens it in Notes", vm.Current.Key == Page.Notes && vm.Notes.Open?.Id == kickoff.Id);

    // The quick note from any program: what is typed is kept however the box closes.
    var quick = new QuickNoteWindow(services) { Typed = "Call the bank about the card\nThey said to phone after 9\n[ ] Card number ready" };
    quick.Show();
    Dispatcher.UIThread.RunJobs();
    using (var quickFrame = quick.CaptureRenderedFrame())
    using (var quickFile = File.Create(Path.Combine(output, "quick-note.png")))
        quickFrame?.Save(quickFile, new PngBitmapEncoderOptions());
    quick.Close();
    Check("A quick note goes into Notes with its title and checklist", quick.Saved is { Title: "Call the bank about the card", Checks: 1 }
        && Until(() => vm.Notes.Rows.Any(r => r.Name == "Call the bank about the card")));

    var notesSettings = (SettingsViewModel)vm[Page.Settings].Main.Page;
    notesSettings.Open("notes");
    Open(Page.Settings);
    Save("settings-notes");
    notesSettings.SetQuickNoteShortcut("Ctrl+Alt+S");
    Check("The quick-note shortcut can't be the screenshot one", notesSettings.QuickNoteProblem && services.Settings.Notes.QuickShortcut == "Ctrl+Alt+N");
    Open(Page.Notes);
}

// Right-click on rows all round the app: clients, invoices, to-dos.
{
    int MenuSize<T>(Page page) where T : Avalonia.Controls.Control
    {
        Open(page);
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<T>().Where(c => c.Classes.Contains("row") || c.Classes.Contains("taskrow"))
            .Select(c => c.ContextMenu?.Items.Count ?? 0).DefaultIfEmpty(0).Max();
    }
    Check("Clients have a right-click menu", MenuSize<Avalonia.Controls.Button>(Page.Clients) >= 8);
    vm.Invoices.Editor = null;
    Check("Invoices have a right-click menu", MenuSize<Avalonia.Controls.Button>(Page.Invoices) >= 4);
    Check("To-dos have a right-click menu", MenuSize<Avalonia.Controls.Border>(Page.Todo) >= 4);
    var firstTask = vm.Todo.Groups.SelectMany(g => g.Items).First(t => !t.IsDone);
    firstTask.DueTomorrowCommand.Execute(null);
    Check("A to-do's right-click moves its due day", store.ListTasks().First(t => t.Id == firstTask.Task.Id).Due == DateOnly.FromDateTime(DateTime.Today).AddDays(1));
    Open(Page.Clients);
    vm.Clients.NewQuoteForCommand.Execute(vm.Clients.Rows.First(r => r.Client.Name == "Fynbos Physio"));
    Check("New quote for them opens a quote for that client", vm.Clients.Detail?.Editor is { CustomerName: "Fynbos Physio" });
    vm.Clients.Detail!.Editor!.BackCommand.Execute(null);
    vm.Clients.Detail.BackCommand.Execute(null);
}

// Ctrl+K search
vm.Search.Show();
Wait(vm.Search.MailLoaded, "Search read the mail");
vm.Search.Query = "karoo";
Save("search");
vm.Search.Hide();

// New mail while another page is open: a count beside Mail and a note in the corner.
var mailIds = store.ListMailAccounts().Select(a => a.Id).ToList();
services.Settings.MailCheck = new Webignite.Core.Mail.MailCheckSettings { Announced = mailIds.ToDictionary(id => id, _ => 0u) };
var mailCheck = vm.CheckMailAsync();
// Let the note's fade-in finish before the picture.
for (var tick = 0; tick < 40; tick++)
{
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Thread.Sleep(15);
}
Save("mail-new-note");

void SeedMail()
{
    var now = DateTimeOffset.Now;
    var work = store.SaveMailAccount(new Webignite.Core.Mail.MailAccount
    {
        Id = "m1", DisplayName = "Eddie, Webignite", Address = "eddie@webignite.co.za",
        ImapHost = "mail.webignite.co.za", SmtpHost = "smtp.webignite.co.za", Created = now,
        Signature = "Eddie Teichert\nWebignite  ·  webignite.co.za",
    });
    var accounts = store.SaveMailAccount(new Webignite.Core.Mail.MailAccount
    {
        Id = "m2", DisplayName = "Webignite Accounts", Address = "accounts@webignite.co.za",
        ImapHost = "mail.webignite.co.za", SmtpHost = "smtp.webignite.co.za", SendsInvoices = true, Created = now,
    });
    void Folder(string account, string path, string name, Webignite.Core.Mail.FolderRole role, int unread = 0) =>
        store.SaveMailFolder(new Webignite.Core.Mail.MailFolder { AccountId = account, Path = path, Name = name, Role = role, Unread = unread });
    foreach (var id in new[] { work.Id, accounts.Id })
    {
        Folder(id, "INBOX", "Inbox", Webignite.Core.Mail.FolderRole.Inbox, id == work.Id ? 2 : 1);
        Folder(id, "INBOX.Drafts", "Drafts", Webignite.Core.Mail.FolderRole.Drafts);
        Folder(id, "INBOX.Sent", "Sent", Webignite.Core.Mail.FolderRole.Sent);
        Folder(id, "INBOX.Archive", "Archive", Webignite.Core.Mail.FolderRole.Archive);
        Folder(id, "INBOX.Junk", "Junk", Webignite.Core.Mail.FolderRole.Junk);
        Folder(id, "INBOX.Trash", "Trash", Webignite.Core.Mail.FolderRole.Trash);
    }
    Folder(work.Id, "INBOX.Clients", "Clients", Webignite.Core.Mail.FolderRole.Other);
    Folder(work.Id, "INBOX.Clients.Karoo", "Karoo", Webignite.Core.Mail.FolderRole.Other);

    services.Settings.MailCategories =
    [
        new Webignite.Core.Mail.MailCategory("clients", "Clients", "blue"),
        new Webignite.Core.Mail.MailCategory("waiting", "Waiting on someone", "purple"),
        new Webignite.Core.Mail.MailCategory("accounts", "Accounts", "orange"),
    ];
    Webignite.Core.Mail.MailSummary Mail(string account, uint uid, string fromName, string from, string subject, string preview, TimeSpan ago, bool seen, bool attachments = false, bool flagged = false, string[]? categories = null) => new()
    {
        AccountId = account, Folder = "INBOX", Uid = uid, Date = now - ago,
        From = new(fromName, from), To = [new(null, account == work.Id ? work.Address : accounts.Address)],
        Subject = subject, Preview = preview, Seen = seen, HasAttachments = attachments, Flagged = flagged, Keywords = categories ?? [],
    };
    store.SaveMailSummaries(
    [
        Mail(work.Id, 44, "Thandi Mokoena", "thandi@karoocoffee.co.za", "New product photos for the shop", "Hi Eddie, here are the new photos for the online shop. Could you swap them in before Friday?", TimeSpan.FromMinutes(25), false, attachments: true, categories: ["clients"]),
        Mail(work.Id, 43, "Pieter van Wyk", "pieter@bosveldbuilders.co.za", "Re: Website quote", "Thanks, the quote looks good. Can we start in the first week of December?", TimeSpan.FromHours(2), false, categories: ["clients", "waiting"]),
        Mail(work.Id, 41, "Lerato Dlamini", "lerato@fynbosphysio.co.za", "Booking page not loading on phones", "Morning Eddie, a few patients say the booking page just spins on their phones.", TimeSpan.FromHours(5), true, attachments: true, flagged: true, categories: ["clients"])
            with { MessageId = "<b2@fynbosphysio.co.za>", InReplyTo = "<b1@fynbosphysio.co.za>" },
        // The first mail in Lerato's conversation, so the reader shows the stack.
        Mail(work.Id, 38, "Lerato Dlamini", "lerato@fynbosphysio.co.za", "Booking page slow",
            "Hi Eddie, is the booking page slow for you too? It took ages this morning, and two patients phoned to say they gave up halfway through choosing a time, which has never happened before. From: Eddie <eddie@webignite.co.za> Sent: Monday", TimeSpan.FromDays(1), true)
            with { MessageId = "<b1@fynbosphysio.co.za>" },
        Mail(work.Id, 40, "xneelo", "noreply@xneelo.co.za", "Your hosting renewal for webignite.co.za", "Your hosting package renews on 15 December 2026.", TimeSpan.FromDays(2), true),
        Mail(work.Id, 39, "Google Search Console", "sc-noreply@google.com", "New coverage issue for fynbosphysio.co.za", "Search Console has found a new issue on your site.", TimeSpan.FromDays(4), true),
        Mail(accounts.Id, 12, "Karoo Coffee Roasters", "accounts@karoocoffee.co.za", "Proof of payment INV-0107", "Good day, please find attached proof of payment for INV-0107.", TimeSpan.FromHours(1), false, attachments: true, categories: ["accounts"]),
        Mail(accounts.Id, 11, "FNB", "inContact@fnb.co.za", "Your statement is ready", "Your monthly statement for account ending 4410 is ready.", TimeSpan.FromDays(3), true),
    ]);

    var message = new MimeKit.MimeMessage();
    message.From.Add(new MimeKit.MailboxAddress("Lerato Dlamini", "lerato@fynbosphysio.co.za"));
    message.To.Add(new MimeKit.MailboxAddress(null, work.Address));
    message.Subject = "Booking page not loading on phones";
    var body = new MimeKit.BodyBuilder
    {
        HtmlBody = "<p>Morning Eddie,</p><p>A few patients say the booking page just spins on their phones. It seems fine on my laptop. "
            + "I've attached two screenshots one of them sent me.</p><p>Could you have a look today? We have a lot of bookings coming in for December.</p>"
            + "<p>Thanks so much,<br/>Lerato<br/><span style=\"color:#666\">Fynbos Physio · <a href=\"https://fynbosphysio.co.za\">fynbosphysio.co.za</a></span></p>",
    };
    body.Attachments.Add("booking-phone-1.png", new byte[48_211], new MimeKit.ContentType("image", "png"));
    body.Attachments.Add("booking-phone-2.png", new byte[51_877], new MimeKit.ContentType("image", "png"));
    body.Attachments.Add("booking-report.pdf", SamplePdf("Booking page report", "Fynbos Physio, 3 November 2026", "The booking page spins on phones after the date is picked."),
        new MimeKit.ContentType("application", "pdf"));
    message.Body = body.ToMessageBody();
    using var mime = new MemoryStream();
    message.WriteTo(mime);
    store.SaveMailBody(work.Id, "INBOX", 41, mime.ToArray());
}

void Sample(string customer, DocumentKind kind, params (string Description, decimal Quantity, decimal Price)[] lines) =>
    store.Save(new StoredDocument
    {
        Id = StoredDocument.NewId(),
        Document = DocumentDefaults.Complete(new Document
        {
            Kind = kind,
            Origin = DocumentOrigin.New,
            Customer = new Customer { Name = customer },
            Lines = lines.Select(l => new Line { Description = l.Description, Quantity = l.Quantity, UnitPrice = l.Price }).ToList(),
        }, new DocumentSettings(), DateOnly.FromDateTime(DateTime.Today)),
    });

// Nothing typed is lost: an invoice replaced by another one, and one left open when the app closes, are both saved.
{
    var invoices = store.List(DocumentKind.Invoice).Where(d => d.Xero.State == Webignite.Core.Xero.XeroWriteState.None).Take(2).ToList();
    vm.Invoices.OpenDocument(invoices[0]);
    vm.Invoices.Editor!.Reference = "Typed before another opened";
    vm.Invoices.OpenDocument(invoices[1]);
    vm.Invoices.Editor!.Reference = "Typed before closing";
    var problem = vm.PrepareToCloseAsync().GetAwaiter().GetResult();
    var kept = store.Get(invoices[0].Id)!.Document.Reference == "Typed before another opened"
        && store.Get(invoices[1].Id)!.Document.Reference == "Typed before closing" && problem is null;
    Check("Open invoices were saved when replaced and on closing", kept);
    vm.Invoices.Editor = null;
}

// Last, as its note stays on screen while the snapshot clock stands still.
// An error on the screen's thread is logged and shown as a note; the app keeps going. Exits with an error if not.
{
    Webignite.Data.AppLog.UseRoot(data);
    Background.CatchUnhandled();
    Dispatcher.UIThread.Post(() => throw new InvalidOperationException("A test problem from the snapshot tool."));
    Dispatcher.UIThread.RunJobs();
    var logged = File.Exists(Webignite.Data.AppLog.CurrentFile(DateTimeOffset.Now))
        && File.ReadAllText(Webignite.Data.AppLog.CurrentFile(DateTimeOffset.Now)).Contains("A test problem from the snapshot tool.");
    Check("An error on the screen's thread was caught and logged", logged);
    Save("problem-note");
}

Check("The new-mail check finished", mailCheck.IsCompletedSuccessfully);
if (failures.Count > 0)
{
    Console.WriteLine($"{failures.Count} check(s) failed: {string.Join("; ", failures)}");
    Environment.Exit(1);
}

// The repository's top folder, found from where the tool runs, so it works from any folder.
static string RepoRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Webignite.slnx")))
                return dir.FullName;
    throw new DirectoryNotFoundException("Run the screenshot tool from inside the Webignite repository.");
}

// A small, real PDF for the attachment preview: one A4 page with three lines of text.
static byte[] SamplePdf(params string[] lines)
{
    var text = string.Join("\n", lines.Select((l, i) => $"BT /F1 {(i == 0 ? 22 : 13)} Tf 60 {760 - i * 34} Td ({l}) Tj ET"));
    string[] objects =
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
        $"<< /Length {text.Length} >>\nstream\n{text}\nendstream",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    ];
    var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
    var offsets = new List<int>();
    for (var i = 0; i < objects.Length; i++)
    {
        offsets.Add(pdf.Length);
        pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
    }
    var xref = pdf.Length;
    pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
    foreach (var offset in offsets)
        pdf.Append($"{offset:D10} 00000 n \n");
    pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
}

// Stands in for Windows' Media Foundation in the screenshots: frames from a picture, and a note of what was trimmed.
sealed class StandInVideo(SkiaSharp.SKBitmap picture) : IVideoTools
{
    public (TimeSpan Start, TimeSpan End)? Trimmed { get; private set; }

    public TimeSpan Length(string path) => TimeSpan.FromSeconds(42);

    public RecordingFrame? FrameAt(string path, TimeSpan at)
    {
        using var bgra = picture.Copy(SkiaSharp.SKColorType.Bgra8888);
        return new RecordingFrame(bgra.Bytes, bgra.Width, bgra.Height);
    }

    public void Trim(string path, string output, TimeSpan start, TimeSpan end)
    {
        Trimmed = (start, end);
        File.Copy(path, output, overwrite: true);
    }
}
