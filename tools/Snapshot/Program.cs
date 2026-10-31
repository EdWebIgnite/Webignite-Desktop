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
var store = new LocalStore(Path.Combine(data, "snapshot.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
var services = new AppServices(store) { TokenStore = new MemoryTokenStore() };
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

Open("Settings");
Save("settings");

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
Open("Reports");
Save("reports");
Open("Overview");
Save("overview");

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
