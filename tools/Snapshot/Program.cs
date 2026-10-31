using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Webignite.Desktop;
using Webignite.Desktop.ViewModels;
using Webignite.Desktop.Views;

// Usage: dotnet run --project tools/Snapshot -- <output folder>
var output = args.Length > 0 ? args[0] : "screenshots";
Directory.CreateDirectory(output);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

var vm = new MainViewModel();
var window = new MainWindow { DataContext = vm, Width = 1280, Height = 860 };
window.Show();

void Save(string name)
{
    Dispatcher.UIThread.RunJobs();
    using var frame = window.CaptureRenderedFrame();
    using var file = File.Create(Path.Combine(output, name + ".png"));
    frame?.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    Console.WriteLine("Saved " + name);
}

Save("overview");

var invoice = (DocumentEditorViewModel)vm.Modules[2].Page;
vm.Selected = vm.Modules[2];
Save("invoice-empty");

invoice.CustomerName = "Karoo Coffee Roasters";
invoice.Reference = "Website November";
invoice.Lines[0].Description = "Website hosting, November";
invoice.Lines[0].Price = "450";
invoice.AddLineCommand.Execute(null);
invoice.Lines[1].Description = "Content updates (hours)";
invoice.Lines[1].Quantity = "3,5";
invoice.Lines[1].Price = "R650";
invoice.Notes = "Please pay by EFT to FNB, account 62xxxxxxx. Thank you!";
Save("invoice-filled");

vm.Selected = vm.Modules[4];
Save("mail-later");
