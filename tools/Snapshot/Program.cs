using System.Security.Cryptography;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Webignite.Core.Layout;
using Webignite.Core.Documents;
using Webignite.Data;
using Webignite.Desktop;
using Webignite.Desktop.Controls;
using Webignite.Desktop.ViewModels;
using Webignite.Desktop.Views;
using Webignite.Xero.Auth;
using Webignite.Core;
using Webignite.Core.Accounts;

// Usage: dotnet run --project tools/Snapshot -- <output folder>
// Uses a throwaway database with sample documents, never the real one.
var output = args.Length > 0 ? args[0] : "screenshots";
Directory.CreateDirectory(output);
var data = Directory.CreateTempSubdirectory("webignite-snapshot-").FullName;
var snapshotKey = RandomNumberGenerator.GetBytes(32);
var store = new LocalStore(Path.Combine(data, "snapshot.db"), new Vault(snapshotKey));
// The DNS tool never reaches the network here: what it shows is handed to it below.
var noDns = new Webignite.Services.DnsClient(
    (_, _, _) => Task.FromException<byte[]>(new HttpRequestException("Couldn't look that up. Check the internet connection.")),
    (_, _, _) => Task.FromException<byte[]>(new System.Net.Sockets.SocketException()));
// Nor does the site crawler: its client answers every address with "no such server".
var noWeb = new Webignite.Services.SiteCrawler(new HttpClient(new NoWeb()));
// WebView2 is Windows only, so the web tools get a stand-in browser here, or none at all to see what they say then.
var browserThere = false;
StandInBrowser? standInBrowser = null;
StandInReader? standInReader = null;
var services = new AppServices(store)
{
    TokenStore = new MemoryTokenStore(), VaultKeySource = () => snapshotKey, Dns = noDns, Crawler = noWeb,
    NewBrowser = () => browserThere ? standInBrowser = new StandInBrowser() : null,
    NewPageReader = () => browserThere ? standInReader = new StandInReader() : null,
};
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
// A new install opens with the dock. The screens here were drawn with the sidebar, which has the most on it, so this
// run picks the sidebar; the dock and the Menu button get their own screens further down, and the memory check
// (tools/Measure) starts the app as a new install does, with the dock.
var startsWithDock = new AppearanceSettings().Menu == Webignite.Core.Layout.MenuKind.Dock;
services.Settings.Appearance = new AppearanceSettings { Motion = Motion.Off, Menu = Webignite.Core.Layout.MenuKind.Sidebar };
// WEBIGNITE_SNAPSHOT_STYLE=Airy (and WEBIGNITE_SNAPSHOT_SIDEBAR=Light) draws every screen in that style, to go
// through a style page by page. Left unset, as in CI, the screens are Classic.
if (Enum.TryParse<LookStyle>(Environment.GetEnvironmentVariable("WEBIGNITE_SNAPSHOT_STYLE"), out var wholeRunStyle))
    services.Settings.Appearance = services.Settings.Appearance with { Style = wholeRunStyle };
if (Enum.TryParse<SidebarTone>(Environment.GetEnvironmentVariable("WEBIGNITE_SNAPSHOT_SIDEBAR"), out var wholeRunSidebar))
    services.Settings.Appearance = services.Settings.Appearance with { Sidebar = wholeRunSidebar };
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
        tools.Images.ImageResults.Add(new ImageResultRow(shrunk));
        tools.Images.ImageFolder = Path.GetDirectoryName(shrunk.Output);
    }
    tools.Images.ImageMessage = $"2 images saved in the web folder: {Webignite.Core.Tools.ImageRules.Saving(before, after)}.";
}

// Light by design: opening the app builds Overview and nothing else, and reads no heavy tool from disk.
Dispatcher.UIThread.RunJobs();
Check("Opening the app builds Overview and no other page", vm.Open.Where(h => h.IsBuilt).Select(h => h.Key).SequenceEqual([Page.Overview]));
{
    string[] heavy = ["Webignite.Tools", "QRCoder", "HtmlRenderer", "Avalonia.HtmlRenderer"];
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

// The corners and room that the cards and list rows on screen have, each kind once: what a style changes. Only
// what can be seen (a hidden part takes its styles when it is next shown), and not how many or which, as a list
// draws fewer rows while they are taller and may be left scrolled elsewhere.
List<string> Shapes() => [.. AllShapes().Distinct().Order()];

List<string> AllShapes() =>
[
    .. window.GetVisualDescendants().OfType<Avalonia.Controls.Border>().Where(b => b.Classes.Contains("card") && b.IsEffectivelyVisible).Select(b => $"{b.CornerRadius} {b.Padding}"),
    .. window.GetVisualDescendants().OfType<Avalonia.Controls.ListBoxItem>().Where(i => i.IsEffectivelyVisible).Select(i => $"{i.Padding} {i.Margin} {i.CornerRadius}"),
];

// The same screen in the other styles, to compare them: Airy with a light and a dark sidebar, and the Classic
// style with a light sidebar. Classic with its dark sidebar is the screenshot under the plain name.
void SaveLooks(string name)
{
    var was = services.Settings.Appearance;
    Dispatcher.UIThread.RunJobs();
    var shapesBefore = Shapes();
    foreach (var (style, tone) in new[] { (LookStyle.Airy, SidebarTone.Light), (LookStyle.Airy, SidebarTone.Dark), (LookStyle.Classic, SidebarTone.Light) })
    {
        services.Settings.Appearance = was with { Style = style, Sidebar = tone };
        Dispatcher.UIThread.RunJobs();
        Save($"look-{style.ToString().ToLowerInvariant()}-{tone.ToString().ToLowerInvariant()}-{name}");
    }
    services.Settings.Appearance = was;
    Dispatcher.UIThread.RunJobs();
    // Drawn once more, so rows the list let go of while they were taller are back before anything is checked.
    window.CaptureRenderedFrame()?.Dispose();
    Dispatcher.UIThread.RunJobs();
    Check($"After trying the other styles on {name}, Classic is back with nothing of them switched on",
        was.Style != LookStyle.Classic || was.Sidebar != SidebarTone.Dark
        || Looks.Style == LookStyle.Classic && !window.Classes.Any(c => c.StartsWith("look-", StringComparison.Ordinal))
            && !Application.Current!.Resources.ContainsKey("Nav")
            && !Application.Current.Resources.ContainsKey("Text12"));
    // Parts that were on screen while another style was on must have their own shape again, not only new ones.
    Check($"After trying the other styles on {name}, no card or list row on screen has a shape it didn't have before",
        was.Style != LookStyle.Classic || Shapes().All(shapesBefore.Contains));
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
Wait(vm.Invoices.BringInFromXeroCommand.ExecuteAsync(null), "Bringing quotes and invoices in from Xero by hand");
Check("Bringing invoices in without Xero says to connect it", vm.Invoices.XeroMessage == "Connect Xero in Settings first.");
Save("invoices-bring-in-from-xero");
vm.Invoices.XeroMessage = "";
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
SaveLooks("invoice-editor");
// Items for Xero's Item column, a number of our own and a discount on the whole invoice.
invoice.Lines[0].Item = "Hosting";
invoice.Lines[1].Item = "Content updates";
invoice.NumberText = "WEB-2026-014";
invoice.DiscountText = "10%";
Dispatcher.UIThread.RunJobs();
Check("Items, a number and a 10% discount go on the document, and the total takes the discount off",
    invoice.Build() is { Number: "WEB-2026-014", DocumentDiscountPercent: 10m } built && built.Lines[0].Product == "Hosting"
    && invoice.HasDiscount && invoice.DiscountLabel == "Discount (10%)" && built.Total == Money.Cents((450m + 3.5m * 650m) * 0.9m));
invoice.DiscountText = "lots";
Dispatcher.UIThread.RunJobs();
Check("A discount that makes no sense says so", invoice.Problems.Any(p => p.Contains("10%", StringComparison.Ordinal)));
invoice.DiscountText = "R 200";
Dispatcher.UIThread.RunJobs();
Save("invoice-items-discount");
invoice.Lines[0].Item = invoice.Lines[1].Item = invoice.NumberText = invoice.DiscountText = "";
invoice.SaveCommand.Execute(null);

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
{
    // A draft already in Xero, changed here and then updated in Xero.
    var editor = new DocumentEditorViewModel(verified, services, () => vm.Invoices.Editor = null);
    vm.Invoices.Editor = editor;
    Check("A draft Webignite put in Xero can be changed", editor.CanChangeDraft && !editor.IsEditable);
    editor.ChangeDraftCommand.Execute(null);
    Check("Change this draft unlocks it and offers Update draft in Xero", editor.IsEditable && editor.IsChangingInXero && !editor.ShowCreate);
    Save("invoice-changing-in-xero");
    vm.Invoices.Editor = null;
    store.Save(store.Get(verified.Id)! with { Xero = verified.Xero });
}
{
    // A sent quote: the client's answer, and the invoice made from it linked both ways. Both go again after.
    var sentQuote = store.Save(DocumentCopies.Copy(verified, DocumentKind.Quote, services.Settings.Documents, DateOnly.FromDateTime(DateTime.Today)) with
    {
        Status = DocumentStatus.InXero,
        Xero = new() { Id = "q77", Number = "QU-0077", State = Webignite.Core.Xero.XeroWriteState.Verified, Status = "SENT", ContactId = "k1" },
        Delivery = new() { Emailed = DateTimeOffset.UtcNow.AddDays(-3), EmailedTo = "Thandi Mokoena" },
    });
    var fromQuote = store.Save(DocumentCopies.Copy(sentQuote, DocumentKind.Invoice, services.Settings.Documents, DateOnly.FromDateTime(DateTime.Today)));
    var quoteEditor = new DocumentEditorViewModel(sentQuote, services, () => vm.Invoices.Editor = null);
    vm.Invoices.Editor = quoteEditor;
    Check("A sent quote offers Client accepted and declined", quoteEditor.CanDecideQuote);
    Check("The quote links to the invoice made from it", quoteEditor.Links.Count == 1 && quoteEditor.Links[0].Text == "Draft invoice made from it");
    Save("quote-sent-answer");
    var invoiceEditor = new DocumentEditorViewModel(fromQuote, services, () => vm.Invoices.Editor = null);
    vm.Invoices.Editor = invoiceEditor;
    Check("The invoice links back to its quote", invoiceEditor.Links.Count == 1 && invoiceEditor.Links[0].Text == "Made from quote QU-0077");
    Save("invoice-from-quote-link");
    vm.Invoices.Editor = null;
    store.DeleteDocument(fromQuote.Id);
    store.DeleteDocument(sentQuote.Id);
}
{
    // An approved invoice put right: a credit note drafted in Xero, or voiding it, each asked first.
    var approved = store.Save(store.Get(verified.Id)! with
    {
        Xero = verified.Xero with { Status = "AUTHORISED" },
        Delivery = new() { Approved = DateTimeOffset.UtcNow.AddDays(-2), Emailed = DateTimeOffset.UtcNow.AddDays(-2), EmailedTo = "Thandi Mokoena" },
        CreditNotes = [new CreditNoteLink { Id = "cn1", Number = "CN-0004", Amount = 450m, Reason = "Hosting cancelled", Drafted = DateTimeOffset.UtcNow.AddDays(-1) }],
        Attachments = [new XeroAttachment { Name = "Timesheet October.pdf", Bytes = 48_211, Online = true, Added = DateTimeOffset.UtcNow.AddDays(-2) }],
    });
    var fixer = new DocumentEditorViewModel(approved, services, () => vm.Invoices.Editor = null);
    vm.Invoices.Editor = fixer;
    Check("An approved invoice offers a credit note and voiding", fixer.CanCredit && fixer.CanVoid && fixer.VoidLabel == "Void in Xero");
    Check("A file can be attached in Xero, and the ones attached are listed", fixer.CanAttach && fixer.AttachmentsText.Count == 1);
    Save("invoice-attachments");
    fixer.StartCreditCommand.Execute(null);
    fixer.CreditAmountText = "500";
    fixer.CreditReason = "Two hours not used in November";
    Save("invoice-credit-note");
    fixer.CancelCreditCommand.Execute(null);
    fixer.StartVoidCommand.Execute(null);
    Check("Voiding asks first", fixer.ConfirmingVoid);
    Save("invoice-void-confirm");
    fixer.CancelVoidCommand.Execute(null);
    Check("An approved invoice with money owed offers Record a payment", fixer.CanRecordPayment);
    Wait(fixer.StartPaymentCommand.ExecuteAsync(null), "Opening Record a payment");
    fixer.BankAccounts.Add(new Webignite.Core.Xero.XeroBankAccount("b1", "FNB Business Cheque"));
    fixer.PaymentAccount = fixer.BankAccounts[0];
    fixer.PaymentAmountText = "1500.00";
    Save("invoice-record-payment");
    vm.Invoices.Editor = null;
    store.Save(store.Get(verified.Id)! with { Xero = verified.Xero, Delivery = verified.Delivery, CreditNotes = [], Attachments = [] });
}

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
// Folded, each group is one icon; its pages open in a list beside it, as on a hover.
Check("The folded sidebar shows each group as one icon, not every page",
    vm.Sidebar.IconPages.OfType<NavGroup>().Select(g => g.Key).SequenceEqual(["my-pages", "client-work", "billing", "computer", "web-tools"])
    && vm.Sidebar.IconPages.OfType<Module>().Select(m => m.Key).SequenceEqual([Page.Overview, Page.Approvals, Page.Mail, Page.Settings]));
var clientWork = vm.Sidebar.IconPages.OfType<NavGroup>().First(g => g.Key == "client-work");
Check("A group's list has its pages", clientWork.Entries.Select(e => e.Title).SequenceEqual(["Clients", "Contacts", "Retainers", "Domains and hosting"]));
var groupIcon = window.GetVisualDescendants().OfType<Avalonia.Controls.Panel>()
    .First(p => p.DataContext == clientWork && Avalonia.Controls.Primitives.FlyoutBase.GetAttachedFlyout(p) is not null);
Avalonia.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(groupIcon);
Dispatcher.UIThread.RunJobs();
Save("sidebar-icons-group");
Avalonia.Controls.Primitives.FlyoutBase.GetAttachedFlyout(groupIcon)!.Hide();
Check("A page outside a group is picked on the strip as itself", vm.SelectedIcon == vm.Selected);
vm.Sidebar.Toggle();
Save("sidebar-hidden");
vm.Sidebar.Toggle();
vm.Sidebar.Items.OfType<NavGroup>().First(g => g.Key == "computer").MoveUpCommand.Execute(null);
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
// How quote and invoice mails look: the details for the branded layout, below the wording.
settingsPage.MailBrand.BrandCompany = "Webignite (Pty) Ltd";
settingsPage.MailBrand.BrandPhone = "021 555 0100";
settingsPage.MailBrand.BrandEmail = "accounts@webignite.co.za";
settingsPage.MailBrand.BrandWebsite = "www.webignite.co.za";
settingsPage.MailBrand.BrandAddress = "1 Sample Street, Cape Town, 8001";
settingsPage.MailBrand.BrandBank = "Sample Bank\nCheque account\nAccount number: 0000000000";
settingsPage.MailBrand.BrandTerms = "https://webignite.co.za/terms/";
settingsPage.MailBrand.SaveBrandCommand.Execute(null);
Check("The mail layout's details are kept and shared", services.Settings.MailBrand.CompanyName == "Webignite (Pty) Ltd"
    && Webignite.Core.Sync.SharedSettings.IsShared("mail-brand") && !settingsPage.MailBrand.BrandProblem);
window.Height = 2900;
Dispatcher.UIThread.RunJobs();
Save("settings-mail-brand");
window.Height = 860;
Dispatcher.UIThread.RunJobs();
settingsPage.Open("mail");
Check("Settings, Mail shows the spelling choices", settingsPage.Spelling.SpellCheckOn && settingsPage.Spelling.SpellLanguages.Count > 0 && settingsPage.Spelling.SpellNote.Length > 0);
Save("settings-mail");
settingsPage.Open("organisation");
settingsPage.Account.PartnerName = "Jaco";
settingsPage.Account.SaveTeamCommand.Execute(null);
Check("Settings, Your organisation names you and your partner", services.Team().Partner!.Name == "Jaco" && services.Team().Me!.Name == "Me" && !settingsPage.Account.TeamProblem);
settingsPage.Account.PartnerName = "me";
settingsPage.Account.SaveTeamCommand.Execute(null);
Check("Your partner can't have your name", settingsPage.Account.TeamProblem && services.Team().Partner!.Name == "Jaco");
settingsPage.Account.PartnerName = "Jaco";
settingsPage.Account.SaveTeamCommand.Execute(null);
Save("settings-organisation-names");
// ChatGPT, step by step: a tunnel and a key from OpenAI, then Connect ChatGPT runs OpenAI's tunnel in the background.
settingsPage.Open("assistants");
settingsPage.ChatGpt.ChatGptTunnelId = "tunnel_ABC";
Wait(settingsPage.ChatGpt.ConnectChatGptCommand.ExecuteAsync(null), "Connecting ChatGPT with a wrong tunnel id");
Check("A wrong tunnel id is explained before anything is downloaded", settingsPage.ChatGpt.ChatGptProblem && settingsPage.ChatGpt.ChatGptStatus.Contains("tunnel_", StringComparison.Ordinal)
    && !settingsPage.ChatGpt.ChatGptConnected);
settingsPage.ChatGpt.ChatGptTunnelId = "tunnel_0123456789abcdef0123456789abcdef";
settingsPage.ChatGpt.ChatGptStatus = "Not connected yet.";
settingsPage.ChatGpt.ChatGptProblem = false;
Dispatcher.UIThread.RunJobs();
Check("The ChatGPT desktop app's way shows the connector's full path to copy", settingsPage.ChatGpt.ConnectorPath.Contains("Webignite.Connector", StringComparison.Ordinal)
    && settingsPage.ChatGpt.CopyConnectorPathCommand is not null);
window.Height = 1500;
Dispatcher.UIThread.RunJobs();
Save("settings-chatgpt");
window.Height = 860;
Dispatcher.UIThread.RunJobs();
settingsPage.Open("account");
// Joining needs nothing but the invite: our own sync server is filled in, shown as a line with Change.
settingsPage.Account.ShowAccountFormCommand.Execute("join");
Dispatcher.UIThread.RunJobs();
Check("Our own sync server is filled in and its box is folded away", settingsPage.Account.SyncServer == SyncAddress.Ours && !settingsPage.Account.ShowSyncServerBox);
Save("settings-account-join");
settingsPage.Account.ChangeSyncServerCommand.Execute(null);
Check("Change opens the sync server's box", settingsPage.Account.ShowSyncServerBox);
settingsPage.Account.ShowAccountFormCommand.Execute("new");
settingsPage.Account.SyncServer = "http://sync.webignite.co.za";
Wait(settingsPage.Account.CreateAccountCommand.ExecuteAsync(null), "Creating an account against a plain-http server");
Check("The sync server must be https", settingsPage.Account.SyncProblem && settingsPage.Account.SyncMessage.Contains("https://", StringComparison.Ordinal));
settingsPage.Account.SyncServer = "https://sync.webignite.co.za";
settingsPage.Account.AccountEmail = "eddie@webignite.co.za";
settingsPage.Account.NewWorkspaceName = "Webignite";
Dispatcher.UIThread.RunJobs();
Save("settings-account");
settingsPage.Account.ShowAccountFormCommand.Execute("recover");
Dispatcher.UIThread.RunJobs();
Check("Forgot your password asks for the recovery key and a new password twice", settingsPage.Account.IsRecoverForm && settingsPage.Account.NeedsNewPassword);
Save("settings-account-recover");
settingsPage.Account.ShowAccountFormCommand.Execute("sign-in");
// Signed in needs a running sync server, which the snapshot doesn't have: the screen is shown as it would be.
void SetShown(string property, object value) =>
    typeof(SettingsAccountViewModel).GetProperty(property)!.SetValue(settingsPage.Account, value);
SetShown(nameof(SettingsAccountViewModel.IsSignedIn), true);
SetShown(nameof(SettingsAccountViewModel.HasWorkspaceKey), true);
SetShown(nameof(SettingsAccountViewModel.IsWorkspaceOwner), true);
SetShown(nameof(SettingsAccountViewModel.SignedInAs), "Signed in as eddie@webignite.co.za, in the workspace Webignite.");
SetShown(nameof(SettingsAccountViewModel.LastSyncedText), $"Last synced {new DateTime(2026, 11, 8, 14, 32, 0):g}.");
Dispatcher.UIThread.RunJobs();
Check("Signed in shows Sync now and when it last synced", settingsPage.Account.SyncNowCommand is not null && settingsPage.Account.LastSyncedText.StartsWith("Last synced", StringComparison.Ordinal));
Save("settings-account-signed-in");
// Your organisation: who's in the workspace with their names, emails, roles and status; the owner can take a partner out, after a yes.
settingsPage.Open("organisation");
SetShown(nameof(SettingsAccountViewModel.OrganisationText), "Webignite: 2 people");
settingsPage.Account.Members.Clear();
settingsPage.Account.Members.Add(new MemberRowViewModel("Eddie", "eddie@webignite.co.za", "Owner", true, Webignite.Core.Chat.PresenceState.Available, false, settingsPage.Account));
settingsPage.Account.Members.Add(new MemberRowViewModel("Jaco", "jaco@webignite.co.za", "Member", false, Webignite.Core.Chat.PresenceState.Away, true, settingsPage.Account));
Dispatcher.UIThread.RunJobs();
Check("Your organisation lists everyone with their initials, email, role and status", settingsPage.ShowOrganisation && settingsPage.Account.Members.Count == 2
    && settingsPage.Account.Members[1].ShownText == "Away" && settingsPage.Account.Members[0].IsMe);
Save("settings-organisation");
settingsPage.Account.Members[1].RemoveCommand.Execute(null);
Dispatcher.UIThread.RunJobs();
Check("Taking a partner out asks first", settingsPage.Account.ConfirmingRemove && settingsPage.Account.RemoveQuestion.Contains("jaco@webignite.co.za", StringComparison.Ordinal));
Save("settings-organisation-take-out");
settingsPage.Account.KeepMemberCommand.Execute(null);
settingsPage.Account.Members.Clear();
// A new password, or other PCs signed out.
settingsPage.Open("account");
settingsPage.Account.StartChangeAccountPasswordCommand.Execute(null);
Dispatcher.UIThread.RunJobs();
Check("Change password asks for the current one and the new one twice", settingsPage.Account.ChangingAccountPassword);
Save("settings-account-people-and-password");
settingsPage.Account.CancelChangeAccountPasswordCommand.Execute(null);
// The invite: one message with the install link, the code and the steps, to copy or send by mail.
SetShown(nameof(SettingsAccountViewModel.InviteText), "K7QM-2XRT-9WDF-HB4N-PZ6C");
SetShown(nameof(SettingsAccountViewModel.InviteMessage), Webignite.Core.Accounts.InvitePackage.Message("K7QM-2XRT-9WDF-HB4N-PZ6C",
    new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero), "Eddie", System.Globalization.CultureInfo.GetCultureInfo("en-ZA")));
window.Height = 1500;
Dispatcher.UIThread.RunJobs();
Check("An invite shows the whole message with the install link and the code, to copy or mail",
    settingsPage.Account.HasInvite && settingsPage.Account.InviteMessage.Contains(Webignite.Core.Accounts.InvitePackage.InstallLink, StringComparison.Ordinal)
    && settingsPage.Account.CopyInviteCommand is not null && settingsPage.Account.MailInviteCommand is not null);
Save("settings-account-invite");
window.Height = 860;
SetShown(nameof(SettingsAccountViewModel.InviteText), "");
SetShown(nameof(SettingsAccountViewModel.InviteMessage), "");
Dispatcher.UIThread.RunJobs();
SetShown(nameof(SettingsAccountViewModel.IsSignedIn), false);
// The welcome screen: off while testing, so the app opens straight in; on, it asks to sign in when nobody has.
Check("While testing, the app opens without asking to sign in", !vm.WelcomePart.ShowingWelcome);
MainWelcomePart.AskToSignInAtStart = true;
vm.WelcomePart.Welcome();
Dispatcher.UIThread.RunJobs();
Check("Switched on, the welcome screen shows when nobody is signed in", vm.WelcomePart.ShowingWelcome);
Save("welcome");
vm.WelcomePart.WelcomeCreateCommand.Execute(null);
Dispatcher.UIThread.RunJobs();
Check("Create an account carries on in Settings, Account and sync, on the new account form, with the guide above",
    !vm.WelcomePart.ShowingWelcome && vm.Selected.Key == Page.Settings && settingsPage.Account.IsNewAccountForm && vm.WelcomePart.ShowingGuide);
vm.WelcomePart.EndGuideCommand.Execute(null);
// The setup guide: joining, Xero and ChatGPT on the real Settings page, then a look round the main pages.
vm.WelcomePart.WelcomeJoinCommand.Execute(null);
Dispatcher.UIThread.RunJobs();
Check("Join with an invite code starts the guide on the join form; the step waits for signing in",
    vm.WelcomePart.ShowingGuide && vm.WelcomePart.GuideStep.Key == "join" && vm.Selected.Key == Page.Settings && settingsPage.Account.IsJoinForm
    && !vm.WelcomePart.GuideStepDone && vm.WelcomePart.GuideNextLabel == "Skip for now" && !vm.WelcomePart.GuideCanGoBack);
Save("setup-guide-join");
vm.WelcomePart.GuideNextCommand.Execute(null);
Dispatcher.UIThread.RunJobs();
Check("Then Xero, on its own Settings section", vm.WelcomePart.GuideStep.Key == "xero" && settingsPage.ShowXero && vm.WelcomePart.GuideCanGoBack);
Save("setup-guide-xero");
vm.WelcomePart.GuideNextCommand.Execute(null);
Dispatcher.UIThread.RunJobs();
Check("Then ChatGPT", vm.WelcomePart.GuideStep.Key == "chatgpt" && settingsPage.ShowAssistants);
Save("setup-guide-chatgpt");
vm.WelcomePart.GuideNextCommand.Execute(null);
vm.WelcomePart.GuideNextCommand.Execute(null);
Dispatcher.UIThread.RunJobs();
Check("The look round shows the real pages: To approve", vm.WelcomePart.GuideStep.Key == "approvals" && vm.Selected.Key == Page.Approvals && vm.WelcomePart.GuideNextLabel == "Next");
Save("setup-guide-approvals");
while (vm.WelcomePart.GuideStep.Key != "done")
    vm.WelcomePart.GuideNextCommand.Execute(null);
Check("The last step finishes", vm.WelcomePart.GuideNextLabel == "Finish");
vm.WelcomePart.GuideNextCommand.Execute(null);
Check("Finished, the guide goes and doesn't come back on its own", !vm.WelcomePart.ShowingGuide && services.Settings.SetupGuide is { Finished: true });
vm.WelcomePart.Welcome();
Check("Opening again while signed out asks to sign in, but doesn't start the guide by itself", !vm.WelcomePart.ShowingGuide && vm.WelcomePart.ShowingWelcome);
vm.WelcomePart.WelcomeNotNowCommand.Execute(null);
vm.Selected = vm[Page.Settings];
settingsPage.Account.ShowAccountFormCommand.Execute("sign-in");
MainWelcomePart.AskToSignInAtStart = false;
settingsPage.Open("xero");

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
Open(Page.Overview);

store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e1", Title = "Kick-off with Bosveld Builders", Date = today, Start = new TimeOnly(10, 0), Location = "Teams" });
store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e2", Title = "Fynbos Physio photo shoot", Date = today.AddDays(2), Start = new TimeOnly(14, 30) });
store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e3", Title = "Public holiday", Date = today.AddDays(-5) });
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t5", Title = "Write blog post on site speed", Created = DateTimeOffset.UtcNow });
Open(Page.Calendar);
Save("calendar");
Open(Page.Planner);
Save("planner");
// The diary: today and its neighbour open like a book, written from the calendar, the to-dos and the money dates.
{
    store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e-teams", Title = "Karoo Coffee monthly catch-up", Date = today, Start = new TimeOnly(14, 0), End = new TimeOnly(14, 45),
        Location = "Microsoft Teams", JoinLink = "https://teams.microsoft.com/l/meetup-join/19%3ameeting_demo/0" });
    store.SaveDiaryNote(today, "Ask Pieter about the new product photos.\nBank: check the Fynbos payment came through.");
    Open(Page.Diary);
    var diary = (DiaryViewModel)vm.Current.Page;
    diary.Show(today);
    Dispatcher.UIThread.RunJobs();
    var page = diary.Left.Date == today ? diary.Left : diary.Right;
    Check("The diary opens at today, a day to a page, with tomorrow or yesterday beside it",
        diary.Right.Date == diary.Left.Date.AddDays(1) && page.IsToday && page.Lines.Count >= 12);
    Check("An event sits on its hour, and a meeting from a mail has its Join link",
        page.Lines.Single(l => l.Hour.Hour == 10).Entries.Any(e => e.Title.StartsWith("Kick-off", StringComparison.Ordinal)) && page.Lines.Single(l => l.Hour.Hour == 14).Entries.Single().CanJoin);
    Check("To-dos due that day are on the page to tick off, and its notes are there", page.Untimed.Any(e => e.CanTick) && page.Notes.StartsWith("Ask Pieter", StringComparison.Ordinal));
    var line = page.Lines.Single(l => l.Hour.Hour == 16);
    line.NewText = "16:30 Phone the printers";
    line.AddCommand.Execute(null);
    page = diary.Left.Date == today ? diary.Left : diary.Right;
    Check("Writing on an hour's line puts it in the calendar at the time said",
        store.ListEvents(today, today).Any(e => e is { Title: "Phone the printers" } && e.Start == new TimeOnly(16, 30)) && page.Lines.Single(l => l.Hour.Hour == 16).Entries.Count == 1);
    page.NewTodo = "Renew the SSL on bosveldbuilders.co.za";
    page.AddTodoCommand.Execute(null);
    page = diary.Left.Date == today ? diary.Left : diary.Right;
    var added = page.Untimed.Single(e => e.Title.StartsWith("Renew the SSL", StringComparison.Ordinal));
    Check("A to-do written on the page is a to-do for that day", store.ListTasks().Any(t => t.Title.StartsWith("Renew the SSL", StringComparison.Ordinal) && t.Due == today && !t.Done));
    added.IsDone = true;
    Check("Ticking it on the page ticks it off for good", store.ListTasks().Single(t => t.Title.StartsWith("Renew the SSL", StringComparison.Ordinal)).Done);
    Dispatcher.UIThread.RunJobs();
    Save("diary");
    var was = diary.Left.Date;
    diary.NextPagesCommand.Execute(null);
    Check("Turning the page moves on two days", diary.Left.Date == was.AddDays(2));
    diary.GoToTodayCommand.Execute(null);
    foreach (var e in store.ListEvents(today, today).Where(e => e.Title == "Phone the printers" || e.Id == "e-teams"))
        store.DeleteEvent(e.Id);
    foreach (var t in store.ListTasks().Where(t => t.Title.StartsWith("Renew the SSL", StringComparison.Ordinal)))
        store.DeleteTask(t.Id);
    store.SaveDiaryNote(today, "");
}
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
// Opens a folding row on the client page on screen, for a screenshot of what's in it.
void OpenSection(string key)
{
    if (vm.Clients.Detail is { } shown && !shown.Sections.IsOpen(key))
        shown.Sections.Toggle(key);
}
Open(Page.Clients);
Check("A page in a group shows as picked on the folded strip by its group's icon", vm.SelectedIcon is NavGroup { Key: "client-work" });
Save("clients");
{
    // Minimising a pane: the handle on the divider, the strip it leaves, the chip in the sidebar, and the button on the list.
    void Click(Avalonia.Controls.Button? button)
    {
        button?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        button?.Command?.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();
    }
    Avalonia.Controls.Button? Find(string kind) => window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().FirstOrDefault(b => b.Classes.Contains(kind) && b.IsEffectivelyVisible);
    var clientsPage = vm.Clients;
    clientsPage.OpenCommand.Execute(clientsPage.Rows.First());
    Dispatcher.UIThread.RunJobs();
    var handle = Find("panehandle");
    handle?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    Check("The handle on the divider minimises the client list", handle is not null && !clientsPage.Panels["list"].IsShown);
    Check("A minimised pane shows as a chip under its page in the sidebar", vm.HiddenPanes.Select(p => p.Title).SequenceEqual(["Client list"]) && Find("panechip") is not null
        && Find("panerail") is not null);
    Save("clients-list-minimised");
    Click(Find("panechip"));
    Check("The chip in the sidebar brings it back", clientsPage.Panels["list"].IsShown && vm.HiddenPanes.Count == 0);
    var minimise = window.GetVisualDescendants().OfType<Avalonia.Controls.Button>()
        .FirstOrDefault(b => ReferenceEquals(b.Command, clientsPage.Panels["list"].MinimiseCommand));
    Click(minimise);
    Check("The minimise button on the list works too", minimise is not null && !clientsPage.Panels["list"].IsShown);
    var strip = Find("panerail");
    strip?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    Check("The strip brings it back", clientsPage.Panels["list"].IsShown);
    clientsPage.Detail?.BackCommand.Execute(null);
}
Wait(vm.Clients.BringInFromXeroCommand.ExecuteAsync(null), "Bringing clients in from Xero by hand");
Check("Bringing clients in without Xero says to connect it", vm.Clients.XeroMessage == "Connect Xero in Settings first.");
vm.Clients.XeroMessage = "";
vm.Clients.OpenCommand.Execute(vm.Clients.Rows[0]);
Save("client");
SaveLooks("client");
{
    // Claude changes the client's email (a mail could have tricked it): the page asks for a check until It's right.
    var shown = vm.Clients.Detail!;
    var name = vm.Clients.Rows[0].Client.Name;
    var connector = new Webignite.Services.AssistantBusiness(services.Store);
    var before = services.Clients.Find(name)!.Email;
    connector.UpdateClient(name, email: "accounts@new-address.co.za");
    shown.Refresh();
    Dispatcher.UIThread.RunJobs();
    Check("A client email changed by Claude asks for a check on their page", shown.HasCheck && shown.Email == "accounts@new-address.co.za");
    Save("client-check");
    shown.ItsRightCommand.Execute(null);
    Check("It's right clears the check", !shown.HasCheck && services.Clients.Find(name)!.Check is null);
    services.Clients.Change(services.Clients.Find(name)!.Id, c => c with { Email = before });
    shown.Refresh();
}
{
    // The client page: Details, and everything else as rows that fold open, in an order that's remembered.
    var detail = vm.Clients.Detail!;
    Dispatcher.UIThread.RunJobs();
    var rows = window.GetVisualDescendants().OfType<Webignite.Desktop.Controls.FoldSection>().ToList();
    Check("A client's page has its details and eight rows that fold, with To-do and Quotes open at first",
        rows.Count == 8 && rows.Where(r => r.IsOpen).Select(r => r.Key).SequenceEqual(["todo", "documents"]) && rows.All(r => !string.IsNullOrEmpty(r.Summary)));
    var mailRow = rows.First(r => r.Key == "mail");
    var header = mailRow.GetVisualDescendants().OfType<Avalonia.Controls.Button>().First(b => b.Name == "PART_Header");
    header.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    Check("Clicking a row's header opens it", mailRow.IsOpen && detail.Sections.IsOpen("mail"));
    header.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    detail.Sections.MoveTo("logins", 0);
    Dispatcher.UIThread.RunJobs();
    var list = window.GetVisualDescendants().OfType<Webignite.Desktop.Controls.FoldList>().First();
    Check("A row moved to the top shows there", list.Children.OfType<Webignite.Desktop.Controls.FoldSection>().First().Key == "logins");
    vm.Clients.OpenCommand.Execute(vm.Clients.Rows[1]);
    Dispatcher.UIThread.RunJobs();
    Check("The order and what's open are the same for the next client", vm.Clients.Detail!.Sections.Layout.Order[0] == "logins"
        && !vm.Clients.Detail.Sections.IsOpen("mail"));
    vm.Clients.Detail.Sections.MoveTo("logins", 7);
    vm.Clients.OpenCommand.Execute(vm.Clients.Rows[0]);
    Dispatcher.UIThread.RunJobs();
}
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
// Bills to pay, as read from Xero.
store.SetSetting("xero-bills", new Webignite.Core.Finance.BillsState
{
    Read = DateTimeOffset.UtcNow,
    Open =
    [
        new() { Id = "b1", Number = "INV-7781", Supplier = "xneelo", Status = "AUTHORISED", Total = 1899m, AmountDue = 1899m, Due = today.AddDays(-3) },
        new() { Id = "b2", Number = "ADB-20931", Supplier = "Adobe", Status = "AUTHORISED", Total = 1150m, AmountDue = 1150m, Due = today.AddDays(5) },
        new() { Id = "b3", Reference = "Banner print", Supplier = "Printing.co.za", Status = "DRAFT", Total = 640m, AmountDue = 640m, Due = today.AddDays(14) },
    ],
});
Open(Page.Money);
(window.GetVisualDescendants().OfType<MoneyView>().FirstOrDefault()?.DataContext as MoneyViewModel)?.Refresh();
Dispatcher.UIThread.RunJobs();
Save("money");
Check("Money shows the bills to pay from Xero", window.GetVisualDescendants().OfType<MoneyView>().FirstOrDefault()?.DataContext is MoneyViewModel billsPage && billsPage.BillRows.Count == 3 && billsPage.BillRows[0].IsOverdue);
{
    // A supplier's PDF read into the bill boxes, to check before it goes to Xero as a draft.
    var billPage = window.GetVisualDescendants().OfType<MoneyView>().First().DataContext as MoneyViewModel;
    var billFile = Path.Combine(data, "xneelo November.pdf");
    File.WriteAllBytes(billFile, SamplePdf("xneelo (Pty) Ltd", "Tax Invoice", "Invoice Number: INV-7781", "Invoice Date: 01/11/2026",
        "Due Date: 08/11/2026", "Dedicated server, November   1 899.00", "Amount Due   R 1 899.00"));
    Wait(billPage!.StartBillAsync(billFile), "Reading a supplier's PDF bill");
    Check("A supplier's PDF is read into the bill boxes", billPage.BillSupplier == "xneelo (Pty) Ltd" && billPage.BillNumber == "INV-7781"
        && billPage.BillTotal.StartsWith("1899", StringComparison.Ordinal) && billPage.BillDue == new DateTime(2026, 11, 8));
    billPage.ExpenseAccounts.Add(new Webignite.Core.Xero.XeroSalesAccount("441", "Hosting and servers"));
    billPage.BillAccount = billPage.ExpenseAccounts[0];
    Save("money-bill-from-pdf");
    billPage.CancelBillCommand.Execute(null);
}

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
{
    // Xero's repeating invoices, each able to become a retainer, made paused so nobody is billed twice.
    Webignite.Core.Retainers.XeroRepeatingInvoice Repeating(string id, string client, string reference, decimal amount, string unit = "MONTHLY") => new()
    {
        Id = id, ContactName = client, Reference = reference, Total = amount, Unit = unit, Status = "AUTHORISED", NextDate = today.AddDays(22),
        Lines = [Priced(reference, amount)],
    };
    retainersPage.ShowRepeating([Repeating("x1", "Bosveld Builders", "Website care plan", 1250m), Repeating("x2", "Fynbos Physio", "Weekly social post", 600m, "WEEKLY")]);
    Check("Only monthly repeating invoices can become retainers", retainersPage.Repeating[0].CanMake && !retainersPage.Repeating[1].CanMake);
    Save("retainers-from-xero");
    retainersPage.Repeating[0].MakeCommand.Execute(null);
    var fromXero = store.ListRetainers().Single(r => r.FromXeroRepeating == "x1");
    Check("A repeating invoice becomes a paused retainer and says to stop Xero's", fromXero.Paused && retainersPage.Message.Contains("Stop the repeating invoice in Xero", StringComparison.Ordinal)
        && !retainersPage.Repeating[0].CanMake);
    Save("retainers-from-xero-made");
    store.DeleteRetainer(fromXero.Id);
    retainersPage.HideXeroRepeatingCommand.Execute(null);
    retainersPage.Message = "";
    retainersPage.Refresh();
}

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
// The whole Money page at once, wide and tall, to see that nothing runs under anything else.
{
    var (wide, tall) = (window.Width, window.Height);
    window.Width = 1700;
    window.Height = 2100;
    Dispatcher.UIThread.RunJobs();
    Save("money-whole");
    window.Width = wide;
    window.Height = tall;
    Dispatcher.UIThread.RunJobs();
}
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
vm.Mail.Status = "";
vm.Mail.StatusIsProblem = false;
var opened = vm.Mail.Messages.First(m => m.Summary.Uid == 41);
Wait(vm.Mail.Select(opened), "The mail opened");
Save("mail");
SaveLooks("mail");
{
    // A hidden pane leaves a strip with its name; a click on it, or Ctrl+Shift+1, brings it back.
    vm.TogglePaneCommand.Execute("1");
    Dispatcher.UIThread.RunJobs();
    var rail = window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().FirstOrDefault(b => b.Classes.Contains("panerail"));
    Check("Ctrl+Shift+1 hides Mail's folders and leaves a strip to bring them back", !vm.Mail.Panels["folders"].IsShown && rail is not null);
    Save("mail-folders-hidden");
    rail?.Command?.Execute(null);
    rail?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    Check("Clicking the strip brings the folders back", vm.Mail.Panels["folders"].IsShown);
    vm.TogglePaneCommand.Execute("2");
    Dispatcher.UIThread.RunJobs();
    Check("Ctrl+Shift+2 hides the mail list too, and the open mail gets the room", !vm.Mail.Panels["messages"].IsShown && vm.Mail.Panels["reader"].IsShown);
    Save("mail-list-hidden");
    vm.TogglePaneCommand.Execute("3");
    vm.TogglePaneCommand.Execute("1");
    Check("One pane always stays, whichever are hidden", vm.Mail.Panels["folders"].IsShown && !vm.Mail.Panels["reader"].IsShown);
    vm.Mail.Panels.ResetCommand.Execute(null);
    Check("Put the panes back shows all three again", vm.Mail.Panels["messages"].IsShown && vm.Mail.Panels["reader"].IsShown && vm.Mail.Panels["folders"].IsShown);
}
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
// Asana's dark look, as Eddie uses it: calm rows, and a mailbox folded up to its name and Inbox.
services.Settings.Appearance = services.Settings.Appearance with { Theme = Theme.Dark, DarkPalette = DarkPalette.Asana };
{
    var foldedBox = vm.Mail.Mailboxes.First(m => m.HasTitle);
    foldedBox.ToggleFoldCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    Check("A folded mailbox shows only its name, Inbox and the folder open, and stays folded",
        foldedBox.IsFolded && foldedBox.Folders.Any(f => f.IsHidden)
        && foldedBox.Folders.Where(f => !f.IsHidden).All(f => f.Role == Webignite.Core.Mail.FolderRole.Inbox || f.IsCurrent)
        && services.Settings.FoldedMailboxes.Contains(foldedBox.Account!.Id));
    Save("dark-mail-asana");
    foldedBox.ToggleFoldCommand.Execute(null);
    Check("Opened again, every folder shows", !foldedBox.IsFolded && foldedBox.Folders.All(f => !f.IsHidden) && services.Settings.FoldedMailboxes.Count == 0);
}
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
// Reading a mail takes it off Mail's count in the sidebar at once, not at the next mail check.
var unreadMail = vm.Mail.Messages.FirstOrDefault(m => !m.Summary.Seen && m.Summary.Folder == "INBOX");
if (unreadMail is not null)
    Wait(vm.Mail.Select(unreadMail), "An unread mail opened");
Dispatcher.UIThread.RunJobs();
var inboxesUnread = vm.Mail.Mailboxes.SelectMany(m => m.Folders).Where(f => f.Kind == FolderKind.Folder && f.Role == Webignite.Core.Mail.FolderRole.Inbox).Sum(f => f.Unread);
Check("Mail in the sidebar counts the same as the inboxes as soon as a mail is read", unreadMail is not null && vm[Page.Mail].Badge == inboxesUnread);
Wait(vm.Mail.Select(vm.Mail.Messages.First(m => m.Summary.Uid == 41)), "The booking mail opened");

// Snooze until a picked day: the small form above the list.
Wait(vm.Mail.SnoozePart.SnoozeMenu().Last().Run!(), "The snooze form opened");
Save("mail-snooze-pick");
vm.Mail.SnoozePart.CancelSnoozePickCommand.Execute(null);

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
    vm.Mail.RulesPart.ManageRulesCommand.Execute(null);
    vm.Mail.RulesPart.RulesPage.Text = "fnb.co.za";
    Save("mail-rules");
    vm.Mail.RulesPart.RulesPage.CloseCommand.Execute(null);
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
vm.Mail.SendingPart.UndoSendCommand.Execute(null);
vm.Mail.Composer.DiscardCommand.Execute(null);
vm.Mail.Open(vm.Mail.Mailboxes[0].Folders[0]);
vm.Mail.Status = "";
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
vm.Mail.EditorsPart.StartNewCategory([]);
vm.Mail.EditorsPart.CategoryEditor!.Name = "Later";
Save("mail-new-category");
vm.Mail.Mailboxes[1].NewFolderCommand.Execute(null);
vm.Mail.EditorsPart.FolderEditor!.Name = "Banking";
Save("mail-new-folder");
vm.Mail.EditorsPart.FolderEditor.CancelCommand.Execute(null);
vm.Mail.Open(vm.Mail.Mailboxes[0].Folders[0]);
vm.Mail.Panels.Show("folders", false);
vm.Mail.Panels.Resized("messages", 460);
Wait(vm.Mail.Select(vm.Mail.Messages.First(m => m.Summary.Uid == 41)), "The booking mail opened");
Save("mail-panels-no-folders");
vm.Mail.Panels.Move("reader", -1);
Save("mail-panels-reader-left");
vm.Mail.Panels.ResetCommand.Execute(null);
// A Teams invite in a mail: a bar on the mail offers to add it to the calendar, with a Join link.
{
    var invite = new MimeKit.MimeMessage { Subject = "Website kick-off", MessageId = "kickoff@bosveldbuilders.co.za" };
    invite.From.Add(new MimeKit.MailboxAddress("Pieter van Wyk", "pieter@bosveldbuilders.co.za"));
    invite.To.Add(new MimeKit.MailboxAddress(null, "eddie@webignite.co.za"));
    var meetingDay = today.AddDays(4);
    var ics = "BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\nSUMMARY:Website kick-off\r\n"
        + $"DTSTART:{meetingDay:yyyyMMdd}T100000\r\nDTEND:{meetingDay:yyyyMMdd}T104500\r\nLOCATION:Microsoft Teams Meeting\r\n"
        + "X-MICROSOFT-SKYPETEAMSMEETINGURL:https://teams.microsoft.com/l/meetup-join/19%3ameeting_kickoff/0\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
    var forms = new MimeKit.MultipartAlternative
    {
        new MimeKit.TextPart("html") { Text = "<p>Hi Eddie,</p><p>Let's kick the new website off on Teams. Sipho from our side will join too.</p><p>Pieter</p>" },
        new MimeKit.TextPart("calendar") { Text = ics },
    };
    invite.Body = forms;
    using var raw = new MemoryStream();
    invite.WriteTo(raw);
    store.SaveMailSummaries([new Webignite.Core.Mail.MailSummary
    {
        AccountId = "m1", Folder = "INBOX.Clients", Uid = 70, Date = DateTimeOffset.Now.AddHours(-3), MessageId = "kickoff@bosveldbuilders.co.za",
        From = new("Pieter van Wyk", "pieter@bosveldbuilders.co.za"), To = [new(null, "eddie@webignite.co.za")], Subject = "Website kick-off",
        Preview = "Hi Eddie, let's kick the new website off on Teams.", Seen = true,
    }]);
    store.SaveMailBody("m1", "INBOX.Clients", 70, raw.ToArray());
    vm.Mail.Open(vm.Mail.Mailboxes.SelectMany(m => m.Folders).First(f => f.IsRealFolder && f.Mailbox.Id == "m1" && f.Path == "INBOX.Clients"));
    Dispatcher.UIThread.RunJobs();
    Wait(vm.Mail.Select(vm.Mail.Messages.First(m => m.Summary.Uid == 70)), "The invite opened");
    var reader = vm.Mail.Reader!;
    Check("A mail with a meeting invite offers to add it, with what the invite says filled in",
        reader.HasMeeting && reader.MeetingCanBeAdded && reader.MeetingTitle == "Website kick-off" && reader.MeetingTime == "10:00"
        && reader.MeetingDay == meetingDay.ToDateTime(TimeOnly.MinValue) && reader.CanJoinMeeting);
    Save("mail-meeting");
    reader.AddMeetingCommand.Execute(null);
    var kept = store.ListEvents(meetingDay, meetingDay).SingleOrDefault(e => e.Title == "Website kick-off");
    Check("Add to calendar writes it in once, with its Join link, its end and the client",
        kept is { JoinLink: not null, Customer: "Bosveld Builders" } && kept.Start == new TimeOnly(10, 0) && kept.End == new TimeOnly(10, 45) && reader.MeetingAdded && !reader.MeetingCanBeAdded);
    Wait(vm.Mail.Select(vm.Mail.Messages.First(m => m.Summary.Uid == 70)), "The invite opened again");
    Check("Opened again, the mail says it is in the calendar already", vm.Mail.Reader!.MeetingAdded && vm.Mail.Reader.MeetingLead.StartsWith("In your calendar", StringComparison.Ordinal));
    Save("mail-meeting-added");
    vm.Mail.Reader.ShowMeetingInDiaryCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    Check("Show in the diary opens the diary at the meeting's day",
        vm.Selected.Key == Page.Diary && vm.Current.Page is DiaryViewModel at && (at.Left.Date == meetingDay || at.Right.Date == meetingDay));
    if (kept is not null)
        store.DeleteEvent(kept.Id);
    Open(Page.Mail);
}
// Mail as Outlook, Gmail and a newsletter write it, each drawn in the reading pane.
foreach (var (name, html) in MailSamples.All)
{
    var sample = new MimeKit.MimeMessage { Subject = "Sample: " + name };
    sample.From.Add(new MimeKit.MailboxAddress("Pieter van Wyk", "pieter@bosveldbuilders.co.za"));
    sample.Body = new MimeKit.TextPart("html") { Text = html };
    using var raw = new MemoryStream();
    sample.WriteTo(raw);
    store.SaveMailBody("m1", "INBOX.Clients", 70, raw.ToArray());
    Wait(vm.Mail.Select(vm.Mail.Messages.First(m => m.Summary.Uid == 70)), "A sample mail opened");
    vm.Mail.Reader!.ShowImagesCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    Save("mail-html-" + name);
}
vm.Mail.Open(vm.Mail.Mailboxes[0].Folders[0]);
Open(Page.Clients);
vm.Clients.OpenCommand.Execute(vm.Clients.Rows.First(r => r.Client.Name == "Fynbos Physio"));
OpenSection("mail");
Dispatcher.UIThread.RunJobs();
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
    tagSettings.FileTags.FileTagRows.First(r => r.Tag.Id == "needs-ok").Colours.First(c => c.Colour == "pink").PickCommand.Execute(null);
    tagSettings.FileTags.NewFileTag = "Logo files";
    tagSettings.FileTags.AddFileTagCommand.Execute(null);
    Check("A tag can be added and recoloured in Settings", services.FileTags.Tags.Any(t => t.Name == "Logo files")
        && services.FileTags.Tags.First(t => t.Id == "needs-ok").Colour == "pink");
    Save("settings-file-tags");
    Open(Page.Files);
    Check("Files shows a tag's new colour straight away", Row("Quote QU-0042.pdf").Tags.Any(t => t.Colour == "pink"));
    tagSettings.FileTags.FileTagRows.First(r => r.Tag.Id == "invoice-sent").RemoveCommand.Execute(null);
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

    // A folder moved onto another disk: /dev/shm is a disk of its own on Linux, like a second drive.
    if (Directory.Exists("/dev/shm"))
    {
        var elsewhere = Path.Combine("/dev/shm", "webignite-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(elsewhere, "Drafts"));
        File.WriteAllText(Path.Combine(elsewhere, "Drafts", "brief.txt"), "Two pages, warm colours");
        var landed = Path.Combine(data, "From another disk");
        Webignite.Desktop.Files.FileOps.MoveOne(elsewhere, landed);
        Check("A folder moves onto another disk with what's in it", !Directory.Exists(elsewhere)
            && File.ReadAllText(Path.Combine(landed, "Drafts", "brief.txt")) == "Two pages, warm colours");
    }
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
    OpenSection("files");
    Dispatcher.UIThread.RunJobs();
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
    var toAddress = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<AddressBox>().First(b => b.Name == "ToBox");
    Check("An address in To shows as a chip, as in Gmail", toAddress.Chips.Count == 1 && toAddress.Box.Text == "");
    var toBox = toAddress.Box;
    toBox.Focus();
    Dispatcher.UIThread.RunJobs();
    toBox.Text = "pie";
    var until = DateTime.UtcNow.AddSeconds(5);
    while (!vm.Mail.Composer!.HasSuggestions && DateTime.UtcNow < until)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
    Check("Typing in To suggests addresses", vm.Mail.Composer!.HasSuggestions);
    vm.Mail.Composer.AcceptSuggestion();
    Dispatcher.UIThread.RunJobs();
    Check("Picking a suggestion fills it in, as a chip of its own", vm.Mail.Composer.To.Contains("pieter", StringComparison.OrdinalIgnoreCase)
        && toAddress.Chips.Count == 2 && toAddress.Box.Text == "");
    toBox.Text = "leon@trimax.co.za, jaco@webignite";
    Dispatcher.UIThread.RunJobs();
    Check("A comma makes what was typed a chip", toAddress.Chips.Count == 3 && toAddress.Box.Text == "jaco@webignite");
    toBox.Text = "";
    toBox.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Back });
    Dispatcher.UIThread.RunJobs();
    Check("Backspace in an empty box takes the last chip back to change it", toAddress.Chips.Count == 2 && toAddress.Box.Text == "leon@trimax.co.za");
    toBox.Text = "leon@trimax";
    toBox.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
    Dispatcher.UIThread.RunJobs();
    Save("compose-chips");
    Check("Enter makes a chip, and one that isn't a full address is marked", toAddress.Chips.Count == 3
        && Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(toAddress).OfType<Avalonia.Controls.Border>().Count(b => b.Classes.Contains("addresschip") && b.Classes.Contains("bad")) == 1);

    // Send later, and every menu made as it opens, fills itself first, so it opens whole and not as a thin line.
    var later = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Button>()
        .First(b => Avalonia.Controls.ToolTip.GetTip(b) as string == "Send at a time you pick");
    var laterMenu = (FreshMenu)later.Flyout!;
    laterMenu.ShowAt(later);
    Dispatcher.UIThread.RunJobs();
    Save("compose-send-later");
    Check("Send later fills its menu before it opens, so it opens whole", laterMenu.IsOpen && laterMenu.Items.Count >= 3);
    laterMenu.Hide();
    var nothing = new FreshMenu();
    nothing.ShowAt(later);
    Check("A menu with nothing in it doesn't open as an empty line", !nothing.IsOpen);
}
// A reply an assistant drafted (a draft from elsewhere, plain text) gets the signature right after the writing.
{
    var drafted = new Webignite.Core.Mail.OutgoingMail
    {
        AccountId = invoiceBox.Id,
        To = [new(null, "accounts@karoocoffee.co.za")],
        Cc = [new(null, "leon@trimax.co.za"), new(null, "jaco@webignite.co.za")],
        Subject = "Re: WEBSITE FEEDBACK",
        Body = "Hi Derek,\n\nEk volg net graag op oor die veranderinge.\n\nBaie dankie!",
        DraftUid = 77,
    };
    vm.Mail.Composer!.DiscardCommand.Execute(null);
    services.Go.ComposeMail(drafted, null, null);
    Dispatcher.UIThread.RunJobs();
    var body = vm.Mail.Composer!.Body;
    var until2 = DateTime.UtcNow.AddSeconds(5);
    while (vm.Mail.Composer!.KnownNames is null && DateTime.UtcNow < until2)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
    Check("A chip for someone known from mail or Contacts shows just their name", vm.Mail.Composer.KnownNames?.Count > 0
        && Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<AddressBox>().First(b => b.Name == "ToBox")
            .GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Any(t => !t.Text!.Contains('@') && t.Text.Length > 0));
    Check("A drafted reply gets the signature right after the writing", body.Contains("Baie dankie!\n\nWebignite Accounts", StringComparison.Ordinal)
        && body.Split("Webignite Accounts").Length == 2);
    Save("compose-reply-signature");
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
// A draft in Xero can be taken off the list by hand (one deleted in Xero, say), after a yes; Undo puts it back.
{
    var inXero = vm.Approvals.Rows.First(r => r.CanRemove);
    var countBefore = vm.Approvals.Rows.Count;
    inXero.AskRemoveCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    Save("to-approve-remove");
    inXero.RemoveCommand.Execute(null);
    Check("Take it off takes it off To approve, and leaves it in its list", vm.Approvals.Rows.Count == countBefore - 1
        && store.Get(inXero.Item.Stored.Id) is { OffApprovals: not null });
    store.Update(inXero.Item.Stored.Id, d => d with { OffApprovals = null });
    vm.Approvals.Refresh();
    Check("Put back, it's on To approve again", vm.Approvals.Rows.Count == countBefore);
}

Open(Page.Overview);
Check("A test build hides Check for updates and says nothing about the update server", !vm.Updates.CanUpdate && vm.Updates.CheckNote.Length == 0);
Save("overview");
SaveLooks("overview");
// Overview is the person's to arrange: its own parts can be taken off, and widgets of their own put under them.
{
    var overview = (OverviewViewModel)vm.Current.Page;
    Check("Overview starts with all its own parts and no widgets, so it builds nothing extra",
        overview.ShowFigures && overview.ShowToday && overview.NoBoard && services.Settings.OverviewBoard is null);
    overview.ToggleFiguresCommand.Execute(null);
    overview.AddWidgetsCommand.Execute(null);
    overview.Board!.AddWidget(WidgetKind.Timer);
    overview.Board.AddWidget(WidgetKind.Note);
    overview.Board.AddWidget(WidgetKind.MyTasks);
    overview.Board.IsEditing = false;
    var (wasWide, wasTall) = (window.Width, window.Height);
    window.Height = 1300;
    Dispatcher.UIThread.RunJobs();
    Check("A part taken off is gone, and widgets added sit under what is left and are saved on their own, not as one of My pages",
        !overview.ShowFigures && overview.HasBoard && overview.Board.Widgets.Count == 3 && overview.Board.IsHome
        && services.Settings.OverviewBoard is { Widgets.Count: 3 } && !services.Settings.OverviewParts.Figures
        && services.Settings.MyPages.All(p => p.Id != OverviewParts.BoardId));
    Save("overview-own-widgets");
    // Back to how it came, for the screens that follow.
    foreach (var widget in overview.Board.Widgets.ToList())
        overview.Board.RemoveWidget(widget);
    overview.ToggleFiguresCommand.Execute(null);
    window.Height = wasTall;
    Dispatcher.UIThread.RunJobs();
    Check("The figures put back show again", overview.ShowFigures && services.Settings.OverviewBoard is { Widgets.Count: 0 });
}
// Not every mail needs a reply: the × waves one off, and Undo brings it back.
{
    var overview = (OverviewViewModel)vm[Page.Overview].Main.Page;
    var waiting = overview.Summary.Where(l => l.CanWaveOff).ToList();
    if (waiting.Count > 0)
    {
        var first = waiting[0];
        first.NoReplyNeededCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check("A mail waved off as needing no reply leaves Needs you", overview.Summary.Count(l => l.CanWaveOff) == waiting.Count - 1
            && overview.Summary.All(l => l.Text != first.Text));
        services.Settings.NoReplyNeeded = new Dictionary<string, DateTimeOffset>();
        overview.Refresh();
        Check("Undoing it brings it back", overview.Summary.Count(l => l.CanWaveOff) == waiting.Count);
    }
}

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
Check("A new install opens with the dock as its menu", startsWithDock);
Check("Tools are in the sidebar under Computer and Web tools, not on a page of their own",
    vm.Sidebar.Items.OfType<ToolLink>().Select(t => t.Group?.Key).SequenceEqual(["computer", "computer", "computer", "computer", "computer", "computer", "computer", "computer", "web-tools", "web-tools", "web-tools", "web-tools", "web-tools", "web-tools", "web-tools", "web-tools", "web-tools"])
    && vm.Modules.All(m => m.Title != "Tools") && vm[Page.Files].Group?.Key == "computer" && vm[Page.Notes].Group?.Key == "computer");
vm.SelectedNav = vm.Sidebar.Items.OfType<ToolLink>().First(t => t.Kind == ToolKind.Calculator);
var calculatorWindow = window.ShowTool(ToolKind.Calculator, services);
Check("Clicking a tool opens its window and leaves the page as it was", vm.Selected.Key == Page.Overview && calculatorWindow.IsVisible);
var calc = calculatorWindow.Tools;
calc.Calculator.Sum = "3.5 x 650";
calc.Calculator.WorkOutCommand.Execute(null);
calc.Calculator.Sum = "14 500 + 3 200";
calc.Calculator.WorkOutCommand.Execute(null);
calc.Calculator.Sum = "2400 - 10%";
calc.Calculator.LineDescription = "Hosting, 12 months, returning client";
SaveWindow(calculatorWindow, "tools-calculator");
var qrWindow = window.ShowTool(ToolKind.Qr, services);
qrWindow.Tools.Qr.QrInput = "karoocoffee.co.za/menu";
SaveWindow(qrWindow, "tools-qr");
qrWindow.Close();
var helpersWindow = window.ShowTool(ToolKind.WebHelpers, services);
var helpers = helpersWindow.Tools;
Check("Web helpers opens on filler text, three paragraphs starting the usual way",
    helpers.WebHelpers.IsLorem && helpers.WebHelpers.LoremText.StartsWith("Lorem ipsum dolor sit amet", StringComparison.Ordinal) && helpers.WebHelpers.LoremText.Split("\n\n").Length == 3);
SaveWindow(helpersWindow, "tools-web-lorem");
helpers.WebHelpers.ShowHelperCommand.Execute(WebHelper.Placeholder);
Check("A placeholder picture is drawn at the size asked for",
    helpers.WebHelpers.PlaceholderLabel == "1200 × 630" && helpers.WebHelpers.PlaceholderPng() is { Length: > 100 } placeholderPng
    && new Avalonia.Media.Imaging.Bitmap(new MemoryStream(placeholderPng)).PixelSize == new Avalonia.PixelSize(1200, 630));
SaveWindow(helpersWindow, "tools-web-placeholder");
helpers.WebHelpers.ShowHelperCommand.Execute(WebHelper.Units);
Check("px and rem answers as it's typed", helpers.WebHelpers.UnitSum == "24px = 1.5rem");
SaveWindow(helpersWindow, "tools-web-units");
helpers.WebHelpers.ShowHelperCommand.Execute(WebHelper.Minify);
helpers.WebHelpers.MinifyInput = "/* The header */\n.site-header {\n    color: #333;\n    margin: 0 auto;\n}\n\n.site-header a:hover {\n    text-decoration: underline;\n}\n";
Check("Minify makes a stylesheet smaller", helpers.WebHelpers.MinifyOutput == ".site-header{color:#333;margin:0 auto}.site-header a:hover{text-decoration:underline}");
SaveWindow(helpersWindow, "tools-web-minify");
helpers.WebHelpers.ShowHelperCommand.Execute(WebHelper.Json);
helpers.WebHelpers.JsonInput = "{\"name\":\"Karoo Coffee\",\"open\":true,\"tags\":[\"coffee\",\"bakery\"]}";
Check("JSON is laid out", helpers.WebHelpers.JsonOutput.StartsWith("{\n  \"name\": \"Karoo Coffee\"", StringComparison.Ordinal) && !helpers.WebHelpers.HasJsonProblem);
SaveWindow(helpersWindow, "tools-web-json");
helpers.WebHelpers.JsonInput = "{\"name\": oops}";
Check("Broken JSON says so and shows nothing to copy", helpers.WebHelpers.HasJsonProblem && !helpers.WebHelpers.HasJsonOutput);
helpers.WebHelpers.ShowHelperCommand.Execute(WebHelper.Cases);
helpers.WebHelpers.CaseInput = "Karoo Coffee: Our Menu & Prices";
Check("A heading becomes a slug", helpers.WebHelpers.Cases.First().Text == "karoo-coffee-our-menu-prices" && helpers.WebHelpers.Cases.Count == 9);
SaveWindow(helpersWindow, "tools-web-cases");
helpers.WebHelpers.ShowHelperCommand.Execute(WebHelper.Utm);
helpers.WebHelpers.UtmAddress = "karoocoffee.co.za/specials";
Check("A campaign link asks for its source", helpers.WebHelpers.HasUtmProblem && !helpers.WebHelpers.HasUtmLink);
helpers.WebHelpers.UtmSource = "newsletter";
helpers.WebHelpers.UtmMedium = "email";
helpers.WebHelpers.UtmCampaign = "spring-sale";
Check("A campaign link is built as it's typed",
    helpers.WebHelpers.UtmLink == "https://karoocoffee.co.za/specials?utm_source=newsletter&utm_medium=email&utm_campaign=spring-sale");
SaveWindow(helpersWindow, "tools-web-utm");
helpersWindow.Close();
var dnsWindow = window.ShowTool(ToolKind.Dns, services);
var dns = dnsWindow.Tools;
dns.Dns.LookUpDnsCommand.Execute(null);
Check("A lookup with nothing typed asks for a domain", dns.Dns.DnsProblem && dns.Dns.DnsNote.StartsWith("Type a domain", StringComparison.Ordinal));
dns.Dns.DnsName = "karoocoffee.co.za";
dns.Dns.DnsTypeChoice = Webignite.Core.Web.DnsType.MX;
Wait(dns.Dns.LookUpDnsCommand.ExecuteAsync(null), "The lookup finished");
Check("With no internet a lookup says so and the tool carries on", dns.Dns.DnsProblem && dns.Dns.DnsNote.StartsWith("Couldn't look that up", StringComparison.Ordinal) && !dns.Dns.DnsBusy);
dns.Dns.ShowDnsAnswer("karoocoffee.co.za", new Webignite.Core.Web.DnsAnswer(
    [new("karoocoffee.co.za", Webignite.Core.Web.DnsType.MX, 7200, "10 mail.karoocoffee.co.za"), new("karoocoffee.co.za", Webignite.Core.Web.DnsType.MX, 7200, "20 mail2.karoocoffee.co.za")], 0, false));
Check("A lookup lists its records with how long each is kept", dns.Dns.DnsRows.Count == 2 && dns.Dns.DnsRows[0].Kept == "kept 2 hours" && dns.Dns.DnsNote.Length == 0);
SaveWindow(dnsWindow, "tools-dns-lookup");
dns.Dns.ShowDnsAnswer("nothere.karoocoffee.co.za", new Webignite.Core.Web.DnsAnswer([], 3, false));
Check("A name that doesn't exist says so", dns.Dns.DnsProblem && !dns.Dns.HasDnsRows && dns.Dns.DnsNote.Contains("doesn't exist", StringComparison.Ordinal));
dns.Dns.ShowDnsPartCommand.Execute(DnsPart.Spread);
dns.Dns.DnsName = "www.karoocoffee.co.za";
dns.Dns.DnsTypeChoice = Webignite.Core.Web.DnsType.A;
dns.Dns.ShowSpread(
[
    new("ns1.host-h.net", true, ["41.203.18.99"]),
    new("ns2.dns-h.com", true, ["41.203.18.99"]),
    new("Cloudflare (1.1.1.1)", false, ["41.203.18.99"]),
    new("Google (8.8.8.8)", false, ["41.203.18.10"]),
    new("Quad9 (9.9.9.9)", false, ["41.203.18.99"]),
    new("OpenDNS", false, ["41.203.18.10"]),
    new("AdGuard", false, [], "No answer."),
], 3600);
Check("The spread check says who is behind and how long it can take",
    dns.Dns.SpreadVerdict.StartsWith("Still spreading: 2 of 4 resolvers", StringComparison.Ordinal) && dns.Dns.SpreadVerdict.Contains("1 hour", StringComparison.Ordinal)
    && dns.Dns.SpreadRows.Count(r => r.Behind) == 2 && dns.Dns.SpreadRows.Count(r => r.State == "Up to date") == 2);
SaveWindow(dnsWindow, "tools-dns-spread");
dns.Dns.ShowDnsPartCommand.Execute(DnsPart.Mail);
dns.Dns.MailDomain = "karoocoffee.co.za";
dns.Dns.ShowMailNow(Webignite.Core.Web.MailRecords.Now(["v=spf1 include:spf.host-h.net ~all"], [], hasMail: true));
dns.Dns.SpfSenders.First(s => s.Name == "xneelo").IsOn = true;
dns.Dns.SpfSenders.First(s => s.Name == "Mailchimp").IsOn = true;
dns.Dns.SpfWebServer = true;
dns.Dns.DmarcReports = "dmarc@karoocoffee.co.za";
dns.Dns.DkimSelector = "default";
Check("The SPF record follows the ticks", dns.Dns.SpfRecord.Value == "v=spf1 mx a include:spf.host-h.net include:servers.mcsv.net ~all");
Check("The DMARC record starts by watching, with where reports go", dns.Dns.DmarcRecord.Value == "v=DMARC1; p=none; rua=mailto:dmarc@karoocoffee.co.za");
Check("What the domain has now is shown, with what's missing marked", dns.Dns.MailNow.Count == 2 && dns.Dns.MailNow[1].Problem && dns.Dns.DkimHost == "default._domainkey");
SaveWindow(dnsWindow, "tools-dns-mail");
dnsWindow.Close();
// Page to PDF or picture, first on a PC with no browser to draw with, then with the stand-in.
var noBrowserWindow = window.ShowTool(ToolKind.PageCapture, services);
Check("Without a browser the page tool says why and offers nothing to save",
    noBrowserWindow.Tools.PageCapture.CaptureWithout.Contains("WebView2", StringComparison.Ordinal) && !noBrowserWindow.Tools.PageCapture.HasCaptureView
    && !noBrowserWindow.Tools.PageCapture.SavePagePdfCommand.CanExecute(null));
SaveWindow(noBrowserWindow, "tools-page-capture-none");
noBrowserWindow.Close();
browserThere = true;
var captureWindow = window.ShowTool(ToolKind.PageCapture, services);
var capture = captureWindow.Tools;
Check("The browser starts when the page tool's window opens, and not before", Until(() => standInBrowser is { Started: true } && capture.PageCapture.HasCaptureView));
Check("Nothing can be saved before a page is open", !capture.PageCapture.SavePagePdfCommand.CanExecute(null) && !capture.PageCapture.SavePagePicturesCommand.CanExecute(null));
capture.PageCapture.CaptureAddress = "karoo coffee";
capture.PageCapture.OpenCapturePageCommand.Execute(null);
Check("Something that isn't an address is said to be so", capture.PageCapture.CaptureProblem && capture.PageCapture.CaptureNote.StartsWith("That isn't a web address", StringComparison.Ordinal));
capture.PageCapture.CaptureAddress = "nothere.karoocoffee.co.za";
capture.PageCapture.OpenCapturePageCommand.Execute(null);
Check("A page that doesn't open says why and can't be saved",
    capture.PageCapture.CaptureProblem && capture.PageCapture.CaptureNote.StartsWith("There is no site at that address", StringComparison.Ordinal) && !capture.PageCapture.CaptureReady);
capture.PageCapture.CaptureAddress = "karoocoffee.co.za/menu";
capture.PageCapture.OpenCapturePageCommand.Execute(null);
Check("An open page shows its title and full address, and can be saved",
    capture.PageCapture.CaptureNote == "Menu | Karoo Coffee" && capture.PageCapture.CaptureAddress == "https://karoocoffee.co.za/menu" && !capture.PageCapture.CaptureProblem
    && capture.PageCapture.SavePagePdfCommand.CanExecute(null) && capture.PageCapture.SavePagePicturesCommand.CanExecute(null));
SaveWindow(captureWindow, "tools-page-capture");
{
    var saved = Path.Combine(data, "page-capture");
    Directory.CreateDirectory(saved);
    Wait(capture.PageCapture.SavePagePicturesToAsync(saved), "The pictures were taken");
    Check("A picture is saved for each ticked screen, named after the page",
        File.Exists(Path.Combine(saved, "karoocoffee-co-za-menu-phone-390.png")) && File.Exists(Path.Combine(saved, "karoocoffee-co-za-menu-desktop-1920.png"))
        && Directory.GetFiles(saved).Length == 2 && capture.PageCapture.CaptureNote == "2 pictures saved." && capture.PageCapture.HasCaptured && capture.PageCapture.CaptureReady);
    Check("Each picture was taken at its own width, the whole page, and the browser put back after",
        standInBrowser!.Calls.Count(c => c == "Page.captureScreenshot") == 2 && standInBrowser.Calls[^1] == "Emulation.clearDeviceMetricsOverride"
        && standInBrowser.Asked.Any(a => a.Contains("\"width\":390", StringComparison.Ordinal) && a.Contains("\"height\":2400", StringComparison.Ordinal)));
    Wait(capture.PageCapture.SavePagePicturesToAsync(saved), "The pictures were taken again");
    Check("Saving again keeps the first pictures", Directory.GetFiles(saved).Length == 4 && File.Exists(Path.Combine(saved, "karoocoffee-co-za-menu-phone-390 (2).png")));
    capture.PageCapture.PdfSideways = true;
    Wait(capture.PageCapture.SavePagePdfToAsync(Path.Combine(saved, "menu.pdf")), "The PDF was made");
    Check("The page is saved as a PDF with the paper chosen",
        File.Exists(Path.Combine(saved, "menu.pdf")) && capture.PageCapture.CaptureNote == "Saved as menu.pdf." && standInBrowser.Pdf is { Paper: Webignite.Core.Web.PaperSize.A4, Sideways: true, Backgrounds: true });
    SaveWindow(captureWindow, "tools-page-capture-saved");
    standInBrowser.FailNext = true;
    Wait(capture.PageCapture.SavePagePdfToAsync(Path.Combine(saved, "again.pdf")), "The PDF was tried");
    Check("When the browser can't, the tool says so and carries on", capture.PageCapture.CaptureProblem && capture.PageCapture.CaptureNote.StartsWith("That didn't work", StringComparison.Ordinal) && capture.PageCapture.CaptureReady);
}
// The same page shown as a phone, a tablet too big for the window, a typed size, and back.
{
    var saved = Path.Combine(data, "page-capture-devices");
    Directory.CreateDirectory(saved);
    standInBrowser!.Calls.Clear();
    capture.PageCapture.CaptureDevice = capture.PageCapture.CaptureDevices.First(d => d.Label == "iPhone 14");
    Check("Shown as an iPhone the page is its size, says it is one, is touched, and is fetched again",
        Until(() => standInBrowser.Calls.Contains("Page.reload")) && capture.PageCapture.DeviceSize.StartsWith("390 × 844", StringComparison.Ordinal)
        && standInBrowser.Asked.Any(a => a.Contains("iPhone", StringComparison.Ordinal)) && standInBrowser.Calls.Contains("Emulation.setTouchEmulationEnabled"));
    Check("The iPhone's screen is drawn in the middle at its own width", capture.PageCapture.IsDeviceOn && capture.PageCapture.PaneWidth <= 390 && capture.PageCapture.PaneWidth / capture.PageCapture.PaneHeight is > 0.45 and < 0.47);
    SaveWindow(captureWindow, "tools-page-capture-phone");
    Wait(capture.PageCapture.SavePagePicturesToAsync(saved), "The phone picture was taken");
    Check("A picture taken as a device is of that device, laid out as a phone, and named after it",
        File.Exists(Path.Combine(saved, "karoocoffee-co-za-menu-iphone-14-390.png")) && Directory.GetFiles(saved).Length == 1
        && standInBrowser.Asked.Any(a => a.Contains("\"width\":390", StringComparison.Ordinal) && a.Contains("\"mobile\":true", StringComparison.Ordinal) && a.Contains("\"deviceScaleFactor\":1", StringComparison.Ordinal)));
    Check("After the picture the browser is put back to showing the iPhone",
        Until(() => standInBrowser.Calls.LastIndexOf("Emulation.setEmitTouchEventsForMouse") > standInBrowser.Calls.LastIndexOf("Page.captureScreenshot")));
    standInBrowser.Calls.Clear();
    capture.PageCapture.CaptureSideways = true;
    Check("Turned sideways it is wider than tall, without fetching the page again",
        Until(() => standInBrowser.Calls.Contains("Emulation.setDeviceMetricsOverride")) && capture.PageCapture.PaneWidth > capture.PageCapture.PaneHeight && !standInBrowser.Calls.Contains("Page.reload"));
    capture.PageCapture.CaptureSideways = false;
    capture.PageCapture.CaptureDevice = capture.PageCapture.CaptureDevices.First(d => d.Label == "iPad Pro 13 inch");
    Check("A screen taller than the window is shrunk to fit and says by how much",
        capture.PageCapture.PaneHeight < 1366 && capture.PageCapture.DeviceSize.StartsWith("1024 × 1366, shown at ", StringComparison.Ordinal));
    SaveWindow(captureWindow, "tools-page-capture-tablet");
    capture.PageCapture.CaptureDevice = capture.PageCapture.CaptureDevices[^1];
    capture.PageCapture.CustomWidth = "wide";
    Check("A typed size that isn't a number is asked for again, and the page fills the window meanwhile",
        capture.PageCapture.IsCustomDevice && !capture.PageCapture.IsDeviceOn && double.IsNaN(capture.PageCapture.PaneWidth) && capture.PageCapture.DeviceSize.StartsWith("Type a width and height", StringComparison.Ordinal));
    capture.PageCapture.CustomWidth = "500";
    capture.PageCapture.CustomHeight = "400";
    Check("A typed size is used as it is", capture.PageCapture.IsDeviceOn && capture.PageCapture.PaneWidth == 500 && capture.PageCapture.PaneHeight == 400 && capture.PageCapture.DeviceSize == "500 × 400");
    standInBrowser.Calls.Clear();
    capture.PageCapture.CaptureDevice = capture.PageCapture.CaptureDevices[0];
    Check("Back to Fit the window the browser is itself again",
        Until(() => standInBrowser.Calls.Contains("Emulation.clearDeviceMetricsOverride") && standInBrowser.Calls.Contains("Page.reload")) && !capture.PageCapture.IsDeviceOn && double.IsNaN(capture.PageCapture.PaneWidth));
}
captureWindow.Close();
Check("Closing the page tool's window shuts its browser down", standInBrowser!.Disposed && !capture.PageCapture.HasCaptureView);
// The Browser tool: a site to click through, at the window's size or a device's, one click each.
{
    var browserWindow = window.ShowTool(ToolKind.Browser, services);
    var browsing = browserWindow.Tools.Browser;
    Check("The Browser tool starts its own browser when its window opens", Until(() => standInBrowser is { Started: true, Disposed: false } && browsing.HasCaptureView));
    var engine = standInBrowser!;
    browsing.CaptureAddress = "karoocoffee.co.za/menu";
    browsing.BrowseCommand.Execute(null);
    Check("An address typed in the Browser opens", browsing.CaptureAddress == "https://karoocoffee.co.za/menu" && browsing.CaptureNote == "Menu | Karoo Coffee");
    browsing.CaptureAddress = "karoo coffee menu";
    browsing.BrowseCommand.Execute(null);
    Check("Words typed in the Browser are searched for", browsing.CaptureAddress.StartsWith("https://www.google.com/search?q=karoo%20coffee%20menu", StringComparison.Ordinal) && !browsing.CaptureProblem);
    engine.Calls.Clear();
    browsing.QuickSizes.First(q => q.Label == "Phone").PickCommand.Execute(null);
    Check("One click shows the page as a phone, and the button says which size is on",
        Until(() => engine.Calls.Contains("Emulation.setDeviceMetricsOverride")) && browsing.IsDeviceOn && browsing.DeviceSize.StartsWith("390 × 844", StringComparison.Ordinal)
        && browsing.QuickSizes.Single(q => q.IsOn).Label == "Phone");
    SaveWindow(browserWindow, "tools-browser-phone");
    browsing.QuickSizes.First(q => q.Label == "Tablet").PickCommand.Execute(null);
    SaveWindow(browserWindow, "tools-browser-tablet");
    browsing.QuickSizes.First(q => q.Label == "Fit window").PickCommand.Execute(null);
    Check("Fit window fills the window again", !browsing.IsDeviceOn && double.IsNaN(browsing.PaneWidth) && browsing.QuickSizes.Single(q => q.IsOn).Label == "Fit window");
    engine.Calls.Clear();
    Wait(browsing.BackCommand.ExecuteAsync(null), "Back was asked for");
    Wait(browsing.ReloadCommand.ExecuteAsync(null), "Reload was asked for");
    Check("Back asks the browser where it has been, and reload loads the page again", engine.Calls.Contains("Page.getNavigationHistory") && engine.Calls.Contains("Page.reload"));
    SaveWindow(browserWindow, "tools-browser");
    browserWindow.Close();
    Check("Closing the Browser's window shuts its browser down", engine.Disposed && !browsing.HasCaptureView);
}
// Text snippets: none yet, one saved, found by its code and copied with today's date filled in.
var snippetWindow = window.ShowTool(ToolKind.Snippets, services);
var snippets = snippetWindow.Tools;
Check("With no snippets yet the window says what they are for", snippets.Snippets.NoSnippets && snippets.Snippets.SnippetRows.Count == 0);
snippets.Snippets.NewSnippetCommand.Execute(null);
snippets.Snippets.SaveSnippetCommand.Execute(null);
Check("A snippet without a name isn't saved", snippets.Snippets.SnippetProblem && snippets.Snippets.IsEditingSnippet);
snippets.Snippets.SnippetName = "Banking details";
snippets.Snippets.SnippetCode = "bank";
snippets.Snippets.SnippetText = "Webignite (Pty) Ltd\nFNB cheque account 62000000000\nBranch 250655\nReference: your invoice number";
SaveWindow(snippetWindow, "tools-snippets-new");
snippets.Snippets.SaveSnippetCommand.Execute(null);
snippets.Snippets.NewSnippetCommand.Execute(null);
snippets.Snippets.SnippetName = "Thanks for the payment";
snippets.Snippets.SnippetText = "Hi\n\nThanks, your payment came through on {date}.\n\nRegards\nEddie";
snippets.Snippets.SaveSnippetCommand.Execute(null);
snippets.Snippets.NewSnippetCommand.Execute(null);
snippets.Snippets.SnippetName = "Hosting renewal";
snippets.Snippets.SnippetCode = "bank";
snippets.Snippets.SnippetText = "Your hosting renews next month.";
snippets.Snippets.SaveSnippetCommand.Execute(null);
Check("Two snippets can't share a code", snippets.Snippets.SnippetProblem && snippets.Snippets.SnippetMessage.Contains("already", StringComparison.Ordinal));
snippets.Snippets.SnippetCode = "host";
snippets.Snippets.SaveSnippetCommand.Execute(null);
Check("Saved snippets are listed and kept in settings", snippets.Snippets.SnippetRows.Count == 3 && services.Settings.Snippets.Items.Count == 3 && !snippets.Snippets.IsEditingSnippet);
SaveWindow(snippetWindow, "tools-snippets");
snippets.Snippets.SnippetSearch = "bank";
Check("Typing a code finds its snippet first", snippets.Snippets.SnippetRows.Count == 1 && snippets.Snippets.SnippetRows[0].Name == "Banking details");
snippets.Snippets.SnippetSearch = "thanks";
string? sent = null;
snippets.Snippets.SendSnippet = text => { sent = text; return Task.FromResult("Copied. Paste it where you need it."); };
Wait(snippets.Snippets.UseFirstSnippetCommand.ExecuteAsync(null), "The snippet was used");
Check("Enter uses the best match, with today's date filled in",
    sent is not null && sent.Contains(DateTime.Now.ToString("d MMMM yyyy"), StringComparison.Ordinal) && !sent.Contains("{date}", StringComparison.Ordinal) && snippets.Snippets.SnippetMessage.StartsWith("Copied", StringComparison.Ordinal));
snippets.Snippets.SnippetSearch = "";
snippets.Snippets.SnippetRows[2].ChangeCommand.Execute(null);
snippets.Snippets.RemoveSnippetCommand.Execute(null);
Check("A snippet can be removed", snippets.Snippets.SnippetRows.Count == 2 && services.Settings.Snippets.Items.Count == 2);
snippetWindow.Close();
// Compare: two versions of a stylesheet side by side, then two folders, then one file out of them.
{
    var compareRoot = Path.Combine(data, "compare");
    Directory.CreateDirectory(Path.Combine(compareRoot, "live", "css"));
    Directory.CreateDirectory(Path.Combine(compareRoot, "staging", "css"));
    var css = string.Join("\n", Enumerable.Range(1, 40).Select(i => i == 12 ? "h1 { color: #3d2b1f; font-size: 2.4rem; }" : i == 30 ? ".button { padding: 12px 24px; }" : $".row-{i} {{ margin: {i}px; }}"));
    File.WriteAllText(Path.Combine(compareRoot, "live", "css", "site.css"), css);
    File.WriteAllText(Path.Combine(compareRoot, "staging", "css", "site.css"),
        css.Replace("font-size: 2.4rem", "font-size: 3rem", StringComparison.Ordinal).Replace(".button { padding: 12px 24px; }", ".button { padding: 14px 28px; }\n.button:hover { opacity: .9; }", StringComparison.Ordinal));
    File.WriteAllText(Path.Combine(compareRoot, "live", "index.html"), "<h1>Karoo Coffee</h1>");
    File.WriteAllText(Path.Combine(compareRoot, "staging", "index.html"), "<h1>Karoo Coffee</h1>");
    File.WriteAllText(Path.Combine(compareRoot, "live", "old-menu.html"), "<h1>Menu</h1>");
    File.WriteAllText(Path.Combine(compareRoot, "staging", "specials.html"), "<h1>Specials</h1>");
    var compareWindow = window.ShowTool(ToolKind.Compare, services);
    var compare = compareWindow.Tools;
    Check("Compare waits for both sides", !compare.Compare.RunCompareCommand.CanExecute(null));
    compare.Compare.StartCompare(Path.Combine(compareRoot, "live", "css", "site.css"), Path.Combine(compareRoot, "staging", "css", "site.css"));
    Check("Two files are shown side by side with what differs counted",
        Until(() => !compare.Compare.CompareBusy && compare.Compare.HasCompareRows) && compare.Compare.CompareSummary == "3 lines differ: 2 changed, 1 only on the right." && compare.Compare.CompareRows.Count == 41);
    SaveWindow(compareWindow, "tools-compare-files");
    compare.Compare.CompareOnlyChanges = true;
    Check("Only what differs folds the matching lines away", compare.Compare.CompareRows.Count < 41 && compare.Compare.CompareRows.Any(r => r.IsSkipped));
    SaveWindow(compareWindow, "tools-compare-changes");
    compare.Compare.CompareOnlyChanges = false;
    compare.Compare.StartCompare(Path.Combine(compareRoot, "live"), Path.Combine(compareRoot, "staging"));
    Check("Two folders are compared file by file, what differs first",
        Until(() => !compare.Compare.CompareBusy && compare.Compare.HasCompareFolderRows) && compare.Compare.CompareSummary == "3 of 4 files differ: 1 different, 1 only on the left, 1 only on the right."
        && compare.Compare.CompareFolderRows.Count == 4 && !compare.Compare.CompareFolderRows[0].Words.StartsWith("The same", StringComparison.Ordinal) && !compare.Compare.HasCompareRows);
    SaveWindow(compareWindow, "tools-compare-folders");
    compare.Compare.CompareFolderRows.First(r => r.CanCompare).CompareCommand.Execute(null);
    Check("A file that differs opens side by side, with a way back to the folders",
        Until(() => !compare.Compare.CompareBusy && compare.Compare.HasCompareRows) && compare.Compare.CanGoBackToFolders && !compare.Compare.HasCompareFolderRows);
    compare.Compare.BackToFoldersCommand.Execute(null);
    Check("Back shows the folders again", Until(() => !compare.Compare.CompareBusy && compare.Compare.HasCompareFolderRows) && !compare.Compare.CanGoBackToFolders);
    compare.Compare.StartCompare(Path.Combine(compareRoot, "live"), Path.Combine(compareRoot, "staging", "index.html"));
    Check("A file against a folder is turned down in plain words", compare.Compare.CompareProblem && compare.Compare.CompareSummary.StartsWith("One is a file", StringComparison.Ordinal));
    compareWindow.Close();

    // Duplicate files: copies found by what is in them, ticked, and sent to the bin with one of each kept.
    var copies = Path.Combine(data, "copies");
    Directory.CreateDirectory(Path.Combine(copies, "Copy of logos"));
    Directory.CreateDirectory(Path.Combine(copies, "old"));
    File.WriteAllText(Path.Combine(copies, "logo.svg"), new string('L', 4000));
    File.WriteAllText(Path.Combine(copies, "Copy of logos", "logo.svg"), new string('L', 4000));
    File.WriteAllText(Path.Combine(copies, "old", "logo final.svg"), new string('L', 4000));
    File.WriteAllText(Path.Combine(copies, "brief.txt"), "The brief");
    File.WriteAllText(Path.Combine(copies, "old", "brief (1).txt"), "The brief");
    File.WriteAllText(Path.Combine(copies, "notes.txt"), "Only one of these");
    var duplicatesWindow = window.ShowTool(ToolKind.Duplicates, services);
    var duplicates = duplicatesWindow.Tools;
    duplicates.Duplicates.StartDuplicates(copies);
    Check("Copies are found by what is in them, the set that frees the most first",
        Until(() => !duplicates.Duplicates.DuplicatesBusy && duplicates.Duplicates.HasDuplicateSets) && duplicates.Duplicates.DuplicateSets.Count == 2 && duplicates.Duplicates.DuplicateSets[0].Files.Count == 3
        && duplicates.Duplicates.DuplicateSummary.StartsWith("3 files are copies of others, in 2 sets.", StringComparison.Ordinal) && !duplicates.Duplicates.RecycleDuplicatesCommand.CanExecute(null));
    duplicates.Duplicates.TickExtraCopiesCommand.Execute(null);
    Check("Tick the extra copies leaves the one nearest the top of the folder", duplicates.Duplicates.DuplicatesTicked == 3
        && duplicates.Duplicates.DuplicateSets[0].Files.Single(f => !f.IsTicked).Path == Path.Combine(copies, "logo.svg") && duplicates.Duplicates.RecycleDuplicatesText == "Move 3 files to the Recycle Bin");
    SaveWindow(duplicatesWindow, "tools-duplicates");
    foreach (var file in duplicates.Duplicates.DuplicateSets[1].Files)
        file.IsTicked = true;
    Wait(duplicates.Duplicates.RecycleDuplicatesCommand.ExecuteAsync(null), "The copies were moved to the bin");
    Check("The ticked copies go, and one of each stays even when every copy was ticked",
        File.Exists(Path.Combine(copies, "logo.svg")) && !File.Exists(Path.Combine(copies, "Copy of logos", "logo.svg")) && !File.Exists(Path.Combine(copies, "old", "logo final.svg"))
        && new[] { Path.Combine(copies, "brief.txt"), Path.Combine(copies, "old", "brief (1).txt") }.Count(File.Exists) == 1 && File.Exists(Path.Combine(copies, "notes.txt"))
        && !duplicates.Duplicates.HasDuplicateSets && duplicates.Duplicates.DuplicateSummary.StartsWith("3 files are in the Recycle Bin; one copy of each was kept.", StringComparison.Ordinal));
    duplicatesWindow.Close();
}
var screenTextWindow = window.ShowTool(ToolKind.ScreenText, services);
Check("Text from the screen names its shortcut and shows nothing until something is read",
    !screenTextWindow.Tools.ScreenText.HasScreenText && screenTextWindow.Tools.ScreenText.ScreenTextHint.StartsWith("Ctrl+Alt+X", StringComparison.Ordinal));
screenTextWindow.Tools.ScreenText.ScreenTextRead = "Karoo Coffee Roasters" + Environment.NewLine + "Small-batch coffee from the Karoo" + Environment.NewLine + "Shop now";
Check("What was read is shown to check and copy from", screenTextWindow.Tools.ScreenText.HasScreenText);
SaveWindow(screenTextWindow, "tools-screen-text");
screenTextWindow.Close();
var rulerWindow = window.ShowTool(ToolKind.Ruler, services);
rulerWindow.Tools.Ruler.AddMeasurement("480 × 270 px (320 × 180 at 100%)");
rulerWindow.Tools.Ruler.AddMeasurement("24 × 1080 px");
Check("The ruler's window lists what was measured, newest first, and names its shortcut",
    rulerWindow.Tools.Ruler.Measurements[0] == "24 × 1080 px" && rulerWindow.Tools.Ruler.HasMeasurements && rulerWindow.Tools.Ruler.RulerHint.StartsWith("Ctrl+Alt+M", StringComparison.Ordinal));
SaveWindow(rulerWindow, "tools-ruler");
rulerWindow.Close();
var passwordWindow = window.ShowTool(ToolKind.Passwords, services);
SaveWindow(passwordWindow, "tools-passwords");
passwordWindow.Close();
services.RememberColour("#1C1D21");
services.RememberColour("#2F9E44");
var colourWindow = window.ShowTool(ToolKind.Colours, services);
Check("The Colours tool shows the recent colours", colourWindow.Tools.Colours.RecentColours.Select(c => c.Hex).Take(2).SequenceEqual(["#2F9E44", "#1C1D21"]));
SaveWindow(colourWindow, "tools-colours");
{
    var colours = colourWindow.Tools;
    colours.Colours.ColourInput = "#E8590C";
    Check("A colour shows its print code, its ten steps from light to dark, and what goes with it",
        colours.Colours.ColourCmyk == "cmyk(0%, 62%, 95%, 9%)" && colours.Colours.Shades.Count == 10 && colours.Colours.Shades[5].Hex == "#E8590C" && colours.Colours.Shades[5].Tip == "500 #E8590C" && colours.Colours.GoesWith.Count == 5);
    SaveWindow(colourWindow, "tools-colours-scale");
    colours.Colours.ClearPaletteCommand.Execute(null);
    colours.Colours.AddToPaletteCommand.Execute(null);
    colours.Colours.AddToPaletteCommand.Execute(null);
    Check("The colour showing is added to the palette once", services.Settings.Palette.Swatches.Count == 1 && colours.Colours.ColourMessage.Contains("already", StringComparison.Ordinal));
    // A logo as an SVG, and one as a picture: the colours written in the first, the most used in the second.
    var logos = Path.Combine(data, "logos");
    Directory.CreateDirectory(logos);
    File.WriteAllText(Path.Combine(logos, "karoo.svg"), """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64" fill="#3D2B1F"/><circle cx="32" cy="32" r="20" fill="#E8590C"/><path d="M20 32h24" stroke="#F8F5F0" stroke-width="4"/></svg>""");
    using (var drawn = new SkiaSharp.SKBitmap(120, 80))
    {
        using (var canvas = new SkiaSharp.SKCanvas(drawn))
        {
            canvas.Clear(new SkiaSharp.SKColor(0x1B, 0x6C, 0xA8));
            using var paint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0xF2, 0xC1, 0x4E) };
            canvas.DrawRect(0, 0, 40, 80, paint);
        }
        using var png = drawn.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(logos, "sign.png"), png.ToArray());
    }
    colours.Colours.ShowColourPartCommand.Execute(ColourPart.Palette);
    colours.Colours.AddToPalette(ColoursToolViewModel.PictureColours(Path.Combine(logos, "karoo.svg")), "karoo.svg");
    Check("An SVG logo gives the colours written in it, less the one already there",
        colours.Colours.PaletteRows.Select(r => r.Hex).SequenceEqual(["#E8590C", "#3D2B1F", "#F8F5F0"]) && colours.Colours.PaletteMessage.StartsWith("2 colours added from karoo.svg", StringComparison.Ordinal));
    colours.Colours.AddToPalette(ColoursToolViewModel.PictureColours(Path.Combine(logos, "sign.png")), "sign.png");
    Check("A picture gives its main colours, the most used first", colours.Colours.PaletteRows.Count == 5 && colours.Colours.PaletteRows[3].Hex == "#1B6CA8" && colours.Colours.PaletteRows[4].Hex == "#F2C14E");
    colours.Colours.PaletteRows[0].Name = "Primary";
    colours.Colours.PaletteRows[1].Name = "Ink";
    colours.Colours.PaletteRows[2].Name = "Paper";
    Check("Names are kept as they are typed", services.Settings.Palette.Swatches.Select(s => s.Name).Take(3).SequenceEqual(["Primary", "Ink", "Paper"]));
    Check("The palette is written out as CSS variables",
        Webignite.Core.Tools.ColourPalettes.CssVariables(services.Settings.Palette.Swatches).StartsWith(":root {\n  --primary: #E8590C;\n  --ink: #3D2B1F;\n  --paper: #F8F5F0;\n  --colour-4: #1B6CA8;", StringComparison.Ordinal));
    SaveWindow(colourWindow, "tools-colours-palette");
    colours.Colours.PaletteRows[4].RemoveCommand.Execute(null);
    Check("A colour can be taken out", colours.Colours.PaletteRows.Count == 4 && services.Settings.Palette.Swatches.Count == 4);
    colours.Colours.ShowColourPartCommand.Execute(ColourPart.Contrast);
    colours.Colours.ContrastText = "#FFFFFF";
    colours.Colours.ContrastBack = "#E8590C";
    Check("White on the brand orange passes for headings and fails for body text",
        colours.Colours.ContrastRatio.StartsWith("3.", StringComparison.Ordinal) && colours.Colours.ContrastRatio.EndsWith(" to 1", StringComparison.Ordinal) && colours.Colours.ContrastVerdicts.Select(v => v.Passes).SequenceEqual([false, true, true, false]));
    SaveWindow(colourWindow, "tools-colours-contrast");
    colours.Colours.ContrastBack = "orange-ish";
    Check("Something that isn't a colour is asked for again", !colours.Colours.HasContrast && colours.Colours.ContrastVerdicts.Count == 0 && colours.Colours.ContrastRatio.StartsWith("Type two colours", StringComparison.Ordinal));
    colours.Colours.ShowColourPartCommand.Execute(ColourPart.Colour);
}
colourWindow.Close();
// Logo pack and favicons, from the SVG logo made above.
{
    var logoWindow = window.ShowTool(ToolKind.Logos, services);
    var logos = logoWindow.Tools;
    Check("Nothing can be made before a logo is chosen", !logos.Logos.HasLogo && !logos.Logos.SaveLogoPackToCommand.CanExecute(null));
    Wait(logos.Logos.OpenLogoAsync(Path.Combine(data, "copies", "notes.txt")), "A file that isn't a logo was tried");
    Check("A file that isn't a picture is turned down in plain words", logos.Logos.LogoProblem && !logos.Logos.HasLogo);
    Wait(logos.Logos.OpenLogoAsync(Path.Combine(data, "logos", "karoo.svg")), "A logo with its own background was opened");
    Check("An SVG with its own background is only offered in colour, and says why",
        logos.Logos.LogoPreviews.Count == 1 && logos.Logos.LogoWarning.StartsWith("It has no see-through background", StringComparison.Ordinal));
    File.WriteAllText(Path.Combine(data, "logos", "karoo-mark.svg"), """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><circle cx="32" cy="32" r="28" fill="#E8590C"/><path d="M18 32h28" stroke="#3D2B1F" stroke-width="6"/></svg>""");
    logos.Logos.LogoName = "";
    Wait(logos.Logos.OpenLogoAsync(Path.Combine(data, "logos", "karoo-mark.svg")), "The logo was opened");
    Check("An SVG logo is shown in colour, all white and all black, with a name from its file",
        logos.Logos.HasLogo && logos.Logos.LogoPreviews.Count == 3 && logos.Logos.LogoName == "Karoo mark" && logos.Logos.LogoInfo.StartsWith("An SVG", StringComparison.Ordinal) && logos.Logos.LogoWarning.Length == 0);
    logos.Logos.LogoName = "Karoo Coffee";
    var made = Path.Combine(data, "logo-made");
    Directory.CreateDirectory(made);
    var zipPath = Path.Combine(made, Webignite.Core.Tools.LogoPack.ZipName(logos.Logos.LogoName));
    Wait(logos.Logos.SaveLogoPackToCommand.ExecuteAsync(zipPath), "The logo pack was made");
    using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
        Check("The logo pack is one zip with every version, the SVG and the note",
            zip.Entries.Count == 30 && zip.Entries.Any(e => e.FullName == "Vector/karoo-coffee.svg") && zip.Entries.Any(e => e.FullName == "White/karoo-coffee-white-1000px.png")
            && logos.Logos.LogoMessage.StartsWith("28 logo files", StringComparison.Ordinal) && logos.Logos.HasLogoMade);
    logos.Logos.FaviconFill = "brown";
    Wait(logos.Logos.SaveFaviconsToCommand.ExecuteAsync(Path.Combine(made, "favicons")), "The favicons were tried with a colour that isn't one");
    Check("A colour that isn't a code is asked for again", logos.Logos.LogoProblem && !Directory.Exists(Path.Combine(made, "favicons")));
    logos.Logos.FaviconFill = "#3D2B1F";
    logos.Logos.FaviconTheme = "#E8590C";
    Wait(logos.Logos.SaveFaviconsToCommand.ExecuteAsync(Path.Combine(made, "favicons")), "The favicons were made");
    Check("The favicon set is saved with the lines to paste",
        File.Exists(Path.Combine(made, "favicons", "favicon.ico")) && File.Exists(Path.Combine(made, "favicons", "apple-touch-icon.png")) && File.Exists(Path.Combine(made, "favicons", "site.webmanifest"))
        && File.Exists(Path.Combine(made, "favicons", "favicon.svg")) && logos.Logos.HasFaviconHtml && logos.Logos.FaviconHtml.Contains("theme-color\" content=\"#E8590C", StringComparison.Ordinal));
    SaveWindow(logoWindow, "tools-logos");
    // The brand sheet: this logo, the palette built in Colours above, and two typefaces.
    Check("The brand sheet shows the palette from Colours and who it is from", logos.Logos.BrandColours.Count == 4 && logos.Logos.BrandColours[0].Tip == "Primary #E8590C");
    logos.Logos.BrandHeadingFont = "A Typeface Nobody Has";
    logos.Logos.BrandBodyFont = "";
    logos.Logos.BrandPreparedBy = "Webignite";
    var sheet = Path.Combine(output, "brand-sheet.pdf");
    Wait(logos.Logos.SaveBrandSheetToCommand.ExecuteAsync(sheet), "The brand sheet was made");
    Check("The brand sheet is one page with the name and the colour codes as real text, and names the missing typeface",
        Until(() => PdfPageCount(sheet) == 1 && PdfReadText(sheet).Contains("Karoo Coffee", StringComparison.Ordinal) && PdfReadText(sheet).Contains("#E8590C", StringComparison.Ordinal))
        && logos.Logos.LogoMessage.Contains("A Typeface Nobody Has isn't installed", StringComparison.Ordinal) && !logos.Logos.LogoProblem);
    SaveWindow(logoWindow, "tools-logos-brand");
    logos.Logos.LogoName = "";
    Check("A brand sheet without a name is asked for one", logos.Logos.BrandSheetProblem() is not null && logos.Logos.LogoProblem);
    logos.Logos.LogoName = "Karoo Coffee";
    Wait(logos.Logos.OpenLogoAsync(Path.Combine(data, "logos", "sign.png")), "A small picture was opened");
    Check("A small picture on a solid background says what will be left out",
        logos.Logos.LogoPreviews.Count == 1 && logos.Logos.LogoWarning.Contains("120 pixels wide", StringComparison.Ordinal) && logos.Logos.LogoWarning.Contains("no see-through background", StringComparison.Ordinal));
    logoWindow.Close();
    Check("Closing the window lets the logo pictures go", logos.Logos.LogoPreviews.Count == 0);
}
// Social images: one wide photo cut to each network's shape.
{
    var photo = Path.Combine(data, "logos", "opening day.jpg");
    using (var drawn = new SkiaSharp.SKBitmap(2400, 1600))
    {
        using (var canvas = new SkiaSharp.SKCanvas(drawn))
        {
            canvas.Clear(new SkiaSharp.SKColor(0xD9, 0xC7, 0xB0));
            using var paint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0x3D, 0x2B, 0x1F), IsAntialias = true };
            canvas.DrawRect(0, 1100, 2400, 500, paint);
            paint.Color = new SkiaSharp.SKColor(0xE8, 0x59, 0x0C);
            canvas.DrawCircle(1200, 700, 380, paint);
            paint.Color = new SkiaSharp.SKColor(0x1B, 0x6C, 0xA8);
            canvas.DrawRect(120, 200, 420, 620, paint);
        }
        using var jpg = drawn.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        File.WriteAllBytes(photo, jpg.ToArray());
    }
    var socialWindow = window.ShowTool(ToolKind.Social, services);
    var social = socialWindow.Tools;
    Check("The usual sizes are ticked to begin with, and nothing can be saved without a picture",
        social.Social.SocialChoices.Count(c => c.IsOn) == 6 && !social.Social.SaveSocialToCommand.CanExecute(null));
    Wait(social.Social.OpenSocialAsync(photo), "The photo was opened");
    Check("Each ticked size is shown as it will be cut, named after the file",
        Until(() => social.Social.SocialPreviews.Count == 6) && social.Social.SocialName == "opening day" && social.Social.SocialInfo == "2,400 × 1,600 pixels."
        // The photo is too short for a story, so that one alone is marked as enlarged.
        && social.Social.SocialPreviews.Single(p => p.Soft).Label == "Instagram story or reel");
    SaveWindow(socialWindow, "tools-social");
    social.Social.SocialChoices.First(c => c.Size.Label == "LinkedIn page cover").IsOn = true;
    social.Social.SocialChoices.First(c => c.Size.Label == "Facebook post").IsOn = false;
    Check("Ticking and unticking sizes redraws them", Until(() => social.Social.SocialPreviews.Count == 6 && social.Social.SocialPreviews.Any(p => p.Label == "LinkedIn page cover") && social.Social.SocialPreviews.All(p => p.Label != "Facebook post")));
    var socialOut = Path.Combine(data, "social-made");
    Wait(social.Social.SaveSocialToCommand.ExecuteAsync(socialOut), "The social pictures were saved");
    Check("Every ticked size is saved at its exact size, named for where it goes",
        Directory.GetFiles(socialOut).Length == 6 && social.Social.SocialMessage.StartsWith("6 pictures saved. One had to be enlarged", StringComparison.Ordinal) && social.Social.HasSocialMade
        && Until(() => Webignite.Tools.PictureTools.Size(Path.Combine(socialOut, "opening-day-instagram-story-or-reel-1080x1920.jpg")) == (1080, 1920)
            && Webignite.Tools.PictureTools.Size(Path.Combine(socialOut, "opening-day-linkedin-page-cover-1128x191.jpg")) == (1128, 191)));
    Wait(social.Social.OpenSocialAsync(Path.Combine(data, "logos", "sign.png")), "A small picture was opened");
    Check("A picture too small for a size says it will be enlarged", Until(() => social.Social.SocialPreviews.Count == 6 && social.Social.SocialPreviews.All(p => p.Soft)));
    socialWindow.Close();
    Check("Closing the window lets the small pictures go", social.Social.SocialPreviews.Count == 0);
}
// Clipboard history: what was copied, text and pictures, newest first, in memory only.
services.Clipboard.Add("thandi@karoocoffee.co.za");
services.Clipboard.Add("Hi Pieter,\n\nThe new menu is live on the site. Have a look and shout if anything needs changing.\n\nEddie");
using (var shot = window.CaptureRenderedFrame()!)
using (var shotStream = new MemoryStream())
{
    shot.Save(shotStream, new PngBitmapEncoderOptions());
    services.Clipboard.Add(new Webignite.Core.Tools.Clip(null, shotStream.ToArray(), shot.PixelSize.Width, shot.PixelSize.Height));
}
services.Clipboard.Add("INV-0107");
services.Clipboard.Add("thandi@karoocoffee.co.za");
var clipWindow = window.ShowTool(ToolKind.Clipboard, services);
Check("Clipboard history lists each copy once, newest first, pictures too", clipWindow.Tools.Clipboard.Clips.Count == 4
    && clipWindow.Tools.Clipboard.Clips[0].Clip.Text == "thandi@karoocoffee.co.za" && clipWindow.Tools.Clipboard.Clips[2].IsPicture && clipWindow.Tools.Clipboard.Clips[2].Thumbnail is not null);
SaveWindow(clipWindow, "tools-clipboard");
clipWindow.Tools.Clipboard.ClipSearch = "pieter";
Check("The search finds a copy by its words", clipWindow.Tools.Clipboard.Clips.Count == 1 && clipWindow.Tools.Clipboard.Clips[0].Preview.StartsWith("Hi Pieter", StringComparison.Ordinal));
clipWindow.Tools.Clipboard.ClipSearch = "";
clipWindow.Tools.Clipboard.Clips[1].ForgetCommand.Execute(null);
Check("Forget takes one off", clipWindow.Tools.Clipboard.Clips.Count == 3 && services.Clipboard.Items.Count == 3);
clipWindow.Close();
Check("Closing the window stops it following the history and lets the pictures go", clipWindow.Tools.Clipboard.Clips.Count == 0);
// Chat with your partner: words, a screenshot and a file, in a small window. Signed in only for the shot.
{
    var me = services.Team().Me!.Id;
    var partnerId = services.Team().Partner!.Id;
    var chatStart = DateTimeOffset.Now.AddMinutes(-40);
    byte[] chatShot;
    using (var shot = window.CaptureRenderedFrame()!)
    using (var shotStream = new MemoryStream())
    {
        shot.Save(shotStream, new PngBitmapEncoderOptions());
        chatShot = shotStream.ToArray();
    }
    var (shotFile, shotParts) = Webignite.Core.Chat.ChatRules.Split("Karoo Coffee menu page.png", chatShot);
    foreach (var part in shotParts)
        services.Store.SaveChatPart(part);
    var pdf = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 sample");
    var (pdfFile, pdfParts) = Webignite.Core.Chat.ChatRules.Split("Karoo Coffee quote QU-0042.pdf", pdf);
    foreach (var part in pdfParts)
        services.Store.SaveChatPart(part);
    services.Store.SaveChatMessage(new Webignite.Core.Chat.ChatMessage { Id = "snap-chat-1", From = partnerId, Text = "Morning! Did Pieter come back on the menu page?", Sent = chatStart });
    services.Store.SaveChatMessage(new Webignite.Core.Chat.ChatMessage { Id = "snap-chat-2", From = me, Text = "Yes, he likes it. Just wants the prices bigger. This is how it looks now:", Files = [shotFile], Sent = chatStart.AddMinutes(4) });
    services.Store.SaveChatMessage(new Webignite.Core.Chat.ChatMessage { Id = "snap-chat-3", From = partnerId, Text = "Looks good. Here's the quote for the extra pages, can you approve it when you're happy?", Files = [pdfFile], Sent = chatStart.AddMinutes(9) });
    services.Secrets.Write("webignite-sync-refresh", "snapshot");
    // Jaco's PC synced a moment ago and he's been using it: he shows Available, and he's read my message.
    services.Store.MarkSent([(Webignite.Core.Sync.SyncKinds.Chat, "snap-chat-2", "snapshot")]);
    services.Chat.TakeIn(new Webignite.Core.Chat.Presence(partnerId, Webignite.Core.Chat.PresenceState.Available, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        chatStart.AddMinutes(5)));
    var chatWindow = window.ShowChat(services);
    Check("With the chat open, messages are looked for every few seconds", services.ChatOpen);
    Check("The chat reads as WhatsApp's: a day chip, his name with online under it, two ticks on what he's read",
        chatWindow.Chat.Messages[0].Day == "Today" && !chatWindow.Chat.Messages[1].HasDay && chatWindow.Chat.PartnerName == "Jaco"
        && chatWindow.Chat.PartnerLine == "online" && chatWindow.Chat.Messages[1].Tick == Webignite.Core.Chat.ChatTick.Read);
    Check("The chat shows my partner's status and lets me pick mine", chatWindow.Chat.PartnerShown == Webignite.Core.Chat.PresenceState.Available
        && chatWindow.Chat.PartnerStatusText == "Jaco is available" && chatWindow.Chat.StatusChoices.Count == 3);
    Check("The chat shows both of you, the screenshot as a picture and the file to open",
        chatWindow.Chat.Messages.Count == 3 && chatWindow.Chat.CanChat && chatWindow.Chat.Messages[1].Files.Single().Picture is not null
        && chatWindow.Chat.Messages[2].Files.Single().Picture is null && chatWindow.Chat.Title == "Chat with Jaco");
    chatWindow.Chat.Draft = "Will do, give me ten minutes";
    SaveWindow(chatWindow, "chat");
    // Enter sends; Shift+Enter is a new line.
    chatWindow.Activate();
    chatWindow.FindDescendantOfType<Avalonia.Controls.TextBox>()!.Focus();
    chatWindow.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.Shift, Avalonia.Input.PhysicalKey.Enter, null);
    Dispatcher.UIThread.RunJobs();
    Check("Shift+Enter doesn't send", chatWindow.Chat.Messages.Count == 3);
    chatWindow.Chat.Draft = "Will do, give me ten minutes";
    chatWindow.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
    Dispatcher.UIThread.RunJobs();
    Check("Enter in the box sends the message", chatWindow.Chat.Messages.Count == 4 && chatWindow.Chat.Draft.Length == 0);
    // A new message while the chat isn't in front: a pop-up in the corner of the screen.
    var popUp = new ChatPopUp(() => { });
    popUp.Say("Jaco", "Looks good. Here's the quote for the extra pages, can you approve it when you're happy?");
    SaveWindow(popUp, "chat-pop-up");
    popUp.Close();
    // Files dragged over the chat: the whole window says where they'll go.
    var chatView = chatWindow.GetVisualDescendants().OfType<ChatView>().First();
    var dropHint = chatView.GetVisualDescendants().OfType<Avalonia.Controls.Border>().First(b => b.Name == "DropHint");
    dropHint.IsVisible = true;
    SaveWindow(chatWindow, "chat-drop");
    dropHint.IsVisible = false;
    // A file in a message: right-click to open, copy or save it.
    var fileButton = chatView.GetVisualDescendants().OfType<Avalonia.Controls.Button>().First(b => b.Classes.Contains("chatfile"));
    fileButton.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
    Dispatcher.UIThread.RunJobs();
    Check("A file in a message offers Open, Copy and Save a copy", fileButton.ContextMenu is { } fileMenu
        && fileMenu.Items.OfType<Avalonia.Controls.MenuItem>().Select(i => i.Header as string).SequenceEqual(["Open", "Copy picture", "Save a copy…"]));
    fileButton.ContextMenu?.Close();
    chatWindow.Close();
    // The Chat button opens the chat, and a second click closes it again.
    Dispatcher.UIThread.RunJobs();
    vm.OpenChatCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    var chatOpened = services.ChatOpen;
    vm.OpenChatCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    Check("The Chat button opens the chat, and clicked again closes it", chatOpened && !services.ChatOpen);
    Dispatcher.UIThread.RunJobs();
    Check("Closing the chat goes back to looking every minute", !services.ChatOpen);
    services.Secrets.Delete("webignite-sync-refresh");
}
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
// The picture viewer: fitted to the window, the next picture in the folder a key away, turned in the file.
{
    var viewer = window.ShowPicture(Path.Combine(photos, "Team photo.png"), services);
    Check("A picture opens in the viewer, fitted to the window", Until(() => viewer.Picture.Picture is not null)
        && viewer.Picture.Fitting && viewer.Picture.Zoom < 1 && viewer.Picture.Info.Contains("2400 × 1600", StringComparison.Ordinal));
    SaveWindow(viewer, "picture-viewer");
    viewer.Picture.NextCommand.Execute(null);
    Check("The arrow key goes on to the next picture in the folder, and round", Until(() => viewer.Picture.Name == "Shop Front (1).jpg" && viewer.Picture.FullWidth == 4032));
    viewer.Picture.ZoomInCommand.Execute(null);
    Check("Zooming by hand stops it following the window", !viewer.Picture.Fitting);
    var noteBeforeTurn = services.ShowNote;
    services.ShowNote = null; // the Undo note would stay in every picture after this one
    viewer.Picture.TurnRightCommand.Execute(null);
    Check("Turn saves the picture turned", Until(() => Webignite.Tools.PictureTools.Size(Path.Combine(photos, "Shop Front (1).jpg")) == (3024, 4032)
        && viewer.Picture.FullWidth == 3024));
    services.ShowNote = noteBeforeTurn;
    viewer.Picture.AskDeleteCommand.Execute(null);
    Check("Delete asks first", viewer.Picture.ConfirmingDelete);
    viewer.Picture.KeepItCommand.Execute(null);
    var again = window.ShowPicture(Path.Combine(photos, "Team photo.png"), services);
    Check("Opening another picture uses the same viewer", ReferenceEquals(again, viewer) && Until(() => viewer.Picture.Name == "Team photo.png"));
    viewer.Close();
    Check("Closing the viewer lets the picture go", viewer.Picture.Picture is null);

    // The viewer's tools, on copies so the pictures above stay as they are.
    var editing = Path.Combine(data, "editing");
    Directory.CreateDirectory(editing);
    File.Copy(Path.Combine(photos, "Team photo.png"), Path.Combine(editing, "team.png"));
    var team = Path.Combine(editing, "team.png");
    var tools = window.ShowPicture(team, services);
    var pic = tools.Picture;
    Until(() => pic.Picture is not null);
    var showNote = services.ShowNote;
    Action? undo = null;
    services.ShowNote = (_, _, act) => undo = act; // the Undo in the corner, kept to press below

    pic.StartCropCommand.Execute(null);
    pic.DragCrop(new Point(100 * pic.Zoom, 100 * pic.Zoom), new Point(1300 * pic.Zoom, 900 * pic.Zoom));
    Dispatcher.UIThread.RunJobs();
    SaveWindow(tools, "picture-crop");
    pic.ApplyCropCommand.Execute(null);
    Check("Crop keeps only the part dragged over", Until(() => Webignite.Tools.PictureTools.Size(team) == (1200, 800) && pic.FullWidth == 1200));
    undo?.Invoke();
    Check("Undo puts the picture back as it was", Until(() => Webignite.Tools.PictureTools.Size(team) == (2400, 1600) && pic.FullWidth == 2400));

    pic.StartResizeCommand.Execute(null);
    pic.ResizeWidth = "600";
    Check("Resize fills in the height to keep the shape", pic.ResizeHeight == "400");
    pic.ApplyResizeCommand.Execute(null);
    Check("Resize saves the new size", Until(() => Webignite.Tools.PictureTools.Size(team) == (600, 400)));

    pic.FlipAcrossCommand.Execute(null);
    Check("Flip saves the picture flipped", Until(() => !pic.Busy && pic.Picture is not null && pic.FullWidth == 600));

    pic.ConvertToCommand.Execute("WebP");
    Check("Make a WebP copy shows before and after, beside the original", Until(() => pic.ShowingResult)
        && File.Exists(Path.Combine(editing, "team.webp")) && pic.AfterText.Contains("WEBP", StringComparison.Ordinal));
    SaveWindow(tools, "picture-before-after");
    pic.ThrowResultAwayCommand.Execute(null);
    Check("Throw it away deletes the copy", !File.Exists(Path.Combine(editing, "team.webp")) && !pic.ShowingResult);

    pic.ShrinkForWebCommand.Execute(null);
    Check("Shrink for a website makes a copy in a web folder", Until(() => pic.ShowingResult) && File.Exists(Path.Combine(editing, "web", "team.webp")));
    pic.KeepResultCommand.Execute(null);

    pic.StartPickingCommand.Execute(null);
    pic.PickAt(new Point(10, 10)).GetAwaiter().GetResult();
    Check("Picking from the picture copies the colour and remembers it", pic.PickedHex.StartsWith('#') && services.Settings.RecentColours.FirstOrDefault() == pic.PickedHex);
    services.ShowNote = showNote;
    tools.Close();
}
// Webignite opened again, e.g. from a picture in Explorer, hands over to the copy running and stops.
{
    Check("The first copy of the app keeps it", Webignite.Desktop.SingleCopy.Claim([]));
    string? handed = null;
    Webignite.Desktop.SingleCopy.Listen(opening => handed = opening);
    var given = Path.Combine(photos, "Team photo.png");
    var carriedOn = Task.Run(() => Webignite.Desktop.SingleCopy.Claim([given])).GetAwaiter().GetResult();
    Check("A second start hands its picture to the running copy and stops", !carriedOn && Until(() => handed == given));
    Webignite.Desktop.SingleCopy.Release();
}
calc.Calculator.AddToInvoiceCommand.Execute(null);
calculatorWindow.Close();
Save("tools-calculator-invoice");
vm.Invoices.Editor!.Lines[0].Price = "3 x 650";
Save("invoice-sum");

// Site check, with made-up results (no network here)
// The two menus that aren't the sidebar (Settings, Look and feel, Menu), in Classic and in Airy.
{
    var was = services.Settings.Appearance;
    Open(Page.Overview);
    services.Settings.Appearance = was with { Menu = MenuKind.Button };
    Dispatcher.UIThread.RunJobs();
    Check("With the Menu button the sidebar is gone and a bar along the top takes its place", vm.MenuPart.IsButtonMenu && vm.MenuPart.HasMenuBar && !vm.MenuPart.IsSidebarMenu && !vm.MenuPart.IsMenuOpen);
    Save("menu-button");
    vm.MenuPart.ToggleMenuCommand.Execute(null);
    Check("The menu lists every group the sidebar has, and Planner's own pages",
        vm.MenuPart.IsMenuOpen && vm.MenuPart.MenuColumns.Count >= 6 && vm.MenuPart.MenuColumns[0].Title == "Start" && vm.MenuPart.MenuColumns.Any(c => c.Title == "Planner")
        && vm.MenuPart.MenuColumns.SelectMany(c => c.Entries).Count(e => e.IsCurrent) == 1);
    Check("The menu says how things stand beside its pages", vm.MenuPart.MenuColumns.SelectMany(c => c.Entries).Any(e => e.HasFigure));
    Check("Retainers, domains, money and clients have their figures too",
        vm.MenuPart.MenuColumns.SelectMany(c => c.Entries).Count(e => e.HasFigure && e.PageKey is "Retainers" or "Renewals" or "Money" or "Clients") == 4);
    Save("menu-button-open");
    Check("Settings comes last in the menu", vm.MenuPart.MenuColumns[^1].Entries.Any(e => e.PageKey == "Settings") && !vm.MenuPart.MenuColumns[^1].CanFold);
    {
        var tools = vm.MenuPart.MenuColumns.First(c => c.Title == "Web tools");
        vm.MenuPart.FoldInMenuCommand.Execute(tools);
        vm.MenuPart.FoldInMenuCommand.Execute(vm.MenuPart.MenuColumns.First(c => c.Title == "Computer"));
        Dispatcher.UIThread.RunJobs();
        Check("A click on a group's heading folds it shut, and the menu stays open",
            vm.MenuPart.IsMenuOpen && vm.MenuPart.MenuColumns.First(c => c.Title == "Web tools") is { IsFolded: true, Entries.Count: > 0 }
            && services.Settings.Appearance.MenuFolded is { Count: 2 });
        Save("menu-button-open-folded");
        vm.MenuPart.ToggleMenuCommand.Execute(null);
        vm.MenuPart.ToggleMenuCommand.Execute(null);
        Check("It opens as it was left", vm.MenuPart.MenuColumns.First(c => c.Title == "Computer").IsFolded);
        vm.MenuPart.FoldInMenuCommand.Execute(vm.MenuPart.MenuColumns.First(c => c.Title == "Web tools"));
        vm.MenuPart.FoldInMenuCommand.Execute(vm.MenuPart.MenuColumns.First(c => c.Title == "Computer"));
        Dispatcher.UIThread.RunJobs();
        Check("Another click opens it again", vm.MenuPart.MenuColumns.All(c => !c.IsFolded) && services.Settings.Appearance.MenuFolded is { Count: 0 });
    }
    vm.MenuPart.PickFromMenuCommand.Execute(vm.MenuPart.MenuColumns.SelectMany(c => c.Entries).First(e => e.PageKey == "Invoices"));
    Check("Picking a page from the menu opens it and closes the menu", !vm.MenuPart.IsMenuOpen && vm.Selected.Key == Page.Invoices);
    vm.MenuPart.ToggleMenuCommand.Execute(null);
    vm.MenuPart.PickFromMenuCommand.Execute(vm.MenuPart.MenuColumns.SelectMany(c => c.Entries).First(e => e.Item is ToolLink { Kind: ToolKind.Calculator }));
    Check("Picking a tool from the menu opens its window and leaves the page", !vm.MenuPart.IsMenuOpen && vm.Selected.Key == Page.Invoices);
    Open(Page.Overview);

    services.Settings.Appearance = was with { Menu = MenuKind.Dock };
    Dispatcher.UIThread.RunJobs();
    Check("The dock holds the usual pages, with the one on screen lit", vm.MenuPart.IsDockMenu && vm.MenuPart.Dock.Count == DockPages.Usual.Count && vm.MenuPart.Dock.Single(e => e.IsCurrent).PageKey == "Overview");
    Check("Picking the dock in Settings puts the dock and the top bar on screen at once",
        window.GetVisualDescendants().OfType<DockView>().Any() && window.GetVisualDescendants().OfType<MenuBarView>().Any());
    Save("menu-dock");
    vm.MenuPart.ToggleMenuCommand.Execute(null);
    var notes = vm.MenuPart.MenuColumns.SelectMany(c => c.Entries).First(e => e.PageKey == "Notes");
    Check("A page not on the dock can be added from the menu", notes.CanAdd && !notes.OnDock);
    vm.MenuPart.AddToDockCommand.Execute(notes);
    Check("Added, it is on the dock and stays there", vm.MenuPart.Dock.Any(e => e.PageKey == "Notes") && services.Settings.Appearance.Dock!.Contains("Notes"));
    vm.MenuPart.CloseMenuCommand.Execute(null);
    vm.MenuPart.TakeOffDockCommand.Execute(vm.MenuPart.Dock.First(e => e.PageKey == "Notes"));
    Check("Taken off, it is gone again", vm.MenuPart.Dock.All(e => e.PageKey != "Notes"));
    Open(Page.Mail);
    Check("On another page the dock lights that one", vm.MenuPart.Dock.Single(e => e.IsCurrent).PageKey == "Mail");
    Save("menu-dock-mail");
    // Beside the dock and on it: the person's own choice. A tool or a whole group can go on the dock, and the small
    // things left and right of it (the day, the timer, chat, a clock) are moved, taken off and put back.
    Check("A new install has the day and the timer left of the dock, chat and a clock right of it",
        vm.DockBarPart.BarLeft.Select(b => b.Widget).SequenceEqual(DockBar.Usual.Left) && vm.DockBarPart.BarRight.Select(b => b.Widget).SequenceEqual(DockBar.Usual.Right)
        && vm.DockBarPart.ClockText.Length > 0 && vm.DockBarPart.TodayText.Length > 0 && !vm.DockBarPart.DockFloats);
    vm.MenuPart.TakeOffDockCommand.Execute(vm.MenuPart.Dock.First(e => e.PageKey == "Money"));
    vm.MenuPart.ToggleMenuCommand.Execute(null);
    var webTools = vm.MenuPart.MenuColumns.First(c => c.Heading?.PageKey == DockKeys.Group("web-tools")).Heading!;
    Check("A whole group can be added to the dock from the menu, but not the columns that aren't a group",
        webTools.CanAdd && !webTools.OnDock && vm.MenuPart.MenuColumns.First().Heading is null);
    vm.MenuPart.AddToDockCommand.Execute(webTools);
    var calculator = vm.MenuPart.MenuColumns.SelectMany(c => c.Entries).First(e => e.PageKey == DockKeys.Tool("Calculator"));
    Check("So can a tool", calculator.CanAdd && !calculator.OnDock);
    vm.MenuPart.AddToDockCommand.Execute(calculator);
    vm.MenuPart.CloseMenuCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    var dockGroup = vm.MenuPart.Dock.Single(e => e.IsGroup);
    Check("The group on the dock holds what is in it, for the short list above the dock",
        dockGroup.Title == "Web tools" && dockGroup.Inside.Count > 2 && dockGroup.Inside.All(e => !e.IsGroup));
    Check("The tool is on the dock too, and both are saved",
        vm.MenuPart.Dock.Any(e => e.PageKey == DockKeys.Tool("Calculator")) && services.Settings.Appearance.Dock!.Contains(DockKeys.Group("web-tools")));
    Save("menu-dock-group-and-tool");
    // Groups folded in the sidebar still show in the menu behind More: there is nowhere to unfold them there.
    {
        var folded = vm.Sidebar.OnMenus().OfType<NavGroup>().Where(g => g.Key is "billing" or "client-work").ToList();
        foreach (var heading in folded)
            if (heading.IsOpen) heading.Toggle();
        vm.MenuPart.ToggleMenuCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var titles = vm.MenuPart.MenuColumns.Select(c => c.Title).ToList();
        Check("A group folded in the sidebar is still in the menu, with everything in it",
            titles.Contains("Billing") && titles.Contains("Client work") && titles.Contains("Web tools") && titles.Contains("My pages")
            && vm.MenuPart.MenuColumns.First(c => c.Title == "Billing").Entries.Any(e => e.PageKey == "Invoices"));
        var plannerHead = vm.MenuPart.MenuColumns.First(c => c.Title == "Planner").Heading;
        Check("Planner's heading can go on the dock, and the columns that aren't a group can't",
            plannerHead is { CanAdd: true, OnDock: false } && vm.MenuPart.MenuColumns.First(c => c.Title == "Start").Heading is null);
        vm.MenuPart.AddToDockCommand.Execute(plannerHead);
        Dispatcher.UIThread.RunJobs();
        Save("menu-dock-more");
        // What the pop-ups above the dock hold: Planner's own menu with the projects, and a group of tools.
        {
            var dockView = window.GetVisualDescendants().OfType<DockView>().First();
            foreach (var (name, key) in new[] { ("dock-pop-planner", DockKeys.Group("planner")), ("dock-pop-web-tools", DockKeys.Group("web-tools")) })
            {
                var pop = new Avalonia.Controls.Window
                {
                    Width = 320, SizeToContent = Avalonia.Controls.SizeToContent.Height, Padding = new Avalonia.Thickness(16),
                    Content = dockView.GroupPicture(vm.MenuPart.OnDock(key)!),
                };
                pop.Show();
                SaveWindow(pop, name);
                pop.Close();
            }
            var plannerList = vm.MenuPart.OnDock(DockKeys.Group("planner"))!.Inside;
            Check("Planner's pop-up is Planner's own menu: its pages, the projects under Work, the planning pages under Plan",
                plannerList.Any(e => e.IsSection && e.Title == "WORK") && plannerList.Any(e => e.IsLink)
                && plannerList.First().PageKey == "Home" && plannerList.Any(e => e.PageKey == "Calendar"));
            var order = vm.MenuPart.Dock.Select(e => e.PageKey).ToList();
            vm.MenuPart.MoveOnDock(vm.MenuPart.Dock[^1], vm.MenuPart.Dock[0], false);
            Dispatcher.UIThread.RunJobs();
            Check("Dragging something along the dock moves it there, and it stays",
                vm.MenuPart.Dock[0].PageKey == order[^1] && vm.MenuPart.Dock[1].PageKey == order[0] && services.Settings.Appearance.Dock![0] == order[^1]);
            vm.MenuPart.MoveOnDock(vm.MenuPart.Dock[0], vm.MenuPart.Dock[^1], true);
            Dispatcher.UIThread.RunJobs();
            Check("Dragged back, the dock is as it was", vm.MenuPart.Dock.Select(e => e.PageKey).SequenceEqual(order));
        }
        Check("Adding to the dock leaves the menu open, and Planner as a group takes the place of its own button",
            vm.MenuPart.IsMenuOpen && vm.MenuPart.Dock.Count(e => e.Title == "Planner") == 1);
        vm.MenuPart.CloseMenuCommand.Execute(null);
        var plannerOnDock = vm.MenuPart.Dock.First(e => e.PageKey == DockKeys.Group("planner"));
        Check("Planner on the dock opens a list of Planner's pages",
            plannerOnDock.IsGroup && plannerOnDock.Inside.Any(e => e.PageKey == "Projects") && plannerOnDock.Inside.Any(e => e.PageKey == "Calendar"));
        vm.MenuPart.TakeOffDockCommand.Execute(plannerOnDock);
        foreach (var heading in folded)
            if (!heading.IsOpen) heading.Toggle();
        Dispatcher.UIThread.RunJobs();
    }
    Open(dockGroup.Inside.Select(e => e.Item).OfType<Module>().First().Key);
    Check("With a page of the group on screen, the group is the lit one", vm.MenuPart.Dock.Single(e => e.IsCurrent).IsGroup);
    Open(Page.Mail);
    vm.DockBarPart.MoveBarAcrossCommand.Execute(vm.DockBarPart.BarLeft.First(b => b.IsTimer));
    vm.DockBarPart.TakeOffBarCommand.Execute(vm.DockBarPart.BarRight.First(b => b.IsClock));
    vm.DockBarPart.AddToBarLeftCommand.Execute(vm.DockBarPart.BarAvailable.First(b => b.IsSearch));
    Check("What is beside the dock is moved across, taken off and added",
        vm.DockBarPart.BarLeft.Select(b => b.Widget).SequenceEqual([BarWidget.Today, BarWidget.Search])
        && vm.DockBarPart.BarRight.Select(b => b.Widget).SequenceEqual([BarWidget.Chat, BarWidget.Timer]) && vm.DockBarPart.BarAvailable.Any(b => b.IsClock));
    services.Settings.Appearance = services.Settings.Appearance with { DockFloats = true };
    Dispatcher.UIThread.RunJobs();
    Check("The dock can float over the page", vm.DockBarPart.DockFloats);
    Check("Floating, the dock takes no room of its own under the page",
        window.GetVisualDescendants().OfType<DockView>().FirstOrDefault()?.Parent is Avalonia.Controls.ContentControl { Margin.Top: < 0 });
    Save("menu-dock-floating");
    // On a wide screen there is room for everything beside the dock; on a narrow one only what fits whole shows.
    window.Width = 1900;
    Dispatcher.UIThread.RunJobs();
    Save("menu-dock-floating-wide");
    services.Settings.Appearance = services.Settings.Appearance with { DockFloats = false };
    Dispatcher.UIThread.RunJobs();
    Save("menu-dock-wide");
    window.Width = 1280;
    Dispatcher.UIThread.RunJobs();
    vm.DockBarPart.UsualBarCommand.Execute(null);
    services.Settings.Appearance = services.Settings.Appearance with { BarLeft = [], BarRight = [] };
    Dispatcher.UIThread.RunJobs();
    Check("With nothing beside it, the dock stands alone", !vm.DockBarPart.HasBarLeft && !vm.DockBarPart.HasBarRight);
    services.Settings.Appearance = was with { Menu = MenuKind.Dock };
    Dispatcher.UIThread.RunJobs();
    Check("A group taken off the dock leaves it as it was", vm.MenuPart.Dock.Count == DockPages.Usual.Count && vm.MenuPart.Dock.All(e => !e.IsGroup) && !vm.DockBarPart.DockFloats);
    Open(Page.Overview);
    services.Settings.Appearance = was with { Menu = MenuKind.Dock, Style = LookStyle.Airy, Sidebar = SidebarTone.Light };
    Dispatcher.UIThread.RunJobs();
    Save("menu-dock-airy");
    services.Settings.Appearance = was with { Menu = MenuKind.Button, Style = LookStyle.Airy };
    vm.MenuPart.ToggleMenuCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    Save("menu-button-open-airy");
    vm.MenuPart.CloseMenuCommand.Execute(null);
    services.Settings.Appearance = was;
    Dispatcher.UIThread.RunJobs();
    Check("Back on the sidebar, the bar and the dock are gone", vm.MenuPart.IsSidebarMenu && !vm.MenuPart.HasMenuBar && vm.MenuPart.Dock.Count == 0);
}
// Choosing what is on the menus: from the sidebar the menu opens to edit; an entry taken off is gone from every
// menu and waits under Available; Settings can't go; what is off is still found by search.
{
    var was = services.Settings.Appearance;
    var calculator = vm.Sidebar.OnMenus().OfType<ToolLink>().First(t => t.Kind == ToolKind.Calculator);
    var retainers = vm[Page.Retainers];
    var before = vm.Sidebar.Items.Count;
    vm.Sidebar.EditCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    Check("From the sidebar, the menu opens to choose what is on it, with nothing under Available yet",
        vm.MenuPart.IsMenuOpen && vm.MenuPart.IsMenuEditing && vm.MenuPart.IsSidebarMenu && vm.MenuPart.MenuAvailable.Count == 0 && vm.MenuPart.NothingAvailable && !vm.MenuPart.HasMenuTakenOff);
    MenuEntry InMenu(object item) => vm.MenuPart.MenuColumns.SelectMany(c => c.Entries).Concat(vm.MenuPart.MenuAvailable).First(e => ReferenceEquals(e.Item, item));
    Check("Each entry is listed once while editing", vm.MenuPart.MenuColumns.SelectMany(c => c.Entries).GroupBy(e => e.Item).All(g => g.Count() == 1));
    vm.MenuPart.PickFromMenuCommand.Execute(InMenu(calculator));
    vm.MenuPart.PickFromMenuCommand.Execute(InMenu(retainers));
    Dispatcher.UIThread.RunJobs();
    Check("A click takes an entry off: it moves to Available, and the page on screen stays",
        vm.MenuPart.MenuAvailable.Select(e => e.Title).SequenceEqual(["Retainers", "Calculator"]) && vm.Selected.Key != Page.Retainers && vm.MenuPart.IsMenuOpen && vm.MenuPart.HasMenuTakenOff
        && vm.Sidebar.IsOff(calculator) && vm.Sidebar.IsOff(retainers));
    var settingsEntry = InMenu(vm[Page.Settings]);
    vm.MenuPart.PickFromMenuCommand.Execute(settingsEntry);
    Check("Settings always stays", !settingsEntry.ShowsMinus && !vm.Sidebar.IsOff(vm[Page.Settings]) && settingsEntry.EditTip == "This one always stays");
    Save("menu-edit");
    vm.MenuPart.DoneEditingMenuCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    Check("Done closes it, and the sidebar no longer lists what was taken off",
        !vm.MenuPart.IsMenuOpen && !vm.MenuPart.IsMenuEditing && vm.Sidebar.Items.Count == before - 2 && !vm.Sidebar.Items.Contains(calculator) && !vm.Sidebar.Items.Contains(retainers));
    Save("menu-edit-sidebar");
    Check("A page taken off is still there to open, by search or a link", vm.Modules.Contains(retainers));
    services.Settings.Appearance = was with { Menu = MenuKind.Dock, Dock = ["Overview", "Retainers", "Clients"] };
    Dispatcher.UIThread.RunJobs();
    Check("It is off the dock too", vm.MenuPart.Dock.Select(e => e.PageKey).SequenceEqual(["Overview", "Clients"]));
    vm.MenuPart.ToggleMenuCommand.Execute(null);
    Check("And off the open menu, which offers to edit", vm.MenuPart.IsMenuOpen && vm.MenuPart.IsMenuBrowsing && vm.MenuPart.MenuColumns.SelectMany(c => c.Entries).All(e => e.Title is not ("Retainers" or "Calculator")) && vm.MenuPart.MenuAvailable.Count == 0);
    vm.MenuPart.EditMenuCommand.Execute(null);
    vm.MenuPart.PickFromMenuCommand.Execute(InMenu(retainers));
    Dispatcher.UIThread.RunJobs();
    Check("Clicking it under Available puts it back where it was, on the dock as well",
        !vm.Sidebar.IsOff(retainers) && vm.MenuPart.Dock.Select(e => e.PageKey).SequenceEqual(["Overview", "Retainers", "Clients"]) && vm.MenuPart.MenuAvailable.Count == 1);
    // Dragging the calculator out of Available onto another tool puts it back just there.
    var passwords = vm.Sidebar.OnMenus().OfType<ToolLink>().First(t => t.Kind == ToolKind.Passwords);
    vm.MenuPart.MoveInMenu(InMenu(calculator), InMenu(passwords), below: true);
    var tools = vm.Sidebar.OnMenus().OfType<ToolLink>().ToList();
    Check("An entry dragged out of Available lands where it is dropped", !vm.Sidebar.IsOff(calculator) && tools.IndexOf(calculator) == tools.IndexOf(passwords) + 1 && !vm.MenuPart.HasMenuTakenOff);
    vm.MenuPart.PickFromMenuCommand.Execute(InMenu(calculator));
    vm.MenuPart.PutAllBackOnMenuCommand.Execute(null);
    Check("Put everything back empties Available", !vm.MenuPart.HasMenuTakenOff && vm.MenuPart.MenuAvailable.Count == 0);
    vm.MenuPart.CloseMenuCommand.Execute(null);
    Check("Closing the menu ends the editing", !vm.MenuPart.IsMenuOpen && !vm.MenuPart.IsMenuEditing);
    vm.Sidebar.ResetCommand.Execute(null);
    services.Settings.Appearance = was;
    Dispatcher.UIThread.RunJobs();
}
Open(Page.Crawler);
{
    var crawler = (CrawlerViewModel)vm[Page.Crawler].Main.Page;
    Check("The crawler waits for a website before it can start", !crawler.StartCommand.CanExecute(null));
    crawler.Address = "karoocoffee.co.za";
    Wait(crawler.StartCommand.ExecuteAsync(null), "The crawl finished");
    Check("With no internet the crawl says the site didn't answer, and is ready to go again",
        crawler.MessageIsProblem && crawler.Message.StartsWith("The site didn't answer", StringComparison.Ordinal) && !crawler.IsCrawling && crawler.StartCommand.CanExecute(null));
    // For a site built by JavaScript, with no browser to read it with and then with the stand-in.
    crawler.RunScripts = true;
    browserThere = false;
    Wait(crawler.StartCommand.ExecuteAsync(null), "The crawl was tried without a browser");
    Check("Without a browser a JavaScript crawl says what it needs and doesn't start",
        crawler.MessageIsProblem && crawler.Message.Contains("WebView2", StringComparison.Ordinal) && !crawler.IsCrawling && crawler.StartCommand.CanExecute(null));
    browserThere = true;
    Wait(crawler.StartCommand.ExecuteAsync(null), "The crawl with a browser finished");
    Check("A JavaScript crawl starts a browser for itself and shuts it down when it ends", standInReader is { Started: true, Disposed: true } && !crawler.IsCrawling);
    crawler.RunScripts = false;
    static Webignite.Core.Web.CrawledPage Found(string path, Webignite.Core.Web.PageFacts? facts = null, int status = 200, string? to = null, string? on = "/") => new()
    {
        Url = new Uri("https://karoocoffee.co.za" + path), Status = status, Facts = facts,
        RedirectedTo = to is null ? null : new Uri("https://karoocoffee.co.za" + to), FoundOn = on is null ? null : new Uri("https://karoocoffee.co.za" + on),
    };
    var good = new Webignite.Core.Web.PageFacts { Title = "Karoo Coffee Roasters", Description = "Fresh roasts and rusks, baked daily in Graaff-Reinet.", Headings = ["Karoo Coffee"], Images = ["/cup.jpg"] };
    var pages = new[]
    {
        Found("/", good, on: null),
        Found("/menu", good with { Title = "Menu, prices and opening hours for the Karoo Coffee Roasters café in Graaff-Reinet", Description = "" , ImagesWithoutAlt = ["/a.jpg", "/b.jpg", "/c.jpg"] }),
        Found("/about", good with { Title = "About us" }),
        Found("/specials-2024", status: 404, on: "/menu"),
        Found("/shop", status: 301, to: "/menu"),
        Found("/contact", good with { Title = "Karoo Coffee Roasters", Headings = [] }),
        Found("/thanks", good with { Title = "Thank you", NoIndex = true }),
    };
    var gone = new Webignite.Core.Web.CheckedLink(new Uri("https://karoocoffee.co.za/specials-2024"), 404, null);
    var dead = new Webignite.Core.Web.CheckedLink(new Uri("https://oldsupplier.example/"), 0, "there is no such server");
    var same = Webignite.Core.Web.SiteCrawl.SameTitles(pages);
    var rows = pages.Select(p =>
    {
        IReadOnlyList<Webignite.Core.Web.CheckedLink> broken = p.Url.AbsolutePath == "/menu" ? [gone, dead] : [];
        var problems = Webignite.Core.Web.SiteCrawl.Problems(p, broken).ToList();
        if (same.Contains(p.Url))
            problems.Add("Another page has the same title.");
        return new Webignite.Core.Web.CrawlRow(p, problems, broken);
    }).ToList();
    crawler.ShowResult(new Webignite.Services.CrawlResult(rows, false, false));
    Check("The crawl opens on the pages with something to fix, and counts them",
        crawler.ShowsToFix && crawler.Rows.Count == 6 && crawler.Summary == "7 pages, 6 with something to fix, 2 broken links.");
    Save("crawler");
    crawler.ShowOnlyCommand.Execute(CrawlFilter.BrokenLinks);
    Check("Broken links shows only the page that has them", crawler.Rows.Count == 1 && crawler.Rows[0].Path == "/menu");
    crawler.ShowOnlyCommand.Execute(CrawlFilter.All);
    Check("Every page shows them all, the good one included", crawler.Rows.Count == 7 && crawler.Rows[2].IsFine);
    Check("The spreadsheet has a line for each page under its headings", crawler.Csv.Split("\r\n").Length == 10 && crawler.CsvName.StartsWith("karoocoffee.co.za crawl ", StringComparison.Ordinal));
    crawler.ShowOnlyCommand.Execute(CrawlFilter.ToFix);
}
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

    // The eyedropper over the same frozen screen: the loupe follows the pointer and a click keeps the colour.
    var dropper = new ColourPickWindow(frozen, screenArea, 1);
    dropper.Show();
    dropper.Canvas.Hover(new Point(370, 668));
    Dispatcher.UIThread.RunJobs();
    using (var dropFrame = dropper.CaptureRenderedFrame())
    using (var dropFile = File.Create(Path.Combine(output, "colour-picking.png")))
        dropFrame?.Save(dropFile, new PngBitmapEncoderOptions());
    var under = dropper.Canvas.Under;
    dropper.Canvas.Pick(new Point(370, 668));
    Check("A click keeps the colour under the pointer", under is not null && dropper.Picked.IsCompletedSuccessfully && dropper.Picked.Result == under);
    var dropStop = new ColourPickWindow(frozen, screenArea, 1);
    dropStop.Show();
    dropStop.Canvas.Stop();
    Check("Esc stops the colour picker without a colour", dropStop.Picked.IsCompletedSuccessfully && dropStop.Picked.Result is null);

    // The ruler over the same frozen screen: pointing measures the stretch under the pointer, dragging a box.
    var ruler = new RulerWindow(frozen, screenArea, 1);
    ruler.Show();
    ruler.Canvas.Hover(new Point(370, 668));
    Dispatcher.UIThread.RunJobs();
    Check("Pointing measures across and down from the pointer", ruler.Canvas.Now is { Width: > 0, Height: > 0 });
    using (var rulerFrame = ruler.CaptureRenderedFrame())
    using (var rulerFile = File.Create(Path.Combine(output, "ruler-pointing.png")))
        rulerFrame?.Save(rulerFile, new PngBitmapEncoderOptions());
    ruler.Canvas.Press(new Point(100, 100));
    ruler.Canvas.Hover(new Point(300, 220));
    Dispatcher.UIThread.RunJobs();
    Check("Dragging measures the box dragged", ruler.Canvas.Now == new Webignite.Core.Tools.Measured(201, 121));
    using (var rulerFrame = ruler.CaptureRenderedFrame())
    using (var rulerFile = File.Create(Path.Combine(output, "ruler-box.png")))
        rulerFrame?.Save(rulerFile, new PngBitmapEncoderOptions());
    ruler.Canvas.Release(new Point(300, 220));
    Check("Letting go keeps the size", ruler.Kept.IsCompletedSuccessfully && ruler.Kept.Result == new Webignite.Core.Tools.Measured(201, 121));
    var rulerStop = new RulerWindow(frozen, screenArea, 1);
    rulerStop.Show();
    rulerStop.Canvas.Stop();
    Check("Esc stops the ruler without a size", rulerStop.Kept.IsCompletedSuccessfully && rulerStop.Kept.Result is null);

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
    Check("Add to My tasks makes a to-do that says where it is",
        store.ListTasks().Any(t => t.Customer == "Bosveld Builders" && t.Notes?.Contains(vm.Markup.SavedPath!, StringComparison.Ordinal) == true));

    var shortcutSettings = (SettingsViewModel)vm[Page.Settings].Main.Page;
    shortcutSettings.SetShortcut(Webignite.Core.Tools.GlobalShortcut.Screenshot, "Shift+S");
    Check("A shortcut that would fire while typing is refused", shortcutSettings.ShortcutProblem && services.Settings.Screenshots.Shortcut == "Ctrl+Alt+S");
    shortcutSettings.SetShortcut(Webignite.Core.Tools.GlobalShortcut.Screenshot, "Ctrl+Alt+P");
    Check("Another shortcut is kept", services.Settings.Screenshots.Shortcut == "Ctrl+Alt+P" && !shortcutSettings.ShortcutProblem);
    shortcutSettings.Open("screenshots");
    Open(Page.Settings);
    Save("settings-screenshots");
    shortcutSettings.ShortcutBackCommand.Execute(Webignite.Core.Tools.GlobalShortcut.Screenshot);
    shortcutSettings.SetShortcut(Webignite.Core.Tools.GlobalShortcut.Recording, "Ctrl+Alt+S");
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
    OpenSection("files");
    if (window.GetVisualDescendants().OfType<Webignite.Desktop.Controls.FoldSection>().FirstOrDefault(f => f.Key == "files") is { } filesRow)
        Avalonia.Controls.ControlExtensions.BringIntoView(filesRow);
    Dispatcher.UIThread.RunJobs();
    Save("client-notes");
    vm.Clients.Detail.NoteLinks.First().OpenCommand.Execute(null);
    Check("Clicking one opens it in Notes", vm.Current.Key == Page.Notes && vm.Notes.Open?.Id == kickoff.Id);

    // Site logins on the client's page: several under one client, passwords hidden until shown, bad addresses caught.
    Open(Page.Clients);
    vm.Clients.OpenCommand.Execute(vm.Clients.Rows.First(r => r.Client.Name == "Bosveld Builders"));
    var withLogins = vm.Clients.Detail!;
    withLogins.AddLoginCommand.Execute(null);
    (withLogins.LoginSite, withLogins.LoginAddress, withLogins.LoginUser, withLogins.LoginPassword) = ("WordPress admin", "bosveldbuilders.co.za/wp-admin", "pieter", "Bosveld#2026!");
    withLogins.SaveLoginCommand.Execute(null);
    withLogins.AddLoginCommand.Execute(null);
    (withLogins.LoginSite, withLogins.LoginAddress, withLogins.LoginUser, withLogins.LoginPassword, withLogins.LoginNotes) = ("Afrihost cPanel", "cp.afrihost.com", "bosveld", "kT7#pW2q", "2FA code goes to Pieter's phone");
    withLogins.SaveLoginCommand.Execute(null);
    Check("Several logins sit under one client, A to Z", withLogins.Logins.Select(l => l.Site).SequenceEqual(["Afrihost cPanel", "WordPress admin"])
        && services.Clients.Logins(services.Clients.Find("Bosveld Builders")!.Id).Count == 2);
    Check("Passwords show as dots until Show is clicked", withLogins.Logins.All(l => l.PasswordText == Webignite.Core.Clients.SiteLogins.Hidden));
    withLogins.Logins[1].RevealCommand.Execute(null);
    Check("Show reveals that one password only", withLogins.Logins[1].PasswordText == "Bosveld#2026!" && withLogins.Logins[0].PasswordText == Webignite.Core.Clients.SiteLogins.Hidden);
    withLogins.AddLoginCommand.Execute(null);
    (withLogins.LoginSite, withLogins.LoginAddress, withLogins.LoginUser) = ("Domain at domains.co.za", "javascript:alert(1)", "pieter@bosveldbuilders.co.za");
    withLogins.SaveLoginCommand.Execute(null);
    Check("A login address that isn't a web address is caught before saving", withLogins.HasLoginProblems && withLogins.Logins.Count == 2);
    withLogins.LoginAddress = "domains.co.za/login";
    Check("Fixing the address clears the problem", !withLogins.HasLoginProblems);
    withLogins.MakeLoginPasswordCommand.Execute(null);
    Check("Make one fills in a strong password", withLogins.LoginPassword.Length == 20);
    Dispatcher.UIThread.RunJobs();
    OpenSection("logins");
    if (window.GetVisualDescendants().OfType<Webignite.Desktop.Controls.FoldSection>().FirstOrDefault(f => f.Key == "logins") is { } loginsRow)
        Avalonia.Controls.ControlExtensions.BringIntoView(loginsRow);
    Dispatcher.UIThread.RunJobs();
    Save("client-logins");
    withLogins.CancelLoginCommand.Execute(null);
    Check("Cancel leaves the saved logins as they were and forgets the typed password", withLogins.Logins.Count == 2 && withLogins.LoginPassword.Length == 0);

    // Contacts: the people already on client pages become contacts once, linked both ways with their clients.
    Open(Page.Contacts);
    var people = (ContactsViewModel)vm[Page.Contacts].Main.Page;
    Check("People already on client pages become contacts", people.Rows.Any(r => r.Name == "Pieter van Wyk" && r.Client?.Name == "Bosveld Builders"));
    var bosveldId = services.Clients.Find("Bosveld Builders")!.Id;
    people.New(bosveldId);
    people.EditName = "Marlene Botha";
    people.EditRole = "Accounts";
    people.Emails[0].Value = "accounts@bosveldbuilders.co.za";
    people.EditGroups = "Hosting clients";
    people.SaveCommand.Execute(null);
    Check("A new person is saved with their client and group", services.Contacts.ForClient(bosveldId).Any(p => p.Name == "Marlene Botha" && p.Groups.SequenceEqual(["Hosting clients"])));
    Dispatcher.UIThread.RunJobs();
    Check("Someone saved without a number shows only the link to add one", people.Phones.Count == 0 && people.AddPhoneText == "+ Add a number");
    Save("contacts");
    people.SaveCommand.Execute(null);
    Check("Saving again keeps their client, after the list of clients was read again", people.EditClient?.Value?.Id == bosveldId
        && services.Contacts.ForClient(bosveldId).Any(p => p.Name == "Marlene Botha"));
    people.PickGroup("Hosting clients");
    Check("Picking a group shows only its people", people.Rows.Count == 1 && people.Rows[0].Name == "Marlene Botha");
    people.PickGroup(ContactsViewModel.AllGroups);
    var marlene = services.Contacts.ForClient(bosveldId).First(p => p.Name == "Marlene Botha");
    var pieter = services.Contacts.ForClient(bosveldId).First(p => p.Name == "Pieter van Wyk");
    people.OpenPerson(pieter.Id);
    if (people.Phones.Count == 0)
        people.AddPhoneCommand.Execute(null);
    people.Phones[0].Value = "083 444 5555";
    people.SaveCommand.Execute(null);
    Check("The contact person's new number shows on the client", services.Clients.Get(bosveldId)!.Phone == "083 444 5555");
    {
        // Pieter only looked at while Claude adds him to a group: leaving him doesn't put the old copy back.
        var connector = new Webignite.Services.AssistantBusiness(services.Store);
        connector.UpdateContact(pieter.Id, groups: ["Builders"]);
        vm.Jobs.LookForAssistants();
        Dispatcher.UIThread.RunJobs();
        people.Keep();
        Check("A person only looked at keeps Claude's change when you move on", services.Contacts.Get(pieter.Id)!.Groups.SequenceEqual(["Builders"]));
    }
    people.CloseCommand.Execute(null);

    Open(Page.Clients);
    vm.Clients.OpenCommand.Execute(vm.Clients.Rows.First(r => r.Client.Name == "Bosveld Builders"));
    var withPeople = vm.Clients.Detail!;
    withPeople.People.First(p => p.Person.Id == marlene.Id).MakeBillingCommand.Execute(null);
    Check("Sending invoices to someone puts their address first on the client", services.Clients.Get(bosveldId)!.Email!.StartsWith("accounts@bosveldbuilders.co.za", StringComparison.Ordinal)
        && withPeople.Email.StartsWith("accounts@bosveldbuilders.co.za", StringComparison.Ordinal));
    Dispatcher.UIThread.RunJobs();
    OpenSection("people");
    if (window.GetVisualDescendants().OfType<Webignite.Desktop.Controls.FoldSection>().FirstOrDefault(f => f.Key == "people") is { } peopleRow)
        Avalonia.Controls.ControlExtensions.BringIntoView(peopleRow);
    Dispatcher.UIThread.RunJobs();
    Save("client-people");
    withPeople.ContactPerson = "Pieter J van Wyk";
    withPeople.SaveCommand.Execute(null);
    Check("Changing the contact person on the client changes them in Contacts", services.Contacts.Get(pieter.Id)!.Name == "Pieter J van Wyk");

    // Contacts in Mail: groups in To, a sender added with a right-click, and a person's mail and documents.
    var bookWithGroups = Task.Run(() => services.AddressBookAsync()).GetAwaiter().GetResult();
    var hosting = Webignite.Core.Mail.AddressBook.Match(bookWithGroups, "hosting").FirstOrDefault();
    Check("Typing a group in To offers the whole group", hosting is { IsGroup: true } && hosting.Members!.Any(m => m.Address == "accounts@bosveldbuilders.co.za"));
    Open(Page.Mail);
    var menuMail = vm.Mail.Messages.FirstOrDefault(m => m.Summary.From?.Address is { } a && Webignite.Core.Clients.ContactRules.WithEmail(services.Contacts.List(), a) is null);
    if (menuMail is not null)
    {
        Wait(vm.Mail.Select(menuMail), "The mail opened");
        var add = vm.Mail.MessageMenu().FirstOrDefault(i => i.Header.EndsWith("to Contacts", StringComparison.Ordinal));
        Check("A mail's right-click offers to add the sender to Contacts", add is not null);
        add?.Run?.Invoke().GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
        Check("Adding the sender opens them in Contacts", vm.Current.Key == Page.Contacts && people.IsOpen
            && Webignite.Core.Clients.ContactRules.WithEmail(services.Contacts.List(), menuMail.Summary.From!.Address) is not null);
        Check("Once they're in Contacts, the right-click opens them there", vm.Mail.MessageMenu().Any(i => i.Header.EndsWith("in Contacts", StringComparison.Ordinal)));
    }
    Open(Page.Contacts);
    people.OpenPerson(services.Contacts.Get(pieter.Id)!.Id);
    Wait(people.TheirMailLoaded, "Their mail was read");
    Check("A person shows their client's quotes and invoices", people.HasTheirDocuments);
    // Import and export: a phone's vCard brings in who isn't here yet; everyone goes out as CSV.
    var cardFile = Path.Combine(data, "phone-contacts.vcf");
    File.WriteAllText(cardFile, "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Sipho Ndlovu\r\nORG:Fynbos Physio\r\nEMAIL:sipho@fynbosphysio.co.za\r\nTEL;TYPE=CELL:072 555 0123\r\nEND:VCARD\r\n"
        + "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Marlene B\r\nEMAIL:accounts@bosveldbuilders.co.za\r\nEND:VCARD\r\n");
    Wait(people.ImportAsync(cardFile), "The vCard was imported");
    Check("Importing a vCard adds who isn't here yet, with their client", people.FileMessage == "Added 1 person. 1 was in Contacts already."
        && services.Contacts.List().Any(p => p.Name == "Sipho Ndlovu" && p.ClientId == services.Clients.Find("Fynbos Physio")?.Id));
    var csvFile = Path.Combine(data, "contacts.csv");
    Wait(people.ExportAsync(csvFile, ContactFileKind.Csv, groupOnly: false), "Contacts were exported");
    Check("Exporting writes everyone to a CSV with their client", File.ReadAllLines(csvFile).Length == services.Contacts.List().Count + 1
        && File.ReadAllText(csvFile).Contains("Sipho Ndlovu,,Fynbos Physio", StringComparison.Ordinal));
    people.OpenPerson(services.Contacts.Get(pieter.Id)!.Id);
    Wait(people.TheirMailLoaded, "Their mail was read again");
    Wait(people.MailedOftenLoaded, "People mailed often were worked out");
    Dispatcher.UIThread.RunJobs();
    Check("The group filter still shows Everyone after the list is read again",
        window.GetVisualDescendants().OfType<Avalonia.Controls.ComboBox>().Any(c => c.Name == "GroupBox" && Equals(c.SelectedItem, ContactsViewModel.AllGroups)));
    Save("contacts-person");
    people.CloseCommand.Execute(null);

    // Writing a mail: the Contacts button adds a group to To, a template fills an empty mail and keeps the signature.
    Open(Page.Mail);
    vm.Mail.WriteCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    var writer = vm.Mail.Composer!;
    writer.OpenPeople();
    writer.PeopleSearch = "hosting";
    var hostingRow = writer.PeopleToPick.FirstOrDefault(r => r.Name == "Hosting clients");
    hostingRow?.AddToCommand.Execute(null);
    Check("The Contacts button adds a whole group to To", hostingRow is not null && writer.To.Contains("accounts@bosveldbuilders.co.za", StringComparison.Ordinal));
    writer.PeopleSearch = "";
    services.Settings.MailTemplates =
    [
        Webignite.Core.Mail.MailTemplates.From("Monthly maintenance report", "", "", "Your {client} site this month",
            Webignite.Core.Writing.RichText.FromText("Hi {name},\n\nHere is what we did on your site this month:\n\n- Plugins and WordPress updated\n- Backups checked\n- Uptime 99.9%"), "mt1"),
    ];
    var inserted = writer.UseTemplate(services.Settings.MailTemplates[0]);
    Dispatcher.UIThread.RunJobs();
    Check("A template fills an empty mail with the client filled in and the signature under it", inserted is null
        && writer.Subject == "Your Bosveld Builders site this month" && writer.Body.StartsWith("Hi Marlene,", StringComparison.Ordinal) && writer.Document.HasSignature);
    Save("mail-template");
    var peopleButton = window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().FirstOrDefault(b => b.Name == "PeopleButton");
    peopleButton?.Flyout?.ShowAt(peopleButton);
    Dispatcher.UIThread.RunJobs();
    Save("mail-contacts-button");
    peopleButton?.Flyout?.Hide();
    writer.StartSaveTemplateCommand.Execute(null);
    writer.TemplateName = "Monthly maintenance report";
    writer.SaveTemplateCommand.Execute(null);
    Check("A template's name must be its own", writer.HasTemplateProblem && services.Settings.MailTemplates.Count == 1);
    writer.TemplateName = "Monthly report, short";
    writer.SaveTemplateCommand.Execute(null);
    Check("Saving a mail as a template keeps it without the signature", services.Settings.MailTemplates.Count == 2
        && !services.Settings.MailTemplates[1].Html.Contains("Webignite", StringComparison.OrdinalIgnoreCase) && !writer.AskingTemplateName);
    services.Settings.MailTemplates = [];
    writer.DiscardCommand.Execute(null);

    // Spell check: Windows' checker stands in for a small word list here.
    services.Spelling = new StandInSpelling();
    vm.Mail.WriteCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    var speller = vm.Mail.Composer!;
    var spellEditor = window.GetVisualDescendants().OfType<Webignite.Desktop.Controls.RichTextEditor>().First();
    spellEditor.Spelling = services.Spelling;
    spellEditor.StartTyping();
    spellEditor.InsertDocument(Webignite.Core.Writing.RichText.FromText("Thanks for teh paymnet"));
    Dispatcher.UIThread.RunJobs();
    var spellMenu = spellEditor.SpellingMenu().OfType<Avalonia.Controls.MenuItem>().ToList();
    Check("A misspelt word offers the right spelling, adding it and ignoring it", spellMenu.Count == 3 && Equals(spellMenu[0].Header, "payment")
        && Equals(spellMenu[1].Header, "Add \"paymnet\" to the dictionary"));
    Save("mail-spelling");
    spellMenu[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    Check("Picking a suggestion swaps the misspelt word", speller.Body.StartsWith("Thanks for teh payment", StringComparison.Ordinal));
    spellEditor.Spelling = null;
    services.Spelling = null;
    speller.DiscardCommand.Execute(null);

    // Projects: a shared website build with headings and tasks, and a personal one.
    {
        var team = services.Team();
        var site = services.Projects.Add(new Webignite.Core.Projects.Project { Id = "snap-site", Name = "Bosveld Builders website", Shared = true, ClientId = bosveldId,
            Due = new DateOnly(2026, 12, 4) }).Saved!;
        services.Projects.Add(new Webignite.Core.Projects.Project { Id = "snap-seo", Name = "Webignite SEO month" });
        var design = services.Projects.AddHeading(site.Id, "Design").Saved!;
        var build = services.Projects.AddHeading(site.Id, "Build").Saved!;
        services.Projects.AddHeading(site.Id, "Launch");
        Webignite.Core.Projects.ProjectTask Add(string title, string? heading, string? who = null, DateOnly? due = null, bool done = false, string? parent = null) =>
            services.Projects.AddTask(new Webignite.Core.Projects.ProjectTask { Id = Webignite.Core.Projects.ProjectRules.NewId(), ProjectId = site.Id, HeadingId = heading,
                Title = title, AssignedTo = who, Due = due, Done = done, ParentId = parent }).Saved!;
        Add("Get the logo and photos from Pieter", null, team.Me!.Id, new DateOnly(2026, 11, 10));
        var wire = Add("Wireframes for the home page", design.Id, team.Me.Id, new DateOnly(2026, 11, 12), done: true);
        Add("Mobile layout", design.Id, team.Me.Id, done: true, parent: wire.Id);
        Add("Tablet layout", design.Id, parent: wire.Id);
        Add("Colours and fonts", design.Id, team.Partner!.Id, new DateOnly(2026, 11, 14));
        var pages = Add("Build the five pages", build.Id, team.Partner.Id, new DateOnly(2026, 11, 26));
        Add("Contact form to info@", build.Id);
        Open(Page.Projects);
        var projectsPage = (ProjectsViewModel)vm[Page.Projects].Main.Page;
        projectsPage.OpenProject(site.Id);
        Dispatcher.UIThread.RunJobs();
        Check("The list of projects tucks away while a project is open; the Planner sidebar lists them", !projectsPage.Panels["list"].IsShown);
        vm.SelectedNav = vm[Page.Projects];
        Dispatcher.UIThread.RunJobs();
        Check("Projects in the sidebar goes back to all projects, with the list showing", projectsPage.Showing is null && projectsPage.Panels["list"].IsShown);
        Save("projects-all");
        projectsPage.OpenProject(site.Id);
        Dispatcher.UIThread.RunJobs();
        Check("Projects lists shared and personal projects", projectsPage.SharedProjects.Count == 1 && projectsPage.PersonalProjects.Count == 1);
        Check("An open project shows its headings with their tasks", projectsPage.Rows.OfType<SectionRow>().Select(r => r.Name).SequenceEqual(["No heading", "Design", "Build", "Launch"])
            && projectsPage.Rows.OfType<TaskRow>().Count() == 5 && projectsPage.Rows.OfType<TaskRow>().First(r => r.Title.StartsWith("Wireframes", StringComparison.Ordinal)).Subtasks == "1 of 2");
        var contactForm = projectsPage.Rows.OfType<TaskRow>().First(r => r.Title.StartsWith("Contact form", StringComparison.Ordinal));
        contactForm.AssignedTo = contactForm.People[1];
        contactForm.Due = new DateTime(2026, 11, 27);
        Check("Giving a task to someone and dating it in the list saves it, stamped with who did it",
            services.Projects.GetTask(contactForm.Task.Id) is { AssignedTo: { } by, Due: { Day: 27 } } saved && by == team.Me.Id && saved.Fields["assigned"].By == team.Me.Id);
        projectsPage.Drop(projectsPage.Rows.OfType<TaskRow>().First(r => r.Task.Id == contactForm.Task.Id), projectsPage.Rows.OfType<TaskRow>().First(r => r.Task.Id == pages.Id), below: false);
        Check("Dragging a task above another puts it there", services.Projects.AllTasks(site.Id).Where(t => t.HeadingId == build.Id).Select(t => t.Title).First().StartsWith("Contact form", StringComparison.Ordinal));
        projectsPage.MoveTo(projectsPage.Rows.OfType<TaskRow>().First(r => r.Task.Id == contactForm.Task.Id), projectsPage.Sections.First(r => r.Name == "Launch"));
        Check("Moving a task to another heading from its menu", services.Projects.GetTask(contactForm.Task.Id)!.HeadingId == projectsPage.Sections.First(r => r.Name == "Launch").Heading!.Id);
        var adding = projectsPage.Rows.OfType<AddTaskRow>().First(r => r.Section.Name == "Launch");
        adding.Title = "Point the domain at the new site";
        adding.AddCommand.Execute(null);
        Check("A task typed under a heading is added there", projectsPage.Rows.OfType<TaskRow>().Any(r => r.Title == "Point the domain at the new site" && r.Section?.Name == "Launch"));
        Dispatcher.UIThread.RunJobs();
        Save("projects");
SaveLooks("projects");
        projectsPage.EditName = "Bosveld Builders new website";
        projectsPage.Keep();
        Check("Renaming a project is saved once typing stops", services.Projects.Get(site.Id)!.Name == "Bosveld Builders new website" && projectsPage.TabTitle == "Bosveld Builders new website");

        // The task page: notes, subtasks, links, and who made and changed it.
        projectsPage.OpenTaskById(wire.Id);
        Dispatcher.UIThread.RunJobs();
        var taskPage = projectsPage.Detail!;
        Check("Opening a task shows its subtasks and who made it", taskPage.Subtasks.Count == 2 && taskPage.History.StartsWith("Made by Me on", StringComparison.Ordinal)
            && projectsPage.Panels["task"].IsShown);
        taskPage.NewSubtask = "Desktop layout";
        taskPage.AddSubtaskCommand.Execute(null);
        Check("A subtask added on the task page counts in the list", taskPage.Subtasks.Count == 3
            && projectsPage.Rows.OfType<TaskRow>().First(r => r.Task.Id == wire.Id).Subtasks == "1 of 3");
        taskPage.Subtasks.First(r => r.Title == "Tablet layout").Done = true;
        Check("Ticking a subtask off updates its task's count", projectsPage.Rows.OfType<TaskRow>().First(r => r.Task.Id == wire.Id).Subtasks == "2 of 3");
        taskPage.Notes = "Use the photos Pieter sent. Keep the home page short.";
        projectsPage.Keep();
        Check("A task's notes are saved once typing stops", services.Projects.GetTask(wire.Id)!.Notes.StartsWith("Use the photos", StringComparison.Ordinal));
        Check("The Subtasks heading counts how many are done, as Asana's", taskPage.SubtaskTally == "2/3" && taskPage.ShowSubtaskBox);
        // A description with formatting keeps it, and its plain words stay for search and templates.
        var described = Webignite.Core.Writing.RichText.FromText("Use the photos Pieter sent.\n**Keep the home page short.**");
        taskPage.Description = described;
        taskPage.DescriptionRevision++;
        projectsPage.Keep();
        var withFormat = services.Projects.GetTask(wire.Id)!;
        Check("A description keeps its bold, with its plain words beside it", withFormat.NotesHtml.Contains("<b>", StringComparison.OrdinalIgnoreCase)
            && withFormat.Notes.Contains("Keep the home page short.", StringComparison.Ordinal));
        projectsPage.OpenTaskById(wire.Id);
        Dispatcher.UIThread.RunJobs();
        taskPage = projectsPage.Detail!;
        Check("Opened again, the description comes back formatted", taskPage.Description.Blocks.OfType<Webignite.Core.Writing.TextBlock>()
            .Any(b => b.Runs.Any(r => r.Format.Bold)));
        taskPage.PickClient(taskPage.Clients.First(c => c.Value?.Id == bosveldId));
        Check("A task can be about a client", services.Projects.GetTask(wire.Id)!.ClientId == bosveldId);
        if (taskPage.Documents.Skip(1).FirstOrDefault() is { } linkedDocument)
        {
            taskPage.PickDocument(linkedDocument);
            Check("A task can be about a quote or invoice", services.Projects.GetTask(wire.Id)!.DocumentId == linkedDocument.Value!.Id);
        }
        taskPage.MailSearch = "photos";
        Wait(taskPage.Searched, "Mail to link was found");
        Dispatcher.UIThread.RunJobs();
        if (taskPage.MailFound.FirstOrDefault() is { } hit)
        {
            hit.LinkCommand.Execute(null);
            Check("A mail found by searching can be linked to the task", services.Projects.GetTask(wire.Id)!.Mail?.Subject == hit.Mail.Subject && taskPage.HasMail);
        }
        Dispatcher.UIThread.RunJobs();
        Save("projects-task");
        // The due date's pop-up: a month to pick from, its time, and how it repeats. Drawn in a window of its own,
        // as pop-ups can't be drawn here.
        {
            taskPage.Due = new DateTime(2026, 11, 2);
            taskPage.Repeat = taskPage.Repeats.First(r => r.Value == Webignite.Core.Projects.TaskRepeat.Monthly);
            taskPage.RepeatOnWeekdayCommand.Execute(null);
            taskPage.DueTimeText = "14:00";
            Dispatcher.UIThread.RunJobs();
            var maintenance = services.Projects.GetTask(wire.Id)!;
            Check("A task can repeat on a weekday of the month, with a time, set from its due date",
                maintenance.Repeat == Webignite.Core.Projects.TaskRepeat.Monthly && maintenance.RepeatOn == new Webignite.Core.Projects.MonthDay(1, DayOfWeek.Monday)
                && maintenance.DueTime == new TimeOnly(14, 0) && taskPage.RepeatWords == "Every month on the 1st Monday" && taskPage.DueNote.Contains("every month on the 1st monday", StringComparison.Ordinal));
            taskPage.RepeatEvery = 2;
            Check("And every so many: every 2 months", services.Projects.GetTask(wire.Id)!.RepeatEvery == 2 && taskPage.RepeatUnit == "months");
            taskPage.Due = new DateTime(2026, 11, 12);
            Check("Moved to another day, a weekday repeat follows it", services.Projects.GetTask(wire.Id)!.RepeatOn == new Webignite.Core.Projects.MonthDay(2, DayOfWeek.Thursday));
            taskPage.RepeatEvery = 1;
            taskPage.Due = new DateTime(2026, 11, 2);
            Dispatcher.UIThread.RunJobs();
            var dueBox = window.GetVisualDescendants().OfType<DateBox>().First(b => b.Extra is not null && b.IsEffectivelyVisible);
            var popUp = new Avalonia.Controls.Window
            {
                Width = 320, SizeToContent = Avalonia.Controls.SizeToContent.Height, Padding = new Avalonia.Thickness(16),
                Content = dueBox.Picture(),
            };
            popUp.Show();
            SaveWindow(popUp, "date-pop-up-task");
            popUp.Content = null;
            popUp.Close();
            var plain = new DateBox { SelectedDate = new DateTime(2026, 11, 12), Earliest = new DateTime(2026, 11, 5) };
            var plainWindow = new Avalonia.Controls.Window { Width = 320, SizeToContent = Avalonia.Controls.SizeToContent.Height, Padding = new Avalonia.Thickness(16), Content = plain.Picture() };
            plainWindow.Show();
            SaveWindow(plainWindow, "date-pop-up");
            plainWindow.Close();
            Save("projects-task-repeating");
            taskPage.Repeat = taskPage.Repeats.First(r => r.Value == Webignite.Core.Projects.TaskRepeat.None);
            taskPage.DueTimeText = "";
            Dispatcher.UIThread.RunJobs();
        }
        projectsPage.CloseTaskCommand.Execute(null);
        Check("Closing the task hides its pane", !projectsPage.HasTask && !projectsPage.Panels["task"].IsShown);

        // The board: headings as columns, cards dragged across.
        projectsPage.BoardViewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check("The board has a column per heading with its tasks as cards", projectsPage.IsBoard
            && projectsPage.Columns.Select(c => c.Section.Name).SequenceEqual(["No heading", "Design", "Build", "Launch"])
            && projectsPage.Columns.First(c => c.Section.Name == "Launch").Cards.Count == 2);
        var colours = projectsPage.Columns.SelectMany(c => c.Cards).First(r => r.Title == "Colours and fonts");
        projectsPage.Drop(colours, projectsPage.Columns.First(c => c.Section.Name == "Build"), below: false);
        Check("Dropping a card on a column moves it to the end of that heading",
            projectsPage.Columns.First(c => c.Section.Name == "Build").Cards.Last().Title == "Colours and fonts");
        var buildCard = projectsPage.Columns.First(c => c.Section.Name == "Build").Cards.Last();
        projectsPage.Drop(buildCard, projectsPage.Columns.First(c => c.Section.Name == "Build").Cards.First(), below: false);
        Check("Dropping a card above another moves it up the column",
            projectsPage.Columns.First(c => c.Section.Name == "Build").Cards.First().Title == "Colours and fonts");
        Check("The board is remembered for next time, for this project", projectsPage.Layout.Board
            && services.Settings.ProjectLayouts.Values.Any(l => l.Board));
        var boardLaunch = projectsPage.Columns.First(c => c.Section.Name == "Launch");
        Check("Each column shows how many tasks it has, by its name", boardLaunch.Section.Total == boardLaunch.Cards.Count.ToString(System.Globalization.CultureInfo.CurrentCulture));
        boardLaunch.Add.AtTop = true;
        boardLaunch.Add.IsTyping = true;
        boardLaunch.Add.Title = "Go-live checklist";
        boardLaunch.Add.AddCard();
        boardLaunch = projectsPage.Columns.First(c => c.Section.Name == "Launch");
        Check("The + at a column's top adds a card there and keeps the box open for the next",
            boardLaunch.Cards.First().Title == "Go-live checklist" && boardLaunch.Add.IsTypingTop);
        boardLaunch.Add.IsTyping = false;
        projectsPage.IsAddingColumn = true;
        projectsPage.NewHeadingName = "Aftercare";
        projectsPage.AddHeadingCommand.Execute(null);
        Check("The column at the end of the board adds a heading", projectsPage.Columns.Last().Section.Name == "Aftercare");
        projectsPage.IsAddingColumn = false;
        Check("A card shows a face, a due date and its counts at its foot",
            projectsPage.Columns.SelectMany(c => c.Cards).Any(c => c.HasCardFoot && c.HasWho));
        window.Width = 1900;
        Dispatcher.UIThread.RunJobs();
        Save("projects-board");
        window.Width = 1280;
        // Put the project back as it was for the checks after: no Aftercare heading, no extra card.
        services.Projects.DeleteHeading(projectsPage.Columns.Last().Section.Heading!.Id);
        services.Projects.DeleteTask(projectsPage.Columns.SelectMany(c => c.Cards).First(c => c.Title == "Go-live checklist").Task.Id);
        projectsPage.Refresh();
        projectsPage.ListViewCommand.Execute(null);

        // The Overview tab, as Asana's: description, key resources, milestones and status updates.
        var healthBefore = services.Projects.Get(site.Id)!.Health;
        projectsPage.OverviewViewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check("The Overview tab shows, and the task tools hide", projectsPage.IsOverview && !projectsPage.IsList && !projectsPage.ShowsTaskTools);
        projectsPage.OverviewPart.ProjectDescription = Webignite.Core.Writing.RichText.FromText("Six pages to start, with online booking for site visits in phase two. Pieter signs off each page before it goes live.");
        projectsPage.OverviewPart.ProjectDescriptionRevision++;
        projectsPage.Keep();
        Check("The project description is kept once typing stops", services.Projects.Get(site.Id)!.Notes.StartsWith("Six pages", StringComparison.Ordinal));
        projectsPage.OverviewPart.NewLinkUrl = "staging.bosveld.co.za";
        projectsPage.OverviewPart.NewLinkTitle = "Staging site";
        projectsPage.OverviewPart.AddLinkCommand.Execute(null);
        projectsPage.OverviewPart.NewLinkUrl = "figma.com/file/bosveld";
        projectsPage.OverviewPart.AddLinkCommand.Execute(null);
        Check("Key resources are added, a bare address made whole and named after its site", projectsPage.OverviewPart.Links.Count == 2
            && projectsPage.OverviewPart.Links[0].Url == "https://staging.bosveld.co.za" && projectsPage.OverviewPart.Links[1].Title == "figma.com");
        projectsPage.OverviewPart.NewMilestone = "Client sign-off";
        projectsPage.OverviewPart.AddMilestoneCommand.Execute(null);
        projectsPage.OverviewPart.SetMilestone(projectsPage.Rows.OfType<TaskRow>().First(r => r.Title == "Point the domain at the new site"), true);
        Check("Milestones show on the Overview, in their order when none has a date",
            projectsPage.OverviewPart.Milestones.Select(m => m.Title).SequenceEqual(["Point the domain at the new site", "Client sign-off"]));
        Check("A task marked as a milestone shows a diamond in the list", projectsPage.Rows.OfType<TaskRow>().Any(r => r.IsMilestone));
        projectsPage.OverviewPart.StartUpdateCommand.Execute(Webignite.Core.Projects.ProjectHealth.AtRisk);
        projectsPage.OverviewPart.UpdateText = "Home page is signed off. Waiting on photos from Pieter before the gallery can go in.";
        projectsPage.OverviewPart.PostUpdateCommand.Execute(null);
        Check("A status update posted goes on the Overview and sets the header's status",
            projectsPage.OverviewPart.Updates.Count == 1 && projectsPage.EditHealth == Webignite.Core.Projects.ProjectHealth.AtRisk && !projectsPage.OverviewPart.IsWritingUpdate);
        window.Width = 1900;
        Dispatcher.UIThread.RunJobs();
        Save("projects-overview");
        window.Width = 1280;
        // Put the project back for the checks after.
        services.Projects.DeleteTask(projectsPage.OverviewPart.Milestones.First(m => m.Title == "Client sign-off").Task.Id);
        services.Projects.ChangeTask(projectsPage.OverviewPart.Milestones.First().Task.Id, t => t with { Milestone = false });
        services.Projects.Change(site.Id, p => p with { Health = healthBefore, Updates = [], Links = [] });
        projectsPage.ListViewCommand.Execute(null);
        projectsPage.Refresh();

        // The Timeline tab, as Asana's: bars from start to due under their headings, dragged a day at a time.
        var datedBefore = services.Projects.AllTasks(site.Id).ToDictionary(t => t.Id, t => (t.Start, t.Due));
        var fivePages = services.Projects.AllTasks(site.Id).First(t => t.Title == "Build the five pages");
        services.Projects.ChangeTask(fivePages.Id, t => t with { Start = t.Due!.Value.AddDays(-8) });
        var coloursFirst = services.Projects.AllTasks(site.Id).First(t => t.Title == "Colours and fonts");
        services.Projects.AddDependency(fivePages.Id, coloursFirst.Id);
        services.Projects.ChangeTask(services.Projects.AllTasks(site.Id).First(t => t.Title == "Point the domain at the new site").Id, t => t with { Start = null, Due = null });
        projectsPage.TimelineViewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check("The Timeline tab shows each heading's dated tasks as bars, and the rest under Unscheduled", projectsPage.IsTimeline
            && projectsPage.TimelinePart.TimelineLanes.Count > 0 && projectsPage.TimelinePart.Unscheduled.Any(r => r.Title == "Point the domain at the new site")
            && projectsPage.TimelinePart.TimelineLanes.SelectMany(l => l.Bars).First(b => b.Title == "Build the five pages").Bar.Length == 9);
        var fiveBar = projectsPage.TimelinePart.TimelineLanes.SelectMany(l => l.Bars).First(b => b.Title == "Build the five pages");
        var fiveDue = fiveBar.Bar.Task.Due!.Value;
        projectsPage.TimelinePart.DragBar(fiveBar, 2, BarEdge.Move);
        Check("Dragging a bar moves its start and due date together", services.Projects.GetTask(fivePages.Id)!.Due == fiveDue.AddDays(2)
            && services.Projects.GetTask(fivePages.Id)!.Start == fiveDue.AddDays(-6));
        projectsPage.TimelinePart.DragBar(projectsPage.TimelinePart.TimelineLanes.SelectMany(l => l.Bars).First(b => b.Title == "Build the five pages"), 3, BarEdge.End);
        Check("Dragging a bar's end stretches it", services.Projects.GetTask(fivePages.Id)!.Due == fiveDue.AddDays(5));
        var domainTask = projectsPage.TimelinePart.Unscheduled.First(r => r.Title == "Point the domain at the new site");
        var launchDay = projectsPage.TimelinePart.TimelineDays.First(d => d.Day == fiveDue.AddDays(9));
        projectsPage.TimelinePart.Schedule(domainTask, launchDay);
        Check("A task with no date dropped on a day of the timeline is due then", services.Projects.GetTask(domainTask.Task.Id)!.Due == launchDay.Day
            && projectsPage.TimelinePart.Unscheduled.All(r => r.Title != "Point the domain at the new site"));
        window.Width = 1900;
        for (var tick = 0; tick < 10; tick++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(15);
        }
        var arrows = window.GetVisualDescendants().OfType<Avalonia.Controls.Canvas>().First(c => c.Name == "TimelineArrows");
        Check("The timeline draws an arrow from a task to the one waiting on it", arrows.Children.Count == 1);
        Save("projects-timeline");
        // Another page and back: the view is made again for bars already there, and still draws the arrow.
        Open(Page.Invoices);
        Dispatcher.UIThread.RunJobs();
        Open(Page.Projects);
        for (var tick = 0; tick < 10; tick++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(15);
        }
        var arrowsAgain = window.GetVisualDescendants().OfType<Avalonia.Controls.Canvas>().FirstOrDefault(c => c.Name == "TimelineArrows" && c.IsEffectivelyVisible);
        Check("Back on the timeline from another page, the arrow is still drawn", projectsPage.IsTimeline && arrowsAgain is { Children.Count: 1 });

        // The Calendar tab, as Asana's: a month of weeks with each task on its due date.
        projectsPage.CalendarViewCommand.Execute(null);
        while (projectsPage.TimelinePart.CalendarMonth.Month != fiveDue.Month || projectsPage.TimelinePart.CalendarMonth.Year != fiveDue.Year)
            projectsPage.TimelinePart.NextMonthCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check("The Calendar tab shows six weeks with tasks on their due dates", projectsPage.IsCalendar && projectsPage.TimelinePart.CalendarCells.Count == 42
            && projectsPage.TimelinePart.CalendarCells.Any(c => c.Tasks.Any(t => t.Title == "Point the domain at the new site")));
        var onCalendar = projectsPage.TimelinePart.CalendarCells.SelectMany(c => c.Tasks).First(t => t.Title == "Point the domain at the new site");
        projectsPage.TimelinePart.MoveToDay(onCalendar, launchDay.Day.AddDays(1));
        Check("A task dragged to another day of the calendar is due then", services.Projects.GetTask(domainTask.Task.Id)!.Due == launchDay.Day.AddDays(1));
        Check("Sort and Group hide on Timeline and Calendar", !projectsPage.ShowsSortGroup);
        Dispatcher.UIThread.RunJobs();
        Save("projects-calendar");
        window.Width = 1280;
        // The project pane dragged wide on a big screen, then the window on a small one: the panes close up to fit.
        projectsPage.ListViewCommand.Execute(null);
        projectsPage.Panels.Show("list", true);
        projectsPage.Panels.Show("task", true);
        projectsPage.Panels.Resized("project", 1700);
        for (var tick = 0; tick < 5; tick++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        var paneGrid = window.GetVisualDescendants().OfType<PanelGrid>().First(g => g.IsEffectivelyVisible && g.DataContext == projectsPage);
        Check("Panes dragged wide on a big screen fit a small window", paneGrid.Children.Where(c => c.IsVisible && PanelGrid.GetPane(c) is not null)
            .All(c => c.Bounds.Right <= paneGrid.Bounds.Width + 1 && c.Bounds.Width >= Webignite.Core.Layout.Panels.MinWidth - 1)
            && paneGrid.Children.Count(c => c.IsVisible && PanelGrid.GetPane(c) is not null) == 3);
        Save("projects-small-window");
        projectsPage.Panels.Reset();
        // Put the dates back for the checks after, and take the dependency off.
        services.Projects.RemoveDependency(fivePages.Id, coloursFirst.Id);
        foreach (var (id, (start, due)) in datedBefore)
            services.Projects.ChangeTask(id, t => t with { Start = start, Due = due });
        projectsPage.ListViewCommand.Execute(null);
        projectsPage.Refresh();

        // Claude or ChatGPT adds a task through the connector (another process, the same database): the open page
        // shows it at the next look, every 3 seconds, without the app being told. (The headless screen runs no timers.)
        {
            var connector = new Webignite.Services.AssistantPlanner(services.Store);
            var fromClaude = connector.AddTask(site.Id, "Check the contact form on a phone", assignee: "me");
            vm.Jobs.LookForAssistants();
            var seen = Until(() => projectsPage.Rows.OfType<TaskRow>().Any(r => r.Title == "Check the contact form on a phone"));
            Check("A task Claude adds through the connector shows on the open project by itself", seen);
            services.Projects.DeleteTask(fromClaude.Task.Id);
            projectsPage.Refresh();
        }

        // Typing in an open task while Claude changes the same task: neither is lost.
        {
            var connector = new Webignite.Services.AssistantPlanner(services.Store);
            var target = services.Projects.AllTasks(site.Id).First(t => t.ParentId is null && !t.Done);
            projectsPage.OpenTaskById(target.Id);
            Dispatcher.UIThread.RunJobs();
            projectsPage.Detail!.Title = target.Title + " (typed)";
            connector.UpdateTask(target.Id, notes: "Notes from Claude", priority: "high");
            vm.Jobs.LookForAssistants();
            Dispatcher.UIThread.RunJobs();
            Check("A change from Claude doesn't throw away what's being typed in the task", projectsPage.Detail?.Title == target.Title + " (typed)");
            projectsPage.Keep();
            var after = services.Projects.GetTask(target.Id)!;
            Check("Saving the typing keeps Claude's change to the same task", after.Title == target.Title + " (typed)" && after.Notes == "Notes from Claude"
                && after.Priority == Webignite.Core.Projects.TaskPriority.High);
            services.Projects.ChangeTask(target.Id, t => t with { Title = target.Title, Notes = target.Notes, NotesHtml = target.NotesHtml, Priority = target.Priority });
            projectsPage.CloseTaskCommand.Execute(null);
            projectsPage.Refresh();
        }

        // Dependencies, as Asana's: a task waits on others in its project, never round in a circle.
        window.Width = 1900;
        Dispatcher.UIThread.RunJobs();
        projectsPage.OpenTaskById(pages.Id);
        Dispatcher.UIThread.RunJobs();
        var colourTask = services.Projects.AllTasks(site.Id).First(t => t.Title == "Colours and fonts");
        projectsPage.Detail!.AddDependency(colourTask.Id);
        Dispatcher.UIThread.RunJobs();
        Check("A task can wait on another: it's in the pane, the other says what it holds up, and the list marks it",
            projectsPage.Detail!.WaitingOn.Any(w => w.Title == "Colours and fonts")
            && projectsPage.Rows.OfType<TaskRow>().Any(r => r.Task.Id == pages.Id && r.IsWaiting && r.WaitingTip == "Colours and fonts"));
        for (var tick = 0; tick < 20; tick++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(15);
        }
        Save("task-dependencies");
        window.Width = 1280;
        projectsPage.OpenTaskById(colourTask.Id);
        Check("The task held up says so, and can't wait on the one waiting on it", projectsPage.Detail!.BlockingText == "Holding up: Build the five pages"
            && projectsPage.Detail.DependencyChoices().All(t => t.Id != pages.Id));
        services.Projects.RemoveDependency(pages.Id, colourTask.Id);
        projectsPage.CloseTaskCommand.Execute(null);
        projectsPage.Refresh();

        // Custom fields, as Asana's: a choice with coloured options and a number, as list columns, card pills and pane rows.
        projectsPage.FieldsPart.NewFieldName = "Stage";
        projectsPage.FieldsPart.NewFieldKind = ProjectFieldsPart.FieldKinds.First(k => k.Value == Webignite.Core.Projects.FieldKind.Choice);
        projectsPage.FieldsPart.NewFieldOptions = "Not started, Doing, Waiting on client, Done";
        projectsPage.FieldsPart.AddFieldCommand.Execute(null);
        projectsPage.FieldsPart.NewFieldName = "Hours";
        projectsPage.FieldsPart.NewFieldKind = ProjectFieldsPart.FieldKinds.First(k => k.Value == Webignite.Core.Projects.FieldKind.Number);
        projectsPage.FieldsPart.AddFieldCommand.Execute(null);
        var stageField = services.Projects.Get(site.Id)!.CustomFields.First(f => f.Name == "Stage");
        var hoursField = services.Projects.Get(site.Id)!.CustomFields.First(f => f.Name == "Hours");
        projectsPage.FieldsPart.SetFieldValue(pages.Id, stageField.Id, "Doing");
        projectsPage.FieldsPart.SetFieldValue(pages.Id, hoursField.Id, "12.5");
        projectsPage.FieldsPart.SetFieldValue(colourTask.Id, stageField.Id, "Waiting on client");
        projectsPage.FieldsPart.SetFieldValue(colourTask.Id, hoursField.Id, "lots");
        Check("A number field turns down words and says why", projectsPage.Message.Contains("number", StringComparison.Ordinal));
        projectsPage.FieldsPart.SetFieldValue(colourTask.Id, hoursField.Id, "3");
        Check("Custom fields show as list columns with each task's value, choices as coloured pills",
            projectsPage.FieldsPart.FieldHeaders.Select(h => h.Name).SequenceEqual(["Stage", "Hours"])
            && projectsPage.Rows.OfType<TaskRow>().First(r => r.Task.Id == pages.Id).FieldCells is [{ IsPill: true, Text: "Doing" }, { Text: var hoursText }]
            && hoursText.Length > 0);
        window.Width = 1900;
        Dispatcher.UIThread.RunJobs();
        for (var tick = 0; tick < 20; tick++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(15);
        }
        Save("projects-fields");
        projectsPage.OpenTaskById(pages.Id);
        Check("The task pane has a row for each field, set as picked", projectsPage.Detail!.FieldEditors.Count == 2
            && projectsPage.Detail.FieldEditors[0].Picked?.Label == "Doing");
        projectsPage.CloseTaskCommand.Execute(null);
        projectsPage.BoardViewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check("Board cards show the choices as pills", projectsPage.Columns.SelectMany(c => c.Cards).First(c => c.Task.Id == pages.Id).Pills.Single().Text == "Doing");
        Save("projects-board-fields");
        window.Width = 1280;
        projectsPage.ListViewCommand.Execute(null);
        // Take the fields off again for the checks after.
        services.Projects.RemoveField(site.Id, stageField.Id);
        services.Projects.RemoveField(site.Id, hoursField.Id);
        projectsPage.Refresh();

        // Priority and tags: set on the task page, shown on rows, and filtered by.
        projectsPage.OpenTaskById(pages.Id);
        var tagged = projectsPage.Detail!;
        tagged.Priority = ProjectsViewModel.Priorities.First(p => p.Value == Webignite.Core.Projects.TaskPriority.High);
        tagged.TagsText = "Build, hosting";
        projectsPage.Keep();
        Check("A task's priority and tags are saved", services.Projects.GetTask(pages.Id) is { Priority: Webignite.Core.Projects.TaskPriority.High } withTags
            && withTags.Tags.SequenceEqual(["Build", "hosting"]));
        projectsPage.CloseTaskCommand.Execute(null);
        projectsPage.OpenTaskById(contactForm.Task.Id);
        projectsPage.Detail!.AddTagCommand.Execute("Build");
        Check("A tag used elsewhere in the project is added with a click", services.Projects.GetTask(contactForm.Task.Id)!.Tags.SequenceEqual(["Build"]));
        projectsPage.CloseTaskCommand.Execute(null);
        projectsPage.PickTagFilter(projectsPage.TagFilters.First(t => t.Value == "Build"));
        Check("Filtering by a tag shows only its tasks", projectsPage.Rows.OfType<TaskRow>().Count() == 2 && projectsPage.HasFilter);
        projectsPage.PickPriorityFilter(ProjectsViewModel.PriorityFilters.First(p => p.Value == Webignite.Core.Projects.TaskPriority.High));
        Check("Filtering by priority as well narrows it again", projectsPage.Rows.OfType<TaskRow>().Single().Task.Id == pages.Id);
        var filteredAdd = projectsPage.Rows.OfType<AddTaskRow>().First(r => r.Section.Name == "Build");
        filteredAdd.Title = "Speed test the pages";
        filteredAdd.AddCommand.Execute(null);
        Check("A task added while filtered takes the filter, so it stays in view",
            projectsPage.Rows.OfType<TaskRow>().Any(r => r.Title == "Speed test the pages" && r.IsHigh && r.Tags.Contains("Build")));
        projectsPage.ClearFilterCommand.Execute(null);
        projectsPage.PickTagFilter(projectsPage.TagFilters.First(t => t.Value == "Build"));
        Dispatcher.UIThread.RunJobs();
        Save("projects-tags");
        projectsPage.ClearFilterCommand.Execute(null);

        // My tasks: everything given to me, by when it's due; opened beside the list; the timer started from one.
        projectsPage.ShowMineCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var myTasks = projectsPage.MyRows.OfType<TaskRow>().ToList();
        Check("My tasks lists what's given to me across projects, grouped by when it's due", projectsPage.IsMyTasks && myTasks.Count > 0
            && myTasks.All(r => r.Task.AssignedTo == team.Me.Id && !r.Task.Done) && projectsPage.MyRows.OfType<MyGroupRow>().Any()
            && myTasks.All(r => r.ProjectName == "Bosveld Builders new website"));
        projectsPage.OpenTaskCommand.Execute(myTasks[0]);
        Check("A task opens beside My tasks", projectsPage.IsMyTasks && projectsPage.Detail?.Task.Id == myTasks[0].Task.Id);
        projectsPage.Detail!.StartTimerCommand.Execute(null);
        Check("The timer starts on a task, with its client", services.Timer.Running is { } timing
            && timing.Note.StartsWith(myTasks[0].Title, StringComparison.Ordinal) && timing.ClientId == bosveldId);
        services.Timer.Stop();
        // My own to-dos show in My tasks with what's given to me, as Asana's; the box adds one.
        projectsPage.CloseTaskCommand.Execute(null);
        projectsPage.MyTodosPart.NewTodo = "Renew the Karoo Coffee domain";
        projectsPage.MyTodosPart.AddTodoCommand.Execute(null);
        Check("My tasks lists my to-dos with the tasks given to me, and the box adds one",
            projectsPage.MyRows.OfType<TodoRow>().Any(r => r.Title == "Renew the Karoo Coffee domain")
            && projectsPage.MySummary.Contains("of your own", StringComparison.Ordinal));
        Check("There's no To-do page any more: to-dos live in My tasks", !Enum.GetNames<Page>().Contains("Todo"));
        window.Width = 1900;
        for (var tick = 0; tick < 10; tick++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(15);
        }
        Save("projects-my-tasks");
        window.Width = 1280;
        projectsPage.CloseTaskCommand.Execute(null);

        // My tasks with a due date show in Planner, for the week they're due.
        var dated = services.Projects.Mine(team.Me.Id).Where(t => t.Due is not null).OrderBy(t => t.Due).First();
        Open(Page.Planner);
        var planner = (PlannerViewModel)vm[Page.Planner].Main.Page;
        while (planner.Days[0].Date > dated.Due)
            planner.PreviousWeekCommand.Execute(null);
        while (planner.Days[^1].Date < dated.Due)
            planner.NextWeekCommand.Execute(null);
        Check("My dated project tasks show in Planner", planner.Days.SelectMany(d => d.Items).Any(i => i.Item.Kind == Webignite.Core.Calendar.AgendaKind.ProjectTask
            && i.Title.StartsWith(dated.Title, StringComparison.Ordinal)));
        planner.ThisWeekCommand.Execute(null);

        // A mail's right-click adds it to a project as a task, linked to the mail.
        Open(Page.Mail);
        if (vm.Mail.Messages.FirstOrDefault() is { } projectMail)
        {
            Wait(vm.Mail.Select(projectMail), "The mail opened");
            var addTo = vm.Mail.MessageMenu().FirstOrDefault(i => i.Header == "Add to a project");
            var into = addTo?.Items.FirstOrDefault(i => i.Header == "Bosveld Builders new website");
            into?.Run?.Invoke().GetAwaiter().GetResult();
            Check("A mail is added to a project as a task, with the mail linked", into is not null
                && services.Projects.AllTasks(site.Id).Any(t => t.Mail?.Uid == projectMail.Summary.Uid && t.AssignedTo == team.Me.Id));
        }

        // A repeating task ticked off comes back; a project kept as a template starts another.
        Open(Page.Projects);
        projectsPage.OpenProject(site.Id);
        var repeating = projectsPage.Rows.OfType<TaskRow>().First(r => r.Title == "Get the logo and photos from Pieter");
        projectsPage.OpenTaskById(repeating.Task.Id);
        projectsPage.Detail!.Repeat = ProjectsViewModel.Repeats.First(r => r.Value == Webignite.Core.Projects.TaskRepeat.Weekly);
        projectsPage.CloseTaskCommand.Execute(null);
        projectsPage.Rows.OfType<TaskRow>().First(r => r.Task.Id == repeating.Task.Id).Done = true;
        Check("Ticking off a weekly task makes the next one, a week later", projectsPage.Rows.OfType<TaskRow>().Any(r => r.Title == repeating.Title && !r.Done
            && r.Task.Due == repeating.Task.Due!.Value.AddDays(7) && r.Repeats));
        projectsPage.SaveAsTemplateCommand.Execute(null);
        Check("A project is kept as a template", projectsPage.Templates.Any(t => t.Name == "Bosveld Builders new website" && t.Headings.Count == 3));
        projectsPage.NewProjectName = "Karoo Coffee website";
        projectsPage.NewFromTemplate(projectsPage.Templates.First());
        Dispatcher.UIThread.RunJobs();
        Check("A new project from a template has its headings and tasks, none done or given to anyone", projectsPage.EditName == "Karoo Coffee website"
            && projectsPage.Sections.Select(x => x.Name).Skip(1).SequenceEqual(["Design", "Build", "Launch"])
            && projectsPage.Rows.OfType<TaskRow>().Any() && projectsPage.Rows.OfType<TaskRow>().All(r => !r.Done && r.Task.AssignedTo is null));
        Check("A new project from a template has no due date of its own", projectsPage.EditDue is null);
        Check("A ticked repeating task isn't copied into a template, only its next one",
            projectsPage.Rows.OfType<TaskRow>().Count(r => r.Title == repeating.Title) == 1);
        Save("projects-template");

        // The sidebar's Projects group: My tasks, each running project by name, All projects and Templates.
        var links = vm.Sidebar.PlannerItems.OfType<ProjectLink>().ToList();
        Check("The sidebar lists My tasks, each running project and Templates under Projects",
            links.Select(l => l.Title).SequenceEqual(["My tasks", "Bosveld Builders new website", "Webignite SEO month", "Karoo Coffee website", "Templates"])
            || (links.First().Kind == ProjectLinkKind.MyTasks && links.Last().Kind == ProjectLinkKind.Templates
                && links.Any(l => l.Title == "Karoo Coffee website") && links.Any(l => l.Title == "Bosveld Builders new website")));
        Check("The project open on the page is the one marked in the sidebar", vm.SelectedNav is ProjectLink { Title: "Karoo Coffee website" });
        Save("projects-sidebar");
        vm.SelectedNav = links.First(l => l.Title == "Bosveld Builders new website");
        Dispatcher.UIThread.RunJobs();
        Check("A project in the sidebar opens it", projectsPage.EditName == "Bosveld Builders new website" && vm.SelectedNav is ProjectLink { Title: "Bosveld Builders new website" });
        vm.SelectedNav = links.First(l => l.Kind == ProjectLinkKind.MyTasks);
        Dispatcher.UIThread.RunJobs();
        Check("My tasks in the sidebar shows my tasks", projectsPage.IsMyTasks && vm.SelectedNav is ProjectLink { Kind: ProjectLinkKind.MyTasks });
        vm.SelectedNav = links.First(l => l.Kind == ProjectLinkKind.Templates);
        Dispatcher.UIThread.RunJobs();
        Check("Templates in the sidebar opens the gallery: my templates and the ones that come with the app", projectsPage.GalleryPart.IsGallery
            && projectsPage.GalleryPart.GalleryCards.Count == projectsPage.Templates.Count + Webignite.Core.Projects.BuiltInTemplates.All.Count
            && vm.SelectedNav is ProjectLink { Kind: ProjectLinkKind.Templates });
        window.Width = 1920;
        window.Height = 1080;
        Dispatcher.UIThread.RunJobs();
        Save("template-gallery");
        projectsPage.GalleryPart.ShowCategory(Webignite.Core.Projects.BuiltInTemplates.Marketing);
        Check("A category shows only its templates, its chip picked", projectsPage.GalleryPart.GalleryCards.All(c => c.Category == "Marketing")
            && projectsPage.GalleryPart.GalleryChips.Single(c => c.IsOn).Name == "Marketing");
        projectsPage.GalleryPart.ShowCategory(ProjectGalleryPart.AllTemplates);
        projectsPage.GalleryPart.GalleryCards.First(c => c.Name == "Web design process").OpenCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check("A template opens to show its headings and tasks, ready to name", projectsPage.GalleryPart.IsPreviewing && projectsPage.GalleryPart.PreviewHeadings.Count == 5
            && projectsPage.GalleryPart.PreviewName == "Web design process");
        Save("template-preview");
        projectsPage.GalleryPart.PreviewName = "Fynbos Physio website";
        projectsPage.GalleryPart.UseTemplateCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var fromGallery = services.Projects.Projects().First(p => p.Name == "Fynbos Physio website");
        Check("Use template makes the project with its headings, tasks, colour and icon, and opens it", projectsPage.EditName == "Fynbos Physio website"
            && services.Projects.Headings(fromGallery.Id).Count == 5 && services.Projects.AllTasks(fromGallery.Id).Count > 15
            && fromGallery is { Icon: "PageLayout", Colour: "Blue" });
        services.Projects.Delete(fromGallery.Id);
        window.Width = 1280;
        window.Height = 860;
        Dispatcher.UIThread.RunJobs();
        vm.SelectedNav = vm[Page.Projects];
        Open(Page.Overview);
        Check("Another page takes the mark off the project entries", vm.SelectedNav is Module { Key: Page.Overview });

        // Planner: its own sidebar, entered from the main one, with Home first and a way back.
        Check("The main sidebar has a Planner entry and no Projects or Planning groups",
            vm.Sidebar.Items.OfType<SectionLink>().Any(l => l.Title == "Planner")
            && !vm.Sidebar.Items.OfType<Module>().Any(m => m.InPlanner)
            && !vm.Sidebar.Items.OfType<NavGroup>().Any(g => g.Key is "projects" or "planning"));
        vm.SelectedNav = vm.Sidebar.Items.OfType<SectionLink>().First();
        Dispatcher.UIThread.RunJobs();
        var homePage = (PlannerHomeViewModel)vm.Current.Page;
        Check("Clicking Planner opens Home and swaps the sidebar for Planner's", vm.Sidebar.IsPlanner && vm.Selected.Key == Page.Home
            && vm.Sidebar.PlannerItems.First() is SidebarBack
            && vm.Sidebar.PlannerItems.OfType<Module>().Select(m => m.Title).SequenceEqual(["Home", "Inbox", "Projects", "Calendar", "Week", "Diary", "Time"])
            && vm.Sidebar.PlannerItems.OfType<ProjectLink>().Any(l => l.Kind == ProjectLinkKind.MyTasks));
        // Back to the main sidebar, then a real click on Planner again: it goes straight back in.
        vm.SelectedNav = vm.Sidebar.PlannerBack;
        Dispatcher.UIThread.RunJobs();
        var mainNavList = window.GetVisualDescendants().OfType<Avalonia.Controls.ListBox>().First(l => l.Name == "Nav");
        var plannerRow = mainNavList.ContainerFromItem(vm.Sidebar.Items.OfType<SectionLink>().First())!;
        var plannerAt = plannerRow.TranslatePoint(new Point(plannerRow.Bounds.Width / 2, plannerRow.Bounds.Height / 2), window)!.Value;
        Check("After the back arrow, Planner is still marked in the main sidebar", !vm.Sidebar.IsPlanner && vm.SelectedNav is SectionLink);
        window.MouseDown(plannerAt, Avalonia.Input.MouseButton.Left);
        window.MouseUp(plannerAt, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Check("Clicking Planner again goes back into it, on the Planner page that was open", vm.Sidebar.IsPlanner && vm.Selected.Key == Page.Home);
        Check("Home greets, counts the week and lists my tasks and recent projects", homePage.Greeting.Contains(services.Me().Name, StringComparison.Ordinal)
            && homePage.CompletedText.EndsWith("completed", StringComparison.Ordinal)
            && homePage.Projects.OfType<HomeProjectTile>().Any() && homePage.Projects.First() is HomeCreateTile);
        homePage.ShowTabCommand.Execute(HomeTab.Completed);
        Check("Home's Completed tab shows tasks I ticked off lately", homePage.IsCompleted && homePage.Tasks.Count > 0);
        homePage.ShowTabCommand.Execute(HomeTab.Upcoming);
        Check("Home's My tasks has my to-dos too, as the My tasks page does", homePage.Tasks.Any(t => t.Item.Todo is not null) && homePage.Tasks.Any(t => t.Item.Task is not null));
        window.Width = 1920;
        window.Height = 1080;
        Dispatcher.UIThread.RunJobs();
        Save("planner-home");
SaveLooks("planner-home");
        // The same with the sidebar folded to icons, wide and narrow: the cards share the width and never overlap.
        vm.Sidebar.Toggle();
        Dispatcher.UIThread.RunJobs();
        Save("planner-home-icons");
        {
            // Each card starts where its box does and the second starts after the first: no card hangs over another.
            var cards = window.GetVisualDescendants().OfType<PlannerHomeView>().First().GetVisualDescendants()
                .OfType<Avalonia.Controls.Border>().Where(b => b.Classes.Contains("card")).ToList();
            var lefts = cards.Select(c => c.TranslatePoint(default, window)!.Value.X).ToList();
            Check("Home's cards sit side by side with the sidebar folded, none cut off or over another",
                cards.Count == 2 && cards.All(c => c.Bounds.X == 0) && lefts[1] >= lefts[0] + cards[0].Bounds.Width);
        }
        window.Width = 1280;
        Dispatcher.UIThread.RunJobs();
        Save("planner-home-icons-narrow");
        window.Width = 1920;
        vm.Sidebar.Toggle();
        vm.Sidebar.Toggle();
        Dispatcher.UIThread.RunJobs();

        // Home's widgets, as Asana's Customise: add a notepad, people, status updates and milestones; resize and move them.
        Check("Home starts with My tasks and Projects", homePage.Widgets.Select(w => w.Kind).SequenceEqual([Webignite.Core.Projects.HomeWidgetKind.MyTasks, Webignite.Core.Projects.HomeWidgetKind.Projects]));
        var homeHealth = services.Projects.Get(site.Id)!.Health;
        services.Projects.PostStatus(site.Id, Webignite.Core.Projects.ProjectHealth.AtRisk, "Waiting on photos from Pieter before the gallery goes in.");
        var launchMilestone = services.Projects.AllTasks(site.Id).First(t => t.Title == "Contact form to info@");
        services.Projects.ChangeTask(launchMilestone.Id, t => t with { Milestone = true });
        foreach (var kind in new[] { Webignite.Core.Projects.HomeWidgetKind.People, Webignite.Core.Projects.HomeWidgetKind.StatusUpdates,
            Webignite.Core.Projects.HomeWidgetKind.Milestones, Webignite.Core.Projects.HomeWidgetKind.Notepad })
            homePage.WidgetChoices.First(c => c.Kind == kind).IsOn = true;
        homePage.Notepad = "Call Pieter after 10 about the photos.\nQuote for the Karoo shop: R 8 500.";
        homePage.KeepNotepad();
        homePage.Refresh();
        Check("Customise adds widgets, kept with Home, and the notepad keeps its words",
            homePage.Widgets.Count == 6 && services.Settings.Home.Widgets!.Count == 6 && services.Settings.Home.Notepad.StartsWith("Call Pieter", StringComparison.Ordinal));
        Check("People, status updates and milestones show what's going on", homePage.People.Count == 2
            && homePage.StatusUpdates.Any(u => u.Text.StartsWith("Waiting on photos", StringComparison.Ordinal))
            && homePage.Milestones.Any(m => m.Title == "Contact form to info@"));
        homePage.Widgets.First(w => w.Kind == Webignite.Core.Projects.HomeWidgetKind.Notepad).ResizeCommand.Execute(null);
        homePage.MoveWidget(homePage.Widgets.First(w => w.Kind == Webignite.Core.Projects.HomeWidgetKind.StatusUpdates),
            homePage.Widgets.First(w => w.Kind == Webignite.Core.Projects.HomeWidgetKind.MyTasks), after: true);
        Check("A widget goes full width from its menu, and drags after another",
            homePage.Widgets.First(w => w.Kind == Webignite.Core.Projects.HomeWidgetKind.Notepad).Full
            && homePage.Widgets[1].Kind == Webignite.Core.Projects.HomeWidgetKind.StatusUpdates);
        window.Height = 1600;
        for (var tick = 0; tick < 10; tick++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(15);
        }
        Save("planner-home-widgets");
        window.Height = 1080;
        homePage.Widgets.First(w => w.Kind == Webignite.Core.Projects.HomeWidgetKind.People).RemoveCommand.Execute(null);
        Check("A widget comes off Home from its menu", homePage.Widgets.All(w => w.Kind != Webignite.Core.Projects.HomeWidgetKind.People)
            && !homePage.WidgetChoices.First(c => c.Kind == Webignite.Core.Projects.HomeWidgetKind.People).IsOn);
        // Back to Home as it started, for the pictures after.
        services.Settings.Home = services.Settings.Home with { Widgets = null };
        services.Projects.ChangeTask(launchMilestone.Id, t => t with { Milestone = false });
        services.Projects.Change(site.Id, p => p with { Health = homeHealth, Updates = [] });
        homePage.Refresh();
        Open(Page.Planner);
        Check("The week planner is called Week inside Planner", vm.Selected.Title == "Week" && vm.Sidebar.IsPlanner);
        Check("Week's list without a day has project tasks given to me as well as to-dos",
            ((PlannerViewModel)vm.Current.Page).Unplanned.Any(u => u.About == "To-do") && ((PlannerViewModel)vm.Current.Page).Unplanned.Any(u => u.About != "To-do"));
        {
            // A day before the task's start can't be its due date: Week says so instead of the pick quietly not taking.
            var weekPage = (PlannerViewModel)vm.Current.Page;
            var undated = weekPage.Unplanned.First(u => u.About != "To-do");
            var undatedTask = services.Projects.Mine(services.Me().Id).First(t => t.Due is null && t.Title == undated.Title);
            services.Projects.ChangeTask(undatedTask.Id, t => t with { Start = DateOnly.FromDateTime(DateTime.Today).AddDays(60) });
            weekPage.Refresh();
            var again = weekPage.Unplanned.First(u => u.Title == undated.Title && u.About != "To-do");
            again.PlanFor = again.Days[0];
            Check("Week shows why a day picked for a task didn't take", weekPage.Message.Contains("before the start date", StringComparison.Ordinal)
                && services.Projects.GetTask(undatedTask.Id)!.Due is null);
            services.Projects.ChangeTask(undatedTask.Id, t => t with { Start = undatedTask.Start });
            weekPage.Refresh();
        }
        vm.SelectedNav = vm.Sidebar.PlannerBack;
        Dispatcher.UIThread.RunJobs();
        Check("The arrow at the top goes back to the main sidebar, leaving the page as it was", !vm.Sidebar.IsPlanner && vm.Selected.Key == Page.Planner
            && vm.SelectedNav is SectionLink);
        Save("planner-back");
        window.Width = 1280;
        window.Height = 860;
        Open(Page.Overview);
        Check("Another page keeps the main sidebar", !vm.Sidebar.IsPlanner);

        // A project's header as Asana's: its coloured square, a star and how it's going.
        projectsPage.OpenProject(site.Id);
        Open(Page.Projects);
        projectsPage.PickColourCommand.Execute("Aqua");
        projectsPage.PickIconCommand.Execute("Star");
        projectsPage.SetHealthCommand.Execute(Webignite.Core.Projects.ProjectHealth.AtRisk);
        var webignite = services.Projects.Projects().First(p => p.Name == "Webignite SEO month");
        projectsPage.OpenProject(webignite.Id);
        projectsPage.ToggleFavouriteCommand.Execute(null);
        projectsPage.OpenProject(site.Id);
        Dispatcher.UIThread.RunJobs();
        var stored = services.Projects.Get(site.Id)!;
        Check("A project keeps the colour, icon and status picked in its header", stored is { Colour: "Aqua", Icon: "Star", Health: Webignite.Core.Projects.ProjectHealth.AtRisk }
            && projectsPage.HealthText == "At risk");
        var projectLinks = vm.Sidebar.PlannerItems.OfType<ProjectLink>().Where(l => l.Kind == ProjectLinkKind.Project).ToList();
        Check("The sidebar shows each project's coloured square, starred ones first", projectLinks.First().Title == "Webignite SEO month"
            && projectLinks.Single(l => l.Id == site.Id) is { TileColour: "Aqua", TileIcon: "Star", HasTile: true });
        window.Width = 1920;
        window.Height = 1080;
        Dispatcher.UIThread.RunJobs();
        Save("project-header");

        // Drag and drop, with the mouse as a person would: press, move, let go. A press without a move still clicks.
        {
            Point Centre(Avalonia.Controls.Control c, double up = 0.5) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height * up), window) ?? default;
            void Press(Point at) => window.MouseDown(at, Avalonia.Input.MouseButton.Left);
            void MoveTo(Point from, Point to)
            {
                for (var step = 1; step <= 6; step++)
                    window.MouseMove(new Point(from.X + (to.X - from.X) * step / 6, from.Y + (to.Y - from.Y) * step / 6), Avalonia.Input.RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
            }
            void Let(Point at)
            {
                window.MouseUp(at, Avalonia.Input.MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }
            void DragOnto(Avalonia.Controls.Control from, Avalonia.Controls.Control to, double up)
            {
                var start = Centre(from);
                var end = Centre(to, up);
                Press(start);
                MoveTo(start, end);
                Let(end);
            }
            var plannerList = window.GetVisualDescendants().OfType<Avalonia.Controls.ListBox>().First(l => l.Name == "PlannerNav");
            var mainList = window.GetVisualDescendants().OfType<Avalonia.Controls.ListBox>().First(l => l.Name == "Nav");
            Avalonia.Controls.Control Row(Avalonia.Controls.ItemsControl list, object item) => list.ContainerFromItem(item)!;
            List<string> ProjectTitles() => [.. vm.Sidebar.PlannerItems.OfType<ProjectLink>().Where(l => l.Kind == ProjectLinkKind.Project).Select(l => l.Title)];

            var dragKaroo = vm.Sidebar.PlannerItems.OfType<ProjectLink>().First(l => l.Title == "Karoo Coffee website");
            var first = vm.Sidebar.PlannerItems.OfType<ProjectLink>().First(l => l.Kind == ProjectLinkKind.Project);
            var start = Centre(Row(plannerList, dragKaroo));
            Press(start);
            MoveTo(start, Centre(Row(plannerList, first), 0.2));
            Save("drag-sidebar-project");
            Let(Centre(Row(plannerList, first), 0.2));
            Check("A project dragged to the top of Work lands there, starred like its new neighbours", ProjectTitles()[0] == "Karoo Coffee website"
                && services.Projects.Projects().First(p => p.Name == "Karoo Coffee website").Favourite);

            var time = vm.Sidebar.PlannerItems.OfType<Module>().First(m => m.Key == Page.Time);
            var week = vm.Sidebar.PlannerItems.OfType<Module>().First(m => m.Key == Page.Planner);
            DragOnto(Row(plannerList, time), Row(plannerList, week), 0.2);
            var planKeys = vm.Sidebar.PlannerItems.OfType<Module>().Select(m => m.Key).ToList();
            Check("A page under Plan dragged above another moves there", planKeys.IndexOf(Page.Time) == planKeys.IndexOf(Page.Planner) - 1);
            Check("To-do isn't in the Planner sidebar: its to-dos show in My tasks", vm.Sidebar.PlannerItems.OfType<Module>().All(m => m.Title != "To-do"));

            var calendar = vm.Sidebar.PlannerItems.OfType<Module>().First(m => m.Key == Page.Calendar);
            var at = Centre(Row(plannerList, calendar));
            Press(at);
            Let(at);
            Check("A click without a drag still opens the page", vm.Selected.Key == Page.Calendar);

            // A task dragged out of a project's list onto another project in the sidebar moves there.
            var dragMe = services.Projects.AddTask(new Webignite.Core.Projects.ProjectTask { Id = "snap-drag", ProjectId = site.Id, Title = "Drag me to SEO" }).Saved!;
            Open(Page.Projects);
            projectsPage.OpenProject(site.Id);
            Dispatcher.UIThread.RunJobs();
            var taskList = window.GetVisualDescendants().OfType<Avalonia.Controls.ItemsControl>().First(c => c.Name == "TaskList");
            var dragRow = projectsPage.Rows.OfType<TaskRow>().First(r => r.Task.Id == dragMe.Id);
            var firstRow = projectsPage.Rows.OfType<TaskRow>().First();
            DragOnto(Row(taskList, dragRow), Row(taskList, firstRow), 0.2);
            Check("A task dragged above the first task lands first", projectsPage.Rows.OfType<TaskRow>().First().Task.Id == dragMe.Id);
            dragRow = projectsPage.Rows.OfType<TaskRow>().First(r => r.Task.Id == dragMe.Id);
            var seoLink = vm.Sidebar.PlannerItems.OfType<ProjectLink>().First(l => l.Title == "Webignite SEO month");
            start = Centre(Row(taskList, dragRow));
            Press(start);
            MoveTo(start, Centre(Row(plannerList, seoLink)));
            Save("drag-task-to-project");
            Let(Centre(Row(plannerList, seoLink)));
            Check("A task dropped on another project in the sidebar moves to that project", services.Projects.GetTask(dragMe.Id)?.ProjectId == "snap-seo"
                && projectsPage.Rows.OfType<TaskRow>().All(r => r.Task.Id != dragMe.Id));
            services.Projects.DeleteTask(dragMe.Id);

            // Headings drag in the list, and as columns on the board; one order serves both. Tall, so every heading shows.
            window.Height = 1300;
            Dispatcher.UIThread.RunJobs();
            List<string> HeadingNames() => [.. projectsPage.Rows.OfType<SectionRow>().Where(r => r.IsHeading).Select(r => r.Name)];
            var launch = projectsPage.Rows.OfType<SectionRow>().First(r => r.Name == "Launch");
            var designHeading = projectsPage.Rows.OfType<SectionRow>().First(r => r.Name == "Design");
            taskList.ScrollIntoView(designHeading);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            DragOnto(Row(taskList, launch), Row(taskList, designHeading), 0.2);
            Check("A heading dragged above another moves there with its tasks", HeadingNames().SequenceEqual(["Launch", "Design", "Build"])
                && projectsPage.Sections.First(r => r.Name == "Launch").Tasks.Count == 2);
            projectsPage.BoardViewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var board = window.GetVisualDescendants().OfType<Avalonia.Controls.ItemsControl>().First(c => c.Name == "Board");
            var buildColumn = projectsPage.Columns.First(c => c.Section.Name == "Build");
            var launchColumn = projectsPage.Columns.First(c => c.Section.Name == "Launch");
            var columnTop = Row(board, buildColumn).TranslatePoint(new Point(Row(board, buildColumn).Bounds.Width / 2, 14), window) ?? default;
            var launchLeft = Row(board, launchColumn).TranslatePoint(new Point(10, 14), window) ?? default;
            Press(columnTop);
            MoveTo(columnTop, launchLeft);
            Save("drag-board-column");
            Let(launchLeft);
            Check("A board column dragged to the left of another moves there, and the list follows", HeadingNames().SequenceEqual(["Build", "Launch", "Design"]));
            projectsPage.ListViewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Ctrl+click picks two tasks; dragging one takes both, in their order.
            var coloursRow = projectsPage.Rows.OfType<TaskRow>().First(r => r.Title == "Colours and fonts");
            var speed = projectsPage.Rows.OfType<TaskRow>().First(r => r.Title == "Speed test the pages");
            foreach (var pick in new[] { coloursRow, speed })
            {
                var p = Centre(Row(taskList, pick), 0.5);
                p = p.WithX(p.X - Row(taskList, pick).Bounds.Width / 2 + 20);
                window.MouseDown(p, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.Control);
                window.MouseUp(p, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.Control);
                Dispatcher.UIThread.RunJobs();
            }
            Check("Ctrl+click picks tasks to drag together", projectsPage.PickedCount == 2 && coloursRow.IsPicked && speed.IsPicked);
            var designAdd = projectsPage.Rows.OfType<AddTaskRow>().First(a => a.Section.Name == "Design");
            start = Centre(Row(taskList, coloursRow));
            Press(start);
            MoveTo(start, Centre(Row(taskList, designAdd), 0.2));
            Save("drag-picked-tasks");
            Let(Centre(Row(taskList, designAdd), 0.2));
            var designTasks = projectsPage.Sections.First(r => r.Name == "Design").Tasks.Select(t => t.Title).ToList();
            Check("Both picked tasks land at the end of Design, in their order", designTasks.TakeLast(2).SequenceEqual(["Colours and fonts", "Speed test the pages"]));

            projectsPage.ClearPicks();
            // Subtasks: a task dragged onto the open task's subtasks becomes one; dragged back to the list, a task again.
            var wireframes = projectsPage.Rows.OfType<TaskRow>().First(r => r.Title == "Wireframes for the home page");
            projectsPage.OpenTaskById(wireframes.Task.Id);
            Dispatcher.UIThread.RunJobs();
            var area = window.GetVisualDescendants().OfType<Avalonia.Controls.StackPanel>().First(c => c.Name == "SubtaskArea");
            var speedRow = projectsPage.Rows.OfType<TaskRow>().First(r => r.Title == "Speed test the pages");
            var subsBefore = projectsPage.Detail!.Subtasks.Count;
            DragOnto(Row(taskList, speedRow), area, 0.5);
            Check("A task dragged onto the open task's subtasks becomes its last subtask", projectsPage.Detail.Subtasks.Count == subsBefore + 1
                && projectsPage.Detail.Subtasks[^1].Title == "Speed test the pages" && services.Projects.GetTask(speedRow.Task.Id)?.ParentId == wireframes.Task.Id);
            var subList = window.GetVisualDescendants().OfType<Avalonia.Controls.ItemsControl>().First(c => c.Name == "SubtaskList");
            Dispatcher.UIThread.RunJobs();
            DragOnto(Row(subList, projectsPage.Detail.Subtasks[^1]), Row(subList, projectsPage.Detail.Subtasks[0]), 0.2);
            Check("A subtask dragged above the first one moves there", projectsPage.Detail.Subtasks[0].Title == "Speed test the pages");
            var firstTask = projectsPage.Rows.OfType<TaskRow>().First(r => r.Section?.Name == "Build");
            start = Centre(Row(subList, projectsPage.Detail.Subtasks[0]));
            Press(start);
            MoveTo(start, Centre(Row(taskList, firstTask)));
            Save("drag-subtask-out");
            Let(Centre(Row(taskList, firstTask)));
            Check("A subtask dragged onto the list becomes a task there", services.Projects.GetTask(speedRow.Task.Id) is { ParentId: null } back
                && projectsPage.Sections.First(r => r.Name == "Build").Tasks[0].Id == back.Id && projectsPage.Detail.Subtasks.Count == subsBefore);
            projectsPage.CloseTaskCommand.Execute(null);
            window.Height = 1080;
            Dispatcher.UIThread.RunJobs();

            // The List tab as Asana's: folding headings, subtasks opened out under their task, sort and group.
            var designRow = projectsPage.Rows.OfType<SectionRow>().First(r => r.Name == "Design");
            designRow.FoldCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Check("A heading folds up to its name, and stays folded", projectsPage.Rows.OfType<SectionRow>().First(r => r.Name == "Design").IsFolded
                && projectsPage.Rows.OfType<TaskRow>().All(r => r.Section?.Name != "Design") && projectsPage.Layout.Folded.Count == 1);
            projectsPage.Rows.OfType<SectionRow>().First(r => r.Name == "Design").FoldCommand.Execute(null);
            var withSubs = projectsPage.Rows.OfType<TaskRow>().First(r => r.HasSubtasks);
            withSubs.ExpandCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Check("A task's arrow shows its subtasks under it, set in", projectsPage.Rows.OfType<TaskRow>().Any(r => r.IsSubtask));
            window.Width = 1920;
            Dispatcher.UIThread.RunJobs();
            Save("list-tab");
            projectsPage.SortByCommand.Execute(Webignite.Core.Projects.TaskSort.DueDate);
            Check("Sorting by due date orders each heading and turns dragging into order off", !projectsPage.CanReorder && projectsPage.SortText == "Sort: Due date");
            projectsPage.GroupByCommand.Execute(Webignite.Core.Projects.TaskGroup.Assignee);
            Dispatcher.UIThread.RunJobs();
            Check("Grouping by assignee makes a group per person", projectsPage.Rows.OfType<SectionRow>().Select(r => r.Title).Contains("No one")
                && projectsPage.Rows.OfType<SectionRow>().All(r => r.IsMadeGroup) && projectsPage.Columns.All(c => c.Section.IsSection));
            Save("list-grouped");
            projectsPage.GroupByCommand.Execute(Webignite.Core.Projects.TaskGroup.Sections);
            projectsPage.SortByCommand.Execute(Webignite.Core.Projects.TaskSort.None);
            withSubs = projectsPage.Rows.OfType<TaskRow>().First(r => r.HasSubtasks);
            withSubs.ExpandCommand.Execute(null);
            projectsPage.BoardViewCommand.Execute(null);
            projectsPage.OpenProject("snap-seo");
            Dispatcher.UIThread.RunJobs();
            var seoOnList = projectsPage.IsList;
            projectsPage.OpenProject(site.Id);
            Dispatcher.UIThread.RunJobs();
            Check("Each project keeps its own list or board", seoOnList && projectsPage.IsBoard);
            projectsPage.ListViewCommand.Execute(null);

            // Attachments, as Asana's: a picture attached shows in the task pane, becomes the card's cover, and is in Files.
            var shotPath = Path.Combine(data, "Shopfront.png");
            File.WriteAllBytes(shotPath, logo);
            var briefPath = Path.Combine(data, "Brief.pdf");
            File.WriteAllBytes(briefPath, new byte[12_000]);
            var coloursTask = projectsPage.Rows.OfType<TaskRow>().First(r => r.Title == "Colours and fonts");
            projectsPage.OpenTaskById(coloursTask.Task.Id);
            projectsPage.FilesPart.Attach(coloursTask.Task.Id, [shotPath, briefPath]);
            var attachedUntil = DateTime.UtcNow.AddSeconds(5);
            while (projectsPage.Detail!.Attachments.Any(a => a.IsPicture && !a.HasPicture) && DateTime.UtcNow < attachedUntil)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }
            Check("Files attached show in the task pane, the picture shown small", projectsPage.Detail.Attachments.Count == 2
                && projectsPage.Detail.Attachments.Any(a => a.IsPicture && a.HasPicture && a.IsCover));
            Check("A paperclip in the list counts the files", projectsPage.Rows.OfType<TaskRow>().First(r => r.Title == "Colours and fonts").AttachmentCount == "2");
            Save("task-attachments");
            projectsPage.BoardViewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var coverCard = projectsPage.Columns.SelectMany(c => c.Cards).First(r => r.Title == "Colours and fonts");
            var coverUntil = DateTime.UtcNow.AddSeconds(5);
            while (coverCard.Cover?.HasPicture != true && DateTime.UtcNow < coverUntil)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }
            Check("The picture is the card's cover on the board", coverCard.HasCover && coverCard.Cover?.HasPicture == true);
            Save("board-cover");
            projectsPage.FilesViewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Check("The Files tab lists every file in the project, with its task", projectsPage.IsFiles && !projectsPage.IsList && !projectsPage.IsBoard
                && projectsPage.FilesPart.ProjectFiles.Count == 2 && projectsPage.FilesPart.ProjectFiles.All(f => f.TaskTitle == "Colours and fonts"));
            Save("project-files");
            var pdfRow = projectsPage.FilesPart.ProjectFiles.First(f => !f.IsPicture);
            pdfRow.RemoveCommand.Execute(null);
            Check("Removing a file takes it off the task and out of Files", projectsPage.FilesPart.ProjectFiles.Count == 1
                && services.Projects.GetTask(coloursTask.Task.Id)!.Attachments.Count == 1);
            // Files dropped from Windows on a task's row: it lights up while they're over it, and they're attached to it.
            projectsPage.ListViewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var dropRow = projectsPage.Rows.OfType<TaskRow>().First(r => r.Title == "Build the five pages");
            var dropBorder = window.GetVisualDescendants().OfType<Avalonia.Controls.Border>()
                .First(b => b.DataContext == dropRow && b.Classes.Contains("taskrow") && b.IsEffectivelyVisible);
            dropBorder.Classes.Add("filedrop");
            Save("list-file-drop");
            dropBorder.Classes.Remove("filedrop");
            projectsPage.FilesPart.AttachTo(dropRow, [briefPath]);
            var dropped = services.Projects.GetTask(dropRow.Task.Id)!;
            Check("Files dropped on a task's row are attached to it, and it says so", dropped.Attachments.Count == 1
                && projectsPage.Message == "Attached Brief.pdf to Build the five pages.");
            services.Projects.Detach(dropRow.Task.Id, dropped.Attachments[0].Id);
            // Comments and All activity, as Asana's: a comment posted shows in the feed and counts in the list.
            projectsPage.ListViewCommand.Execute(null);
            projectsPage.OpenTaskById(coloursTask.Task.Id);
            Dispatcher.UIThread.RunJobs();
            var talk = projectsPage.Detail!;
            talk.NewComment = "Jaco, the client prefers the darker green. Can you swap it?";
            talk.PostCommentCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Check("A comment posted shows in the task's feed, by me, today", talk.Feed.Count == 1 && talk.Feed[0].IsComment && talk.Feed[0].When.StartsWith("Today", StringComparison.Ordinal)
                && talk.NewComment.Length == 0);
            Check("A speech bubble in the list counts the comments", projectsPage.Rows.OfType<TaskRow>().First(r => r.Task.Id == coloursTask.Task.Id).CommentCount == 1);
            talk.ShowAllActivityCommand.Execute(null);
            Check("All activity adds what happened: attached, removed, and the comment", talk.Feed.Count > 1
                && talk.Feed.Any(f => f.Text.StartsWith("attached", StringComparison.Ordinal)) && talk.Feed.Any(f => f.IsComment));
            window.Width = 1920;
            window.Height = 1300;
            Dispatcher.UIThread.RunJobs();
            var paneScroll = window.GetVisualDescendants().OfType<Avalonia.Controls.Border>().First(b => b.Name == "TaskPane")
                .GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>().First();
            paneScroll.Offset = new Vector(0, 10_000);
            Dispatcher.UIThread.RunJobs();
            Save("task-activity");
            // Likes, followers and @mentions, as Asana's.
            talk.ShowCommentsCommand.Execute(null);
            talk.ToggleLikeCommand.Execute(null);
            talk.Feed[0].ToggleLikeCommand.Execute(null);
            var partnerName = services.Team().Partner!.Name;
            talk.NewComment = "Thanks @" + partnerName[..2];
            Dispatcher.UIThread.RunJobs();
            Check("Typing @ and the start of a name suggests who to mention", talk.MentionSuggestions.Contains(partnerName));
            Check("A liked task and comment show the thumbs-up with a count, and I follow the task I made",
                talk.ILikeIt && talk.LikeCount == "1" && talk.Feed[0].ILikeIt && talk.Feed[0].LikeCount == "1" && talk.IFollow);
            paneScroll.Offset = new Vector(0, 10_000);
            Dispatcher.UIThread.RunJobs();
            Save("task-likes");
            talk.MentionCommand.Execute(partnerName);
            Check("Picking who to mention finishes their name", talk.NewComment == $"Thanks @{partnerName} " && !talk.HasMentionSuggestions);
            talk.NewComment = "";
            paneScroll.Offset = default;
            window.Height = 1080;
            talk.ShowCommentsCommand.Execute(null);
            talk.Feed[0].DeleteCommand.Execute(null);
            Check("Deleting my comment takes it out of the feed and the list's count", talk.Feed.Count == 0
                && projectsPage.Rows.OfType<TaskRow>().First(r => r.Task.Id == coloursTask.Task.Id).CommentCount == 0);
            projectsPage.CloseTaskCommand.Execute(null);
            projectsPage.ListViewCommand.Execute(null);
            window.Width = 1920;
            Dispatcher.UIThread.RunJobs();

            // The main sidebar: a page dragged to a new place, then put back.
            vm.SelectedNav = vm.Sidebar.PlannerBack;
            Dispatcher.UIThread.RunJobs();
            var mail = vm.Sidebar.Items.OfType<Module>().First(m => m.Key == Page.Mail);
            var overview = vm.Sidebar.Items.OfType<Module>().First(m => m.Key == Page.Overview);
            mainList.ScrollIntoView(overview);
            Dispatcher.UIThread.RunJobs();
            DragOnto(Row(mainList, mail), Row(mainList, overview), 0.2);
            Check("A page dragged in the main sidebar moves there, without opening it", vm.Sidebar.Items.OfType<Module>().First().Key == Page.Mail && vm.Selected.Key != Page.Mail);
            vm.Sidebar.ResetCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Home: in my order, a project's tile drags before another, and the sidebar follows.
            Open(Page.Home);
            homePage.ShowProjectsCommand.Execute(Webignite.Core.Projects.HomeProjects.Ordered);
            Dispatcher.UIThread.RunJobs();
            var tilesHost = window.GetVisualDescendants().OfType<Avalonia.Controls.ItemsControl>().First(c => c.Name == "ProjectTiles");
            var tiles = homePage.Projects.OfType<HomeProjectTile>().ToList();
            var lastTile = tiles[^1];
            start = Centre(Row(tilesHost, lastTile));
            var firstTile = Row(tilesHost, tiles[0]);
            var before = firstTile.TranslatePoint(new Point(4, firstTile.Bounds.Height / 2), window) ?? default;
            Press(start);
            MoveTo(start, before);
            Save("drag-home-tile");
            Let(before);
            Check("In my order, a Home tile dragged before the first one moves there, and the sidebar follows",
                homePage.Projects.OfType<HomeProjectTile>().First().Name == lastTile.Name && ProjectTitles()[0] == lastTile.Name);
            homePage.ShowProjectsCommand.Execute(Webignite.Core.Projects.HomeProjects.Recent);
        }

        // Asana's own looks, light and dark: Home, a project, and its colour and icon picker.
        Check("Asana's 16 colours and 32 icons each have a brush and a drawing", Webignite.Core.Projects.ProjectLooks.Colours.All(c => ProjectColours.Brush.Convert(c, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture) is not null)
            && Webignite.Core.Projects.ProjectLooks.Icons.All(i => ProjectColours.Icon.Convert(i, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture) is not null));
        foreach (var (theme, name) in new[] { (Theme.Light, "light"), (Theme.Dark, "dark") })
        {
            services.Settings.Appearance = services.Settings.Appearance with { Theme = theme, LightPalette = LightPalette.Asana, DarkPalette = DarkPalette.Asana };
            Dispatcher.UIThread.RunJobs();
            Open(Page.Home);
            Save($"asana-{name}-home");
            Open(Page.Projects);
            projectsPage.OpenProject(site.Id);
            Dispatcher.UIThread.RunJobs();
            Save($"asana-{name}-project");
            var picker = window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().First(b => Avalonia.Controls.ToolTip.GetTip(b) as string == "Set colour and icon");
            picker.Flyout?.ShowAt(picker);
            Dispatcher.UIThread.RunJobs();
            Save($"asana-{name}-picker");
            picker.Flyout?.Hide();
        }
        services.Settings.Appearance = services.Settings.Appearance with { Theme = Theme.Light, LightPalette = LightPalette.Standard, DarkPalette = DarkPalette.Graphite };
        Dispatcher.UIThread.RunJobs();
        window.Width = 1280;
        window.Height = 860;
        Open(Page.Overview);

        // My pages: a page of widgets the person puts together, saved by name in the sidebar.
        Open(Page.MyPages);
        var myPages = (MyPagesViewModel)vm[Page.MyPages].Main.Page;
        Check("My pages starts with a word on what they're for", myPages.NoPages);
        Save("my-pages-empty");
        vm.SelectedNav = vm.Sidebar.Items.OfType<MyPageLink>().Single(l => l.Id is null);
        Dispatcher.UIThread.RunJobs();
        Check("New page in the sidebar makes a page and opens it to arrange", myPages.IsPage && myPages.IsEditing
            && vm.Sidebar.Items.OfType<MyPageLink>().Any(l => l.Title == "My page"));
        myPages.EditName = "Monday";
        myPages.Keep();
        foreach (var kind in new[] { WidgetKind.MyTasks, WidgetKind.Today, WidgetKind.Month, WidgetKind.Note, WidgetKind.Timer, WidgetKind.Calculator })
            myPages.AddWidget(kind);
        myPages.AddWidget(WidgetKind.Project, site.Id);
        myPages.AddWidget(WidgetKind.Client, services.Clients.List().First(c => c.Name == "Bosveld Builders").Id);
        myPages.AddWidget(WidgetKind.Files);
        var filesBody = (FilesBody)myPages.Widgets.Single(w => w.Kind == WidgetKind.Files).Body;
        filesBody.Go(data);
        Check("The Files widget lists a folder's files, folders first, and keeps the folder with the page",
            Until(() => filesBody.Lines.Count > 0) && filesBody.Folder == data
            && services.Settings.MyPages.Single().Widgets.Single(w => w.Kind == WidgetKind.Files).Target == data
            && filesBody.Lines.SkipWhile(l => l.IsFolder).All(l => l.IsFile));
        ((NoteBody)myPages.Widgets.First(w => w.Kind == WidgetKind.Note).Body).Text = "Call Pieter after 10 about the photos";
        myPages.Keep();
        Dispatcher.UIThread.RunJobs();
        var savedPage = services.Settings.MyPages.Single();
        Check("A page keeps its name, its widgets and a note's words", savedPage.Name == "Monday" && savedPage.Widgets.Count == 9
            && savedPage.Widgets.Single(w => w.Kind == WidgetKind.Note).Text == "Call Pieter after 10 about the photos"
            && vm.Sidebar.Items.OfType<MyPageLink>().Any(l => l.Title == "Monday"));
        myPages.AddWidget(WidgetKind.NeedsYou);
        myPages.AddWidget(WidgetKind.Mail);
        Check("Needs you and the mail widget show what Overview and Mail have", myPages.Widgets.Single(w => w.Kind == WidgetKind.NeedsYou).Body is NeedsYouBody { Lines.Count: > 0 }
            && myPages.Widgets.Single(w => w.Kind == WidgetKind.Mail).Body is MailBody { Lines.Count: > 0 } mailBody && mailBody.Title!.StartsWith("Mail inbox", StringComparison.Ordinal));
        savedPage = services.Settings.MyPages.Single();
        Check("Widgets go on the grid without overlapping", savedPage.Widgets.All(x => savedPage.Widgets.All(y => x.Id == y.Id || !MyPages.Overlap(x, y))));
        Check("The project widget shows the project's open tasks", myPages.Widgets.Single(w => w.Kind == WidgetKind.Project).Body is TasksBody { Lines.Count: > 0 } projectBody
            && projectBody.Title == "Bosveld Builders new website");
        var noteWidget = myPages.Widgets.First(w => w.Kind == WidgetKind.Note);
        myPages.BeginArranging();
        myPages.MoveTo(noteWidget, 0, 0);
        myPages.EndArranging();
        myPages.BeginArranging();
        myPages.ResizeTo(noteWidget, 16, 12);
        myPages.EndArranging();
        savedPage = services.Settings.MyPages.Single();
        Check("Dragging a widget to the top left puts it there and moves the others out of its way", noteWidget is { Column: 0, Row: 0, Width: 16, Height: 12 }
            && savedPage.Widgets.Single(w => w.Kind == WidgetKind.Note) is { Column: 0, Row: 0, Width: 16, Height: 12 }
            && savedPage.Widgets.All(x => savedPage.Widgets.All(y => x.Id == y.Id || !MyPages.Overlap(x, y))));
        var timerWidget = myPages.Widgets.Single(w => w.Kind == WidgetKind.Timer);
        var (timerColumn, timerRow) = (timerWidget.Column, timerWidget.Row);
        myPages.BeginArranging();
        myPages.MoveTo(timerWidget, 37, 150);
        myPages.EndArranging();
        Check("A widget stays where it's dropped, gaps and all, on a fine grid", timerWidget is { Column: 36, Row: 150 }
            && services.Settings.MyPages.Single().Widgets.Single(w => w.Kind == WidgetKind.Timer) is { Column: 36, Row: 150 });
        myPages.BeginArranging();
        myPages.MoveTo(timerWidget, timerColumn, timerRow);
        myPages.EndArranging();
        Save("my-pages-arranging");
        var filesWidget = myPages.Widgets.Single(w => w.Kind == WidgetKind.Files);
        myPages.BeginArranging();
        myPages.ResizeTo(filesWidget, 16, 22);
        myPages.EndArranging();
        myPages.BeginArranging();
        myPages.MoveTo(filesWidget, 32, 0);
        myPages.EndArranging();
        myPages.ToggleEditingCommand.Execute(null);
        window.Width = 1920;
        window.Height = 1080;
        Dispatcher.UIThread.RunJobs();
        Check("The page on screen is the one marked in the sidebar", vm.SelectedNav is MyPageLink { Title: "Monday" });
        Save("my-pages");
        window.Width = 1280;
        window.Height = 860;
        myPages.ShowList();
        Check("All my pages lists the pages made", myPages.Cards.Count == 1 && myPages.Cards[0].Name == "Monday");
        Save("my-pages-list");
        myPages.NewPage();
        myPages.EditName = "Morning";
        foreach (var kind in new[] { WidgetKind.NeedsYou, WidgetKind.Mail, WidgetKind.MyTasks })
            myPages.AddWidget(kind);
        myPages.ToggleEditingCommand.Execute(null);
        window.Width = 1920;
        window.Height = 1080;
        Dispatcher.UIThread.RunJobs();
        Check("A second page shows in the sidebar under its name", vm.Sidebar.Items.OfType<MyPageLink>().Select(l => l.Title).Take(2).SequenceEqual(["Monday", "Morning"]));
        Save("my-pages-morning");
        window.Width = 1280;
        window.Height = 860;
        Open(Page.Overview);
    }

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

    // The same box makes a to-do or an event.
    var quickTodo = new QuickNoteWindow(services) { Typed = "Send Bosveld the sitemap\nThe PDF from Friday's meeting" };
    quickTodo.Show(Webignite.Core.Tools.CaptureKind.Todo);
    quickTodo.Due = Webignite.Core.Tools.CaptureDue.Tomorrow;
    quickTodo.Show();
    Dispatcher.UIThread.RunJobs();
    using (var todoFrame = quickTodo.CaptureRenderedFrame())
    using (var todoFile = File.Create(Path.Combine(output, "quick-todo.png")))
        todoFrame?.Save(todoFile, new PngBitmapEncoderOptions());
    quickTodo.SaveAndClose();
    Check("A quick to-do goes on the list, due tomorrow, with its notes", quickTodo.SavedTodo is { Title: "Send Bosveld the sitemap", Notes: "The PDF from Friday's meeting" } todoSaved
        && todoSaved.Due == DateOnly.FromDateTime(DateTime.Today).AddDays(1) && store.ListTasks().Any(t => t.Id == todoSaved.Id));

    var quickEvent = new QuickNoteWindow(services) { Typed = "Site walk-through with Pieter" };
    quickEvent.Show(Webignite.Core.Tools.CaptureKind.Event);
    quickEvent.Time = "after lunch";
    quickEvent.Show();
    quickEvent.SaveAndClose();
    Check("An event with a time that can't be read stays open and says why", quickEvent.IsVisible && quickEvent.SavedEvent is null);
    quickEvent.Time = "14:00-15:30";
    Dispatcher.UIThread.RunJobs();
    using (var eventFrame = quickEvent.CaptureRenderedFrame())
    using (var eventFile = File.Create(Path.Combine(output, "quick-event.png")))
        eventFrame?.Save(eventFile, new PngBitmapEncoderOptions());
    quickEvent.SaveAndClose();
    Check("A quick event goes in the calendar at its time", quickEvent.SavedEvent is { Title: "Site walk-through with Pieter" } eventSaved
        && eventSaved.Start == new TimeOnly(14, 0) && eventSaved.End == new TimeOnly(15, 30) && store.GetEvent(eventSaved.Id) is not null);

    var quickLost = new QuickNoteWindow(services) { Typed = "Dentist" };
    quickLost.Show(Webignite.Core.Tools.CaptureKind.Event);
    quickLost.Time = "sometime";
    quickLost.Show();
    quickLost.Close();
    Check("Closed anyway, an event that can't be saved is kept as a note", quickLost.Saved is { } lostNote && lostNote.Text.Contains("sometime", StringComparison.Ordinal));

    var notesSettings = (SettingsViewModel)vm[Page.Settings].Main.Page;
    notesSettings.Open("notes");
    Open(Page.Settings);
    Save("settings-notes");
    notesSettings.SetShortcut(Webignite.Core.Tools.GlobalShortcut.QuickNote, "Ctrl+Alt+S");
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
    services.Go.MyTasks();
    Dispatcher.UIThread.RunJobs();
    var firstTodo = ((ProjectsViewModel)vm.Current.Page).MyRows.OfType<TodoRow>().First(t => !t.Done);
    firstTodo.SetDue(DateOnly.FromDateTime(DateTime.Today).AddDays(1));
    Check("A to-do in My tasks moves its due day", store.ListTasks().First(t => t.Id == firstTodo.Todo.Id).Due == DateOnly.FromDateTime(DateTime.Today).AddDays(1));
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
Wait(vm.Search.FilesLoaded, "Search read the file names");
vm.Search.Query = "bosveld";
var kinds = vm.Search.Results.Select(r => r.Kind).ToHashSet();
Check("Ctrl+K finds notes, site logins and files too", kinds.Contains(Webignite.Core.Search.HitKind.Note)
    && kinds.Contains(Webignite.Core.Search.HitKind.Login) && kinds.Contains(Webignite.Core.Search.HitKind.File));
File.WriteAllText(Path.Combine(projects, "Bosveld Builders", "brief-notes.txt"), "Pieter wants the retaining wall along the back done first.");
vm.Search.Query = "retaining wall";
Wait(vm.Search.InsideLoaded, "Search looked inside the files");
Check("Ctrl+K finds a file by the words inside it", vm.Search.Results.Any(r => r.Kind == Webignite.Core.Search.HitKind.File && r.Title == "brief-notes.txt"));
Save("search-inside");
var tagName = services.FileTags.Tags.First(t => services.FileTags.Count(t.Id) > 0).Name;
vm.Search.Hide();
vm.Search.Show();
vm.Search.Query = tagName;
Check("Ctrl+K finds files by their tag", vm.Search.Results.Any(r => r.Kind == Webignite.Core.Search.HitKind.File && r.Detail.Contains(tagName, StringComparison.Ordinal)));
vm.Search.Query = "bosveld";
Check("A site login in Ctrl+K never shows its password", vm.Search.Results.Where(r => r.Kind == Webignite.Core.Search.HitKind.Login)
    .All(r => !(r.Title + r.Detail).Contains("Bosveld#2026!", StringComparison.Ordinal)));
Save("search-more");
vm.Search.Query = "marlene";
Check("Ctrl+K finds people in Contacts", vm.Search.Results.Any(r => r.Kind == Webignite.Core.Search.HitKind.Person && r.Title == "Marlene Botha"));
vm.Search.Query = "wordpress";
vm.Search.OpenChosen();
Check("Opening a site login opens its client", vm.Clients.Detail?.Title == "Bosveld Builders" && !vm.Search.IsOpen);
vm.Clients.Detail?.BackCommand.Execute(null);

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
// Every page on a big screen and in a small window: pages use the room they are given, Settings keeps a
// reading width, and the month fills the window both ways.
{
    foreach (var (size, width, height) in new[] { ("big", 1920.0, 1080.0), ("small", 1280.0, 800.0) })
    {
        window.Width = width;
        window.Height = height;
        foreach (var page in Enum.GetValues<Page>())
        {
            Open(page);
            Save($"size-{size}-{page.ToString().ToLowerInvariant()}");
        }
    }
    window.Width = 1920;
    window.Height = 1080;
    Open(Page.Calendar);
    Dispatcher.UIThread.RunJobs();
    var calendarView = window.GetVisualDescendants().OfType<CalendarView>().First();
    var days = calendarView.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Where(b => b.Classes.Contains("day")).ToList();
    Check("On a big screen the calendar's days grow to fill the height", days.Count >= 28 && days[0].Bounds.Height > 120);
    var monthScroller = calendarView.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>().First(s => s.Name == "MonthScroller");
    Check("On a big screen the month fits without scrolling", monthScroller.Extent.Height <= monthScroller.Viewport.Height + 1);
    Open(Page.Retainers);
    Dispatcher.UIThread.RunJobs();
    var listPage = window.GetVisualDescendants().OfType<RetainersView>().FirstOrDefault();
    Check("On a big screen a list page is wider than the old reading width", listPage is { Bounds.Width: > 1200 });
    Open(Page.Settings);
    Dispatcher.UIThread.RunJobs();
    var settings = window.GetVisualDescendants().OfType<SettingsView>().FirstOrDefault();
    Check("Settings keeps a reading width on a big screen", settings is { Bounds.Width: <= 1040 });

    // Calendar and Planner sized by dragging: kept, and a fresh copy of the page opens at that size.
    var calendarPage = (CalendarViewModel)vm[Page.Calendar].Main.Page;
    calendarPage.KeepMonthHeight(420);
    var sizedCalendar = vm.OpenAnother(Page.Calendar);
    Dispatcher.UIThread.RunJobs();
    var sizedView = window.GetVisualDescendants().OfType<CalendarView>().First(v => v.IsEffectivelyVisible);
    var sizedScroller = sizedView.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>().First(s => s.Name == "MonthScroller");
    Check("A month dragged shorter opens at that height", sizedScroller.Bounds.Height is > 300 and < 420
        && services.Settings.CalendarSizes.MonthHeight == 420);
    Save("calendar-sized");
    calendarPage.KeepMonthHeight(null);
    vm.CloseTab(sizedCalendar);
    Open(Page.Planner);
    var plannerPage = (PlannerViewModel)vm[Page.Planner].Main.Page;
    plannerPage.KeepDayHeight(170);
    Dispatcher.UIThread.RunJobs();
    Check("Planner's days take the height they were dragged to, and keep it", plannerPage.DayHeight == 170
        && services.Settings.CalendarSizes.PlannerDayHeight == 170);
    Save("planner-sized");
    plannerPage.KeepDayHeight(null);
    Check("Double-clicking the handle fits the days to what's on them again", double.IsNaN(plannerPage.DayHeight)
        && services.Settings.CalendarSizes.PlannerDayHeight is null);
    window.Width = 1280;
    window.Height = 860;
    Open(Page.Overview);
}

// Inbox, as Asana's: what my partner did on a task of mine, a mention, a like and a task given to me.
{
    var project = services.Projects.Projects().First(p => p.Shared);
    var me = services.Me().Id;
    var partner = services.Team().Partner!;
    var asPartner = new Webignite.Services.ProjectService(services.Store, () => partner.Id);
    var mine = services.Projects.AddTask(new Webignite.Core.Projects.ProjectTask { Id = Webignite.Core.Projects.ProjectRules.NewId(), ProjectId = project.Id, Title = "Ask the client for their logo files" }).Saved!;
    asPartner.AddComment(mine.Id, $"@{services.Me().Name} they sent them through. They're in the shared folder now.");
    asPartner.LikeTask(mine.Id, true);
    asPartner.ChangeTask(mine.Id, t => t with { Done = true });
    var given = asPartner.AddTask(new Webignite.Core.Projects.ProjectTask { Id = Webignite.Core.Projects.ProjectRules.NewId(), ProjectId = project.Id, Title = "Send the client the colour options" }).Saved!;
    asPartner.ChangeTask(given.Id, t => t with { AssignedTo = me });
    Open(Page.Inbox);
    Dispatcher.UIThread.RunJobs();
    var inboxPage = (InboxViewModel)vm[Page.Inbox].Main.Page;
    var rows = inboxPage.Rows.OfType<InboxRow>().ToList();
    Check("Inbox lists what my partner did: given to me, completed, liked and mentioned, newest first, all unread",
        rows.Count == 4 && rows.All(r => r.IsUnread) && rows[0].Line.EndsWith("assigned this task to you", StringComparison.Ordinal)
        && rows.Any(r => r.Line.EndsWith("mentioned you", StringComparison.Ordinal) && r.HasSnippet)
        && rows.Any(r => r.Line.EndsWith("liked your task", StringComparison.Ordinal)));
    Check("Inbox groups them under Today", inboxPage.Rows.OfType<InboxHeading>().Single().Title == "Today");
    Wait(Task.Run(services.Inbox.Unread), "Counting Inbox");
    Save("inbox");
    rows[1].ArchiveCommand.Execute(null);
    Check("Archiving takes one out of Activity and reads it", inboxPage.Rows.OfType<InboxRow>().Count() == 3 && services.Inbox.Unread() == 3);
    inboxPage.ShowArchiveCommand.Execute(null);
    Check("Archive shows it, ready to move back", inboxPage.Rows.OfType<InboxRow>().Single().IsArchived);
    inboxPage.ShowActivityCommand.Execute(null);
    inboxPage.MarkAllReadCommand.Execute(null);
    Check("Mark all as read reads them all", services.Inbox.Unread() == 0 && inboxPage.Rows.OfType<InboxRow>().All(r => !r.IsUnread) && !inboxPage.HasUnread);
    Open(Page.Overview);
}

// A project's Messages tab, as Asana's: a conversation with a reply from my partner, and the Share button.
{
    var project = services.Projects.Projects().First(p => p.Shared);
    var partner = services.Team().Partner!;
    var asPartner = new Webignite.Services.ProjectService(services.Store, () => partner.Id);
    services.Go.ProjectMessages(project.Id);
    Dispatcher.UIThread.RunJobs();
    var projectsPage = (ProjectsViewModel)vm[Page.Projects].Main.Page;
    Check("Messages opens on its own tab with no task tools", projectsPage.IsMessages && !projectsPage.ShowsTaskTools && projectsPage.MessagesPart.NoMessages);
    projectsPage.MessagesPart.NewMessageSubject = "Kick-off";
    projectsPage.MessagesPart.NewMessageText = "Site map and the first wireframes are in the shared folder. Launch is planned for the 20th.";
    projectsPage.MessagesPart.PostMessageCommand.Execute(null);
    var kickOff = services.Projects.Messages(project.Id)[0].Start;
    asPartner.PostMessage(project.Id, "", $"@{services.Me().Name} the 20th works for me. I'll have the copy done by the 14th.", kickOff.Id);
    asPartner.LikeMessage(kickOff.Id, true);
    projectsPage.Refresh();
    Dispatcher.UIThread.RunJobs();
    Check("A message posted shows with my partner's reply and like", projectsPage.MessagesPart.MessageThreads.Count == 1
        && projectsPage.MessagesPart.MessageThreads[0].Replies.Count == 1 && projectsPage.MessagesPart.MessageThreads[0].Start.LikeCount == "1"
        && projectsPage.MessagesPart.NewMessageSubject.Length == 0);
    Save("project-messages");
    projectsPage.ListViewCommand.Execute(null);
    Open(Page.Overview);
}

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
/// <summary>Stands in for Windows' spell checker: two known misspellings and their fixes.</summary>
sealed class StandInSpelling : Webignite.Core.Writing.ISpelling
{
    private readonly Dictionary<string, string> fixes = new() { ["teh"] = "the", ["paymnet"] = "payment" };
    public string Language => "en-ZA";
    public int Revision { get; private set; }
    public bool IsWrong(string word) => fixes.ContainsKey(word);
    public IReadOnlyList<string> Suggest(string word) => fixes.TryGetValue(word, out var fix) ? [fix] : [];

    public void Add(string word)
    {
        fixes.Remove(word);
        Revision++;
    }

    public void Ignore(string word) => Add(word);
}

/// <summary>A page reader with no browser behind it: it notes that it was started and shut down.</summary>
sealed class StandInReader : Webignite.Desktop.Web.IPageReader
{
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }

    public Task StartAsync()
    {
        Started = true;
        return Task.CompletedTask;
    }

    public Task<string?> ReadAsync(Uri address, CancellationToken cancel) => Task.FromResult<string?>(null);

    public void Dispose() => Disposed = true;
}

/// <summary>A browser that opens nothing: it says a page opened, hands back a small picture, and notes what it was asked.</summary>
sealed class StandInBrowser : Webignite.Desktop.Web.IWebEngine
{
    // The smallest PNG there is: one clear pixel.
    private const string Pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    public StandInBrowser() => View = new Avalonia.Controls.Border
    {
        Background = Avalonia.Media.Brushes.White,
        Child = new Avalonia.Controls.StackPanel
        {
            Margin = new Avalonia.Thickness(40, 32), Spacing = 14,
            Children =
            {
                new Avalonia.Controls.TextBlock { Text = "Karoo Coffee", FontSize = 30, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brushes.Black },
                new Avalonia.Controls.TextBlock { Text = "Menu", FontSize = 20, Foreground = Avalonia.Media.Brushes.Black },
                new Avalonia.Controls.TextBlock { Text = "(The page itself is drawn here by WebView2 on Windows. This is a stand-in for the screenshot tool.)", Foreground = Avalonia.Media.Brushes.Gray },
            },
        },
    };

    public Avalonia.Controls.Control View { get; }
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }
    public bool FailNext { get; set; }
    public List<string> Calls { get; } = [];
    public List<string> Asked { get; } = [];
    public Webignite.Core.Web.PdfChoices? Pdf { get; private set; }

    public event Action<Uri>? Loading;
    public event Action<Webignite.Core.Web.PageOpened>? Opened;

    public Task StartAsync()
    {
        Started = true;
        return Task.CompletedTask;
    }

    public void Open(Uri address)
    {
        Loading?.Invoke(address);
        var there = !address.Host.StartsWith("nothere.", StringComparison.Ordinal);
        Opened?.Invoke(new Webignite.Core.Web.PageOpened(address, there, there ? 200 : 0, there ? "Menu | Karoo Coffee" : "",
            there ? "" : Webignite.Core.Web.PageCapture.Problem(0, "HostNameNotResolved")));
    }

    public Task<string> DevToolsAsync(string method, string parametersJson)
    {
        Calls.Add(method);
        Asked.Add(parametersJson);
        return Task.FromResult(method switch
        {
            "Runtime.evaluate" => """{"result":{"type":"number","value":2400}}""",
            "Page.captureScreenshot" => "{\"data\":\"" + Pixel + "\"}",
            _ => "{}",
        });
    }

    public Task<bool> SavePdfAsync(string path, Webignite.Core.Web.PdfChoices choices)
    {
        if (FailNext)
        {
            FailNext = false;
            return Task.FromResult(false);
        }
        Pdf = choices;
        File.WriteAllText(path, "%PDF-1.4");
        return Task.FromResult(true);
    }

    public void Dispose() => Disposed = true;
}

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

/// <summary>A web with nothing on it: every request fails as it would with no internet.</summary>
sealed class NoWeb : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromException<HttpResponseMessage>(new HttpRequestException(HttpRequestError.NameResolutionError));
}
