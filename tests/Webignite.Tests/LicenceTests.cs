using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Webignite.Core.Accounts;
using Webignite.Data;
using Webignite.Server;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>Licences: signed by the server, checked offline by the app, a 30-day trial, 14 days' grace.</summary>
public sealed class LicenceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-licence-").FullName;
    private readonly (string PrivateKey, string PublicKey) keys = Licences.NewKeys();

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public void A_licence_checks_out_only_with_the_servers_key_and_unchanged()
    {
        using var key = LicenceService.KeyFrom(keys.PrivateKey)!;
        var licence = new Licence("w1", LicencePlans.Active, Now.AddDays(30), Now);
        var token = Licences.Sign(licence, key);
        Assert.Equal(licence, Licences.Read(token, keys.PublicKey));

        Assert.Null(Licences.Read(token, Licences.NewKeys().PublicKey));
        var body = Convert.FromBase64String(token.Split('.')[0]);
        var stretched = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(body).Replace("2026-12", "2036-12", StringComparison.Ordinal));
        Assert.Null(Licences.Read(Convert.ToBase64String(stretched) + "." + token.Split('.')[1], keys.PublicKey));
        Assert.Null(Licences.Read("nonsense", keys.PublicKey));
        Assert.Null(Licences.Read(token, ""));
    }

    [Fact]
    public void A_licence_stands_as_trial_paid_grace_or_locked()
    {
        var trial = Licences.For("w", LicencePlans.Trial, Now.AddDays(-7), null, Now);
        Assert.Equal(Now.AddDays(23), trial.ValidUntil);
        Assert.Equal((LicenceState.Trial, 23, "Free trial: 23 days left."), Parts(Licences.Standing(trial, Now)));

        var grace = Licences.Standing(trial, trial.ValidUntil.AddDays(3));
        Assert.Equal(LicenceState.Grace, grace.State);
        Assert.Equal("The free trial has ended. 11 days left before Webignite locks.", grace.Text);

        var locked = Licences.Standing(trial, trial.ValidUntil + Licences.Grace);
        Assert.Equal(LicenceState.Locked, locked.State);
        Assert.False(Licences.Locks(locked, enforced: false));
        Assert.True(Licences.Locks(locked, enforced: true));

        var paid = Licences.For("w", LicencePlans.Active, Now.AddYears(-1), Now.AddDays(10), Now);
        Assert.Equal(LicenceState.Active, Licences.Standing(paid, Now).State);
        Assert.Equal("The subscription has run out. 1 day left before Webignite locks.", Licences.Standing(paid, Now.AddDays(23)).Text);

        Assert.Equal(LicenceState.Active, Licences.Standing(Licences.For("w", LicencePlans.Complimentary, Now.AddYears(-3), null, Now), Now.AddYears(5)).State);
        Assert.Equal(LicenceState.None, Licences.Standing(null, Now).State);
    }

    private static (LicenceState, int, string) Parts(LicenceStanding s) => (s.State, s.DaysLeft, s.Text);

    private WebApplicationFactory<Program> Server(string? licenceKey) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseEnvironment("Testing");
        b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Webignite:Database"] = Path.Combine(folder, "server.db"),
            ["Webignite:SignInPerMinute"] = "1000",
            ["Webignite:LicenceKey"] = licenceKey,
        }));
    });

    [Fact]
    public async Task The_server_gives_a_new_workspace_a_trial_and_a_plan_set_by_hand_reaches_the_app()
    {
        using var server = Server(keys.PrivateKey);
        using var store = new LocalStore(Path.Combine(folder, "app.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        using var client = new AccountClient(store, new MemorySecretStore(), server.Server.CreateHandler(), keys.PublicKey);
        Assert.Null(client.UseServer("http://localhost"));
        Assert.True((await client.SignUpAsync("eddie@webignite.co.za", "karoo rooibos sunrise", "Webignite", null, Cancel)).Worked);

        var trial = await client.RefreshLicenceAsync(TimeSpan.FromHours(12), DateTimeOffset.UtcNow, Cancel);
        Assert.True(trial.Worked, trial.Problem);
        Assert.Equal(LicencePlans.Trial, trial.Value!.Plan);
        Assert.Equal(client.Account!.Workspace.Id, trial.Value.WorkspaceId);
        Assert.InRange(trial.Value.ValidUntil, DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow.AddDays(31));
        Assert.Equal(trial.Value, client.Licence); // kept, and checked again when read

        // Webignite's own workspace, set by the command on the server.
        Assert.True(server.Services.GetRequiredService<ServerStore>().SetPlan("eddie@webignite.co.za", LicencePlans.Complimentary, null));
        Assert.False(server.Services.GetRequiredService<ServerStore>().SetPlan("nobody@example.com", LicencePlans.Active, Now));
        var kept = await client.RefreshLicenceAsync(TimeSpan.FromHours(12), DateTimeOffset.UtcNow, Cancel);
        Assert.Equal(LicencePlans.Trial, kept.Value!.Plan); // fetched less than 12 hours ago: the kept one serves
        var fresh = await client.RefreshLicenceAsync(TimeSpan.Zero, DateTimeOffset.UtcNow, Cancel);
        Assert.Equal(LicencePlans.Complimentary, fresh.Value!.Plan);
    }

    [Fact]
    public async Task A_licence_for_another_workspace_pasted_in_is_not_taken()
    {
        using var server = Server(keys.PrivateKey);
        using var store = new LocalStore(Path.Combine(folder, "app.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        using var client = new AccountClient(store, new MemorySecretStore(), server.Server.CreateHandler(), keys.PublicKey);
        Assert.Null(client.UseServer("http://localhost"));
        Assert.True((await client.SignUpAsync("eddie@webignite.co.za", "karoo rooibos sunrise", "Webignite", null, Cancel)).Worked);
        using var key = LicenceService.KeyFrom(keys.PrivateKey)!;
        var someoneElses = Licences.Sign(new Licence("another-workspace", LicencePlans.Complimentary, DateTimeOffset.UtcNow.AddYears(10), DateTimeOffset.UtcNow), key);
        store.SetSetting("licence", new KeptLicence(someoneElses, DateTimeOffset.UtcNow));
        Assert.Null(client.Licence);

        // From the future: a PC clock wound back doesn't make an old licence good again.
        var ours = Licences.Sign(new Licence(client.Account!.Workspace.Id, LicencePlans.Active, DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow.AddDays(5)), key);
        store.SetSetting("licence", new KeptLicence(ours, DateTimeOffset.UtcNow));
        Assert.Null(client.Licence);
    }

    [Fact]
    public async Task Without_keys_licences_are_simply_not_offered()
    {
        using var server = Server(null);
        using var store = new LocalStore(Path.Combine(folder, "app.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        using var client = new AccountClient(store, new MemorySecretStore(), server.Server.CreateHandler(), keys.PublicKey);
        Assert.Null(client.UseServer("http://localhost"));
        Assert.True((await client.SignUpAsync("eddie@webignite.co.za", "karoo rooibos sunrise", "Webignite", null, Cancel)).Worked);
        Assert.Equal("Licences aren't set up on this server yet.", (await client.RefreshLicenceAsync(TimeSpan.Zero, DateTimeOffset.UtcNow, Cancel)).Problem);
        Assert.Null(client.Licence);

        using var unkeyed = new AccountClient(store, new MemorySecretStore(), server.Server.CreateHandler(), "");
        Assert.False(unkeyed.ChecksLicences);
    }

    [Fact]
    public async Task A_licence_signed_with_another_key_is_not_kept()
    {
        using var server = Server(Licences.NewKeys().PrivateKey); // a server that isn't ours
        using var store = new LocalStore(Path.Combine(folder, "app.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        using var client = new AccountClient(store, new MemorySecretStore(), server.Server.CreateHandler(), keys.PublicKey);
        Assert.Null(client.UseServer("http://localhost"));
        Assert.True((await client.SignUpAsync("eddie@webignite.co.za", "karoo rooibos sunrise", "Webignite", null, Cancel)).Worked);
        Assert.Equal("The licence from the server didn't check out.", (await client.RefreshLicenceAsync(TimeSpan.Zero, DateTimeOffset.UtcNow, Cancel)).Problem);
        Assert.Null(client.Licence);
    }
}
