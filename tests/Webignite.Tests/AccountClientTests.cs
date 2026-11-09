using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Webignite.Core.Accounts;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>The app's side of accounts, against the real sync server running in memory.</summary>
public sealed class AccountClientTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-client-").FullName;
    private readonly WebApplicationFactory<Program> server;
    private readonly LocalStore store;
    private readonly MemorySecretStore secrets = new();

    public AccountClientTests()
    {
        server = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Webignite:Database"] = Path.Combine(folder, "server.db"),
                ["Webignite:SignInPerMinute"] = "1000",
            }));
        });
        store = new LocalStore(Path.Combine(folder, "app.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
    }

    public void Dispose()
    {
        store.Dispose();
        server.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private AccountClient Client(ISecretStore? vault = null)
    {
        var client = new AccountClient(store, vault ?? secrets, server.Server.CreateHandler());
        Assert.Null(client.UseServer("http://localhost"));
        return client;
    }

    [Fact]
    public async Task Sign_up_stay_signed_in_and_sign_in_with_two_factor()
    {
        using var client = Client();
        var signedUp = await client.SignUpAsync("eddie@webignite.co.za", "karoo rooibos sunrise", "Webignite", null, TestContext.Current.CancellationToken);
        Assert.True(signedUp.Worked, signedUp.Problem);
        Assert.NotNull(client.EncryptionKey);
        Assert.True(client.HasWorkspaceKey);
        var workspaceKey = client.WorkspaceKey;
        Assert.Matches("^([A-Z2-7]{4}-){12}[A-Z2-7]{4}$", signedUp.Value!.RecoveryKey);
        Assert.True(client.IsSignedIn);
        Assert.Equal("eddie@webignite.co.za", client.Settings.Email);

        // Another start of the app: the kept sign-in still works, without the password.
        using (var later = Client())
            Assert.Equal("Webignite", (await later.RefreshAccountAsync(TestContext.Current.CancellationToken)).Value!.Workspace.Name);

        var setup = (await client.StartTwoFactorAsync(TestContext.Current.CancellationToken)).Value!;
        var key = Base32.Decode(setup.Secret)!;
        var codes = await client.ConfirmTwoFactorAsync(Totp.Code(key, Totp.StepOf(DateTimeOffset.UtcNow)), TestContext.Current.CancellationToken);
        Assert.Equal(8, codes.Value!.Codes.Count);
        Assert.True(client.Account!.TwoFactor);
        await client.SignOutAsync(TestContext.Current.CancellationToken);
        Assert.False(client.IsSignedIn);
        Assert.False(client.HasWorkspaceKey);

        using var fresh = Client(new MemorySecretStore());
        var first = await fresh.SignInAsync("eddie@webignite.co.za", "karoo rooibos sunrise", cancel: TestContext.Current.CancellationToken);
        Assert.Equal(Needs.Code, first.Need);
        Assert.False((await fresh.SignInAsync("eddie@webignite.co.za", "wrong password here", codes.Value.Codes[0], TestContext.Current.CancellationToken)).Worked);
        var second = await fresh.SignInAsync("eddie@webignite.co.za", "karoo rooibos sunrise", codes.Value.Codes[1], TestContext.Current.CancellationToken);
        Assert.True(second.Worked, second.Problem);
        Assert.Equal(signedUp.Value!.Account.Id, second.Value!.Id);
        Assert.Equal(workspaceKey, fresh.WorkspaceKey);
    }

    [Fact]
    public async Task A_partner_joins_with_an_invite_code()
    {
        using var eddie = Client();
        await eddie.SignUpAsync("eddie@webignite.co.za", "karoo rooibos sunrise", "Webignite", null, TestContext.Current.CancellationToken);
        var invite = (await eddie.InviteAsync(TestContext.Current.CancellationToken)).Value!;

        using var jaco = Client(new MemorySecretStore());
        var joined = await jaco.SignUpAsync("jaco@webignite.co.za", "bosveld thunder storm", null, invite.Code, TestContext.Current.CancellationToken);
        Assert.True(joined.Worked, joined.Problem);
        Assert.Equal(eddie.Account!.Workspace.Id, joined.Value!.Account.Workspace.Id);
        // The partner has the same workspace key; the server never had it unlocked.
        Assert.Equal(eddie.WorkspaceKey, jaco.WorkspaceKey);
    }

    [Fact]
    public void Only_https_or_this_pc_is_accepted_and_weak_passwords_are_stopped_here()
    {
        using var client = new AccountClient(store, secrets);
        Assert.Null(client.UseServer("https://sync.webignite.co.za/"));
        Assert.Equal("https://sync.webignite.co.za", client.Settings.ServerAddress);
        Assert.NotNull(client.UseServer("http://sync.webignite.co.za"));
        Assert.NotNull(client.UseServer("https://user:pass@sync.webignite.co.za"));
        Assert.Null(client.UseServer("http://127.0.0.1:5080"));
        Assert.NotNull(AccountRules.PasswordProblem("short"));
    }

    [Fact]
    public async Task A_forgotten_password_is_replaced_with_the_recovery_key_and_the_workspace_key_survives()
    {
        using var client = Client();
        var signedUp = (await client.SignUpAsync("eddie@webignite.co.za", "karoo rooibos sunrise", "Webignite", null, TestContext.Current.CancellationToken)).Value!;
        var workspaceKey = client.WorkspaceKey;

        using var lostPc = Client(new MemorySecretStore());
        Assert.False((await lostPc.RecoverAsync("eddie@webignite.co.za", WorkspaceKeys.NewRecoveryKey(), "new password for karoo", cancel: TestContext.Current.CancellationToken)).Worked);
        var recovered = await lostPc.RecoverAsync("eddie@webignite.co.za", signedUp.RecoveryKey.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal),
            "new password for karoo", cancel: TestContext.Current.CancellationToken);
        Assert.True(recovered.Worked, recovered.Problem);
        Assert.Equal(workspaceKey, lostPc.WorkspaceKey);
        // A new recovery key comes back; the old one no longer works.
        Assert.NotEqual(signedUp.RecoveryKey, recovered.Value!.RecoveryKey);
        using var another = Client(new MemorySecretStore());
        Assert.False((await another.RecoverAsync("eddie@webignite.co.za", signedUp.RecoveryKey, "third password for karoo", cancel: TestContext.Current.CancellationToken)).Worked);
        Assert.True((await another.RecoverAsync("eddie@webignite.co.za", recovered.Value.RecoveryKey, "third password for karoo", cancel: TestContext.Current.CancellationToken)).Worked);

        using var later = Client(new MemorySecretStore());
        Assert.False((await later.SignInAsync("eddie@webignite.co.za", "karoo rooibos sunrise", cancel: TestContext.Current.CancellationToken)).Worked);
        Assert.True((await later.SignInAsync("eddie@webignite.co.za", "third password for karoo", cancel: TestContext.Current.CancellationToken)).Worked);
        Assert.Equal(workspaceKey, later.WorkspaceKey);
    }

    [Fact]
    public async Task A_new_password_keeps_the_workspace_key_and_the_old_one_stops_working()
    {
        using var client = Client();
        Assert.True((await client.SignUpAsync("eddie@webignite.co.za", "karoo rooibos sunrise", "Webignite", null, TestContext.Current.CancellationToken)).Worked);
        var workspaceKey = client.WorkspaceKey;
        Assert.False((await client.ChangePasswordAsync("not my password", "a fresh password for karoo", TestContext.Current.CancellationToken)).Worked);
        var changed = await client.ChangePasswordAsync("karoo rooibos sunrise", "a fresh password for karoo", TestContext.Current.CancellationToken);
        Assert.True(changed.Worked, changed.Problem);

        using var later = Client(new MemorySecretStore());
        Assert.False((await later.SignInAsync("eddie@webignite.co.za", "karoo rooibos sunrise", cancel: TestContext.Current.CancellationToken)).Worked);
        Assert.True((await later.SignInAsync("eddie@webignite.co.za", "a fresh password for karoo", cancel: TestContext.Current.CancellationToken)).Worked);
        Assert.Equal(workspaceKey, later.WorkspaceKey);
    }

    [Fact]
    public async Task An_invite_code_missing_its_secret_half_is_refused_before_anything_is_sent()
    {
        using var eddie = Client();
        await eddie.SignUpAsync("eddie@webignite.co.za", "karoo rooibos sunrise", "Webignite", null, TestContext.Current.CancellationToken);
        var invite = (await eddie.InviteAsync(TestContext.Current.CancellationToken)).Value!;
        using var jaco = Client(new MemorySecretStore());
        var serverHalfOnly = string.Join('-', invite.Code.Split('-').Take(3));
        Assert.False((await jaco.SignUpAsync("jaco@webignite.co.za", "bosveld thunder storm", null, serverHalfOnly, TestContext.Current.CancellationToken)).Worked);
        var tampered = invite.Code[..^1] + (invite.Code[^1] == 'A' ? 'B' : 'A');
        Assert.False((await jaco.SignUpAsync("jaco@webignite.co.za", "bosveld thunder storm", null, tampered, TestContext.Current.CancellationToken)).Worked);
        Assert.True((await jaco.SignUpAsync("jaco@webignite.co.za", "bosveld thunder storm", null, invite.Code, TestContext.Current.CancellationToken)).Worked);
    }
}
