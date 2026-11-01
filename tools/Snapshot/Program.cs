using System.Security.Cryptography;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Webignite.Core.Documents;
using Webignite.Data;
using Webignite.Desktop;
using Webignite.Desktop.ViewModels;
using Webignite.Desktop.Views;
using Webignite.Xero.Auth;

// Usage: dotnet run --project tools/Snapshot -- <output folder>
// Uses a throwaway database with sample documents, never the real one.
var output = args.Length > 0 ? args[0] : "screenshots";
Directory.CreateDirectory(output);
var data = Directory.CreateTempSubdirectory("webignite-snapshot-").FullName;
var snapshotKey = RandomNumberGenerator.GetBytes(32);
var store = new LocalStore(Path.Combine(data, "snapshot.db"), new Vault(snapshotKey));
var services = new AppServices(store) { TokenStore = new MemoryTokenStore(), VaultKeySource = () => snapshotKey };
services.BackupSettings = new Webignite.Core.Backups.BackupSettings { Folder = Path.Combine(data, "OneDrive", "Webignite backups") };
App.Services = services;

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

Sample("Karoo Coffee Roasters", DocumentKind.Invoice, ("Website hosting, November", 1, 450m), ("Content updates (hours)", 3.5m, 650m));
Sample("Fynbos Physio", DocumentKind.Invoice, ("Monthly SEO", 1, 2800m));
store.Save(store.List(DocumentKind.Invoice)[0] with { Status = DocumentStatus.InXero, XeroNumber = "INV-0107" });
Sample("Bosveld Builders", DocumentKind.Quote, ("New website, 6 pages", 1, 14500m), ("Logo refresh", 1, 3200m));

var vm = new MainViewModel(services);
var window = new MainWindow { DataContext = vm, Width = 1280, Height = 860 };
window.Show();

void Open(string title) => vm.Selected = vm.Modules.Single(m => m.Title == title);

void Save(string name)
{
    Dispatcher.UIThread.RunJobs();
    using var frame = window.CaptureRenderedFrame();
    using var file = File.Create(Path.Combine(output, name + ".png"));
    frame?.Save(file, new PngBitmapEncoderOptions());
    Console.WriteLine("Saved " + name);
}

Save("overview");

Open("Invoices");
Save("invoices");

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
var uncertain = store.Save(karoo with { XeroState = Webignite.Core.Xero.XeroWriteState.Uncertain, XeroContactId = "k1",
    XeroProblems = ["The connection to Xero broke during the write. Use Check Xero."] });
vm.Invoices.Editor = new DocumentEditorViewModel(uncertain, services, () => vm.Invoices.Editor = null);
Save("invoice-xero-uncertain");
var verified = store.Save(uncertain with { XeroState = Webignite.Core.Xero.XeroWriteState.Verified, XeroId = "3f2a", XeroNumber = "INV-0111",
    Status = DocumentStatus.InXero, XeroProblems = [] });
vm.Invoices.Editor = new DocumentEditorViewModel(verified, services, () => vm.Invoices.Editor = null);
Save("invoice-in-xero");
vm.Invoices.Editor.StartEmailCommand.Execute(null);
Save("invoice-approve");
vm.Invoices.Editor = null;

Open("Settings");
Save("settings");

// Run synchronously here: the headless dispatcher isn't pumped while this script waits.
services.Secrets.Write(AppServices.BackupPasswordName, "a long sample password");
var backupAt = DateTimeOffset.Now.AddMinutes(-20);
var backupFile = Backups.Seal(Backups.Snapshot(store), snapshotKey, "a long sample password", services.BackupFolder, backupAt, "0.2.2", 1000);
services.BackupSettings = services.BackupSettings with { LastBackup = backupAt, LastFile = backupFile };
var settingsPage = (SettingsViewModel)vm.Selected.Page;
settingsPage.Refresh();
Save("settings-backups");

var today = DateOnly.FromDateTime(DateTime.Today);
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t1", Title = "Send Bosveld the sitemap for sign-off", Due = today, Created = DateTimeOffset.UtcNow });
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t2", Title = "Renew webignite.co.za SSL certificate", Due = today.AddDays(3), Created = DateTimeOffset.UtcNow });
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t3", Title = "Update portfolio with Fynbos Physio site", Created = DateTimeOffset.UtcNow });
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t4", Title = "Back up client sites", Due = today.AddDays(-1), Done = true, DoneAt = DateTimeOffset.UtcNow, Created = DateTimeOffset.UtcNow });
var overdue = store.List(DocumentKind.Invoice).First(d => d.XeroNumber is not null);
store.Save(overdue with { Document = overdue.Document with { Date = today.AddDays(-21), DueOrExpiryDate = today.AddDays(-14) } });
var quote = store.List(DocumentKind.Quote)[0];
store.Save(quote with { Status = DocumentStatus.InXero, XeroNumber = "QU-0031" });
Open("To-do");
Save("todo");

store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e1", Title = "Kick-off with Bosveld Builders", Date = today, Start = new TimeOnly(10, 0), Location = "Teams" });
store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e2", Title = "Fynbos Physio photo shoot", Date = today.AddDays(2), Start = new TimeOnly(14, 30) });
store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e3", Title = "Public holiday", Date = today.AddDays(-5) });
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t5", Title = "Write blog post on site speed", Created = DateTimeOffset.UtcNow });
Open("Calendar");
Save("calendar");
Open("Planner");
Save("planner");
store.SaveClient(new Webignite.Core.Clients.Client
{
    Id = "c1", Name = "Bosveld Builders", ContactPerson = "Pieter van Wyk", Email = "pieter@bosveldbuilders.co.za",
    Phone = "082 555 0142", Website = "bosveldbuilders.co.za", Notes = "Prefers WhatsApp. Site on our xneelo server.",
});
store.SaveClient(new Webignite.Core.Clients.Client { Id = "c2", Name = "Fynbos Physio", Email = "admin@fynbosphysio.co.za" });
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t6", Title = "Get photos of finished projects", Customer = "Bosveld Builders", Due = today.AddDays(4), Created = DateTimeOffset.UtcNow });
store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e4", Title = "Sitemap review", Date = today.AddDays(1), Start = new TimeOnly(9, 0), Customer = "Bosveld Builders" });
Open("Clients");
Save("clients");
vm.Clients.OpenCommand.Execute(vm.Clients.Rows[0]);
Save("client");
vm.Clients.Detail!.BackCommand.Execute(null);
var month = new DateOnly(today.Year, today.Month, 1);
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
Open("Money");
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
Open("Retainers");
Save("retainers");
var retainersPage = (RetainersViewModel)vm.Selected.Page;
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
Open("Domains and hosting");
Save("domains");
Open("Reports");
Save("reports");

// Mail: the first-time setup, then two mailboxes with a few messages kept on this PC. No network is used.
Open("Mail");
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
_ = vm.Mail.Select(opened);
Save("mail");
vm.Mail.Reader!.ReplyCommand.Execute(null);
vm.Mail.Composer!.Body = "Hi Lerato,\n\nThanks for letting me know. I'm looking at it now and will come back to you before lunch." + vm.Mail.Composer.Body;
vm.Mail.Composer.ShowQuote = true;
Save("mail-reply");
vm.Mail.Composer.DiscardCommand.Execute(null);
vm.Mail.Open(vm.Mail.Mailboxes[0].Folders[0]);
vm.Mail.Status = "Up to date at 09:12";
Save("mail-all-inboxes");
Open("Clients");
vm.Clients.OpenCommand.Execute(vm.Clients.Rows.First(r => r.Client.Name == "Fynbos Physio"));
Save("client-mail");

// An invoice email as Approve and email prepares it, from accounts@ with the signature picture.
var logo = File.ReadAllBytes(Path.Combine("src", "Webignite.Desktop", "Assets", "webignite-logo.png"));
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
services.ComposeMail!(new Webignite.Core.Mail.OutgoingMail
{
    AccountId = invoiceBox.Id,
    To = [new("Thandi Mokoena", "accounts@karoocoffee.co.za")],
    Subject = filled.Subject,
    Body = filled.Body + Webignite.Core.Mail.Replies.StartBody(invoiceBox.Signature),
    Attachments = [new Webignite.Core.Mail.OutgoingAttachment("Invoice INV-0111.pdf", 48_000, FilePath: pdfPath)],
}, null, null);
Save("invoice-email");
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
Open("To approve");
Save("to-approve");
vm.Approvals.Rows[1].OpenCommand.Execute(null);
Save("to-approve-open");
vm.Approvals.Editor!.BackCommand.Execute(null);

Open("Overview");
Save("overview");

// New mail while another page is open: a count beside Mail and a note in the corner.
var mailIds = store.ListMailAccounts().Select(a => a.Id).ToList();
services.MailCheckSettings = new Webignite.Core.Mail.MailCheckSettings { Announced = mailIds.ToDictionary(id => id, _ => 0u) };
_ = vm.CheckMailAsync();
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

    Webignite.Core.Mail.MailSummary Mail(string account, uint uid, string fromName, string from, string subject, string preview, TimeSpan ago, bool seen, bool attachments = false, bool flagged = false) => new()
    {
        AccountId = account, Folder = "INBOX", Uid = uid, Date = now - ago,
        From = new(fromName, from), To = [new(null, account == work.Id ? work.Address : accounts.Address)],
        Subject = subject, Preview = preview, Seen = seen, HasAttachments = attachments, Flagged = flagged,
    };
    store.SaveMailSummaries(
    [
        Mail(work.Id, 44, "Thandi Mokoena", "thandi@karoocoffee.co.za", "New product photos for the shop", "Hi Eddie, here are the new photos for the online shop. Could you swap them in before Friday?", TimeSpan.FromMinutes(25), false, attachments: true),
        Mail(work.Id, 43, "Pieter van Wyk", "pieter@bosveldbuilders.co.za", "Re: Website quote", "Thanks, the quote looks good. Can we start in the first week of December?", TimeSpan.FromHours(2), false),
        Mail(work.Id, 41, "Lerato Dlamini", "lerato@fynbosphysio.co.za", "Booking page not loading on phones", "Morning Eddie, a few patients say the booking page just spins on their phones.", TimeSpan.FromHours(5), true, attachments: true, flagged: true),
        Mail(work.Id, 40, "xneelo", "noreply@xneelo.co.za", "Your hosting renewal for webignite.co.za", "Your hosting package renews on 15 December 2026.", TimeSpan.FromDays(2), true),
        Mail(work.Id, 39, "Google Search Console", "sc-noreply@google.com", "New coverage issue for fynbosphysio.co.za", "Search Console has found a new issue on your site.", TimeSpan.FromDays(4), true),
        Mail(accounts.Id, 12, "Karoo Coffee Roasters", "accounts@karoocoffee.co.za", "Proof of payment INV-0107", "Good day, please find attached proof of payment for INV-0107.", TimeSpan.FromHours(1), false, attachments: true),
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
