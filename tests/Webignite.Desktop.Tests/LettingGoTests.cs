using System.Runtime.CompilerServices;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Tests;

/// <summary>Closing something frees what it held: a page let go is not kept alive by the services it listened to.</summary>
public sealed class LettingGoTests
{
    [Fact]
    public async Task Settings_let_go_is_not_held_by_the_look_it_listened_to()
    {
        using var app = new TestApp();
        var gone = await OpenAndLetGo(app, services => new SettingsViewModel(services));

        Collect();

        Assert.False(gone.IsAlive, "Settings is still in memory after it was let go: something it subscribed to still holds it.");
        // And the look can still change without a let-go page answering.
        await Ui.Run(() => app.Services.Settings.Appearance = app.Services.Settings.Appearance with { Menu = Webignite.Core.Layout.MenuKind.Sidebar });
    }

    [Fact]
    public async Task The_list_pages_let_go_are_not_held_either()
    {
        using var app = new TestApp();
        app.Add(DocumentKind.Invoice, "Karoo Coffee Roasters", "Website hosting", 450m);
        var invoices = await OpenAndLetGo(app, services => new DocumentsViewModel(DocumentKind.Invoice, services));
        var clients = await OpenAndLetGo(app, services => new ClientsViewModel(services));

        Collect();

        Assert.False(invoices.IsAlive, "Invoices is still in memory after it was let go.");
        Assert.False(clients.IsAlive, "Clients is still in memory after it was let go.");
    }

    // Kept out of the test's own method, so nothing there still points at the page when memory is collected.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> OpenAndLetGo(TestApp app, Func<AppServices, ViewModelBase> open)
    {
        WeakReference? weak = null;
        await Ui.Run(() =>
        {
            var page = open(app.Services);
            weak = new WeakReference(page);
            (page as IDisposable)?.Dispose();
        });
        await Ui.Settle();
        return weak!;
    }

    private static void Collect()
    {
        for (var round = 0; round < 3; round++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
