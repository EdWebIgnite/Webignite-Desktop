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

void Save(string name)
{
    Dispatcher.UIThread.RunJobs();
    using var frame = window.CaptureRenderedFrame();
    using var file = File.Create(Path.Combine(output, name + ".png"));
    frame?.Save(file, new PngBitmapEncoderOptions());
    Console.WriteLine("Saved " + name);
}

Save("overview");

vm.Selected = vm.Modules[2];
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

vm.Selected = vm.Modules[8];
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
vm.Selected = vm.Modules[3];
Save("todo");

store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e1", Title = "Kick-off with Bosveld Builders", Date = today, Start = new TimeOnly(10, 0), Location = "Teams" });
store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e2", Title = "Fynbos Physio photo shoot", Date = today.AddDays(2), Start = new TimeOnly(14, 30) });
store.SaveEvent(new Webignite.Core.Calendar.CalendarEvent { Id = "e3", Title = "Public holiday", Date = today.AddDays(-5) });
store.SaveTask(new Webignite.Core.Tasks.TaskItem { Id = "t5", Title = "Write blog post on site speed", Created = DateTimeOffset.UtcNow });
vm.Selected = vm.Modules[4];
Save("calendar");
vm.Selected = vm.Modules[5];
Save("planner");
vm.Selected = vm.Modules[0];
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
