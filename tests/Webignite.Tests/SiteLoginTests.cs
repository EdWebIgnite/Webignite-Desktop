using System.Security.Cryptography;
using System.Text;
using Webignite.Core.Clients;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>Site logins under a client: kept apart from the client, encrypted, and gone with it.</summary>
public sealed class SiteLoginTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-logins-").FullName;
    private readonly LocalStore store;
    private readonly ClientService clients;
    private bool closed;

    public SiteLoginTests()
    {
        store = new LocalStore(Path.Combine(folder, "webignite.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        clients = new ClientService(store);
    }

    public void Dispose()
    {
        if (!closed)
            store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private static SiteLogin Login(string clientId, string site, string? address = null, string? user = null, string? password = null) =>
        new() { Id = SiteLogin.NewId(), ClientId = clientId, Site = site, Address = address, UserName = user, Password = password };

    [Theory]
    [InlineData("karoocoffee.co.za/wp-admin", "https://karoocoffee.co.za/wp-admin")]
    [InlineData("  https://cp.afrihost.com  ", "https://cp.afrihost.com/")]
    [InlineData("http://192.168.0.10:2083", "http://192.168.0.10:2083/")]
    public void A_login_address_opens_as_a_web_address(string typed, string opened) =>
        Assert.Equal(opened, SiteLogins.Link(typed)?.ToString());

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("ftp://karoocoffee.co.za")]
    [InlineData("karoo coffee")]
    [InlineData("")]
    public void Anything_but_a_web_address_is_never_opened(string typed) => Assert.Null(SiteLogins.Link(typed));

    [Fact]
    public void The_address_shows_without_https_or_a_trailing_slash()
    {
        Assert.Equal("karoocoffee.co.za/wp-admin", SiteLogins.Shown("https://karoocoffee.co.za/wp-admin/"));
        Assert.Equal("cp.afrihost.com", SiteLogins.Shown("cp.afrihost.com"));
        Assert.Equal("192.168.0.10:2083", SiteLogins.Shown("http://192.168.0.10:2083"));
    }

    [Fact]
    public void A_login_needs_a_site_name_and_a_sensible_address()
    {
        var problems = SiteLogins.Check(Login("c1", " ", address: "not an address"));
        Assert.Equal(2, problems.Count);
        Assert.Empty(SiteLogins.Check(Login("c1", "WordPress admin")));
    }

    [Fact]
    public void Tidying_trims_the_text_but_keeps_the_password_exactly()
    {
        var tidy = SiteLogins.Tidy(Login("c1", "  cPanel ", "  ", " admin ", " pass word ") with { Notes = "   " });
        Assert.Equal(("cPanel", null, "admin", " pass word ", null), (tidy.Site, tidy.Address, tidy.UserName, tidy.Password, tidy.Notes));
    }

    [Fact]
    public void Search_finds_the_site_and_user_but_never_looks_at_the_password()
    {
        var login = Login("c1", "WordPress admin", "karoocoffee.co.za/wp-admin", "thandi", "SecretKaroo1!");
        Assert.True(SiteLogins.Matches(login, "wordpress thandi"));
        Assert.False(SiteLogins.Matches(login, "SecretKaroo"));
    }

    [Fact]
    public void Several_logins_belong_to_one_client_and_list_A_to_Z()
    {
        var karoo = clients.Add("Karoo Coffee Roasters");
        var bosveld = clients.Add("Bosveld Builders");
        clients.SaveLogin(Login(karoo.Id, "WordPress admin", user: "thandi"));
        clients.SaveLogin(Login(karoo.Id, "Afrihost cPanel", user: "karoo"));
        clients.SaveLogin(Login(bosveld.Id, "Wix", user: "pieter"));

        Assert.Equal(["Afrihost cPanel", "WordPress admin"], clients.Logins(karoo.Id).Select(l => l.Site));
        Assert.Equal(["Wix"], clients.Logins(bosveld.Id).Select(l => l.Site));
    }

    [Fact]
    public void A_changed_login_keeps_its_place_and_a_bad_one_is_not_saved()
    {
        var karoo = clients.Add("Karoo Coffee Roasters");
        var (saved, _) = clients.SaveLogin(Login(karoo.Id, "WordPress admin", password: "old"));
        clients.SaveLogin(saved! with { Password = "new" });
        Assert.Equal("new", Assert.Single(clients.Logins(karoo.Id)).Password);

        var (none, problems) = clients.SaveLogin(saved with { Site = "" });
        Assert.Null(none);
        Assert.NotEmpty(problems);
        Assert.Equal("WordPress admin", Assert.Single(clients.Logins(karoo.Id)).Site);
    }

    [Fact]
    public void Passwords_are_never_on_disk_in_plain_text_or_in_the_client_record()
    {
        var karoo = clients.Add("Karoo Coffee Roasters");
        clients.SaveLogin(Login(karoo.Id, "WordPress admin", "karoocoffee.co.za/wp-admin", "thandi", "SecretKaroo1!"));
        store.Dispose();
        closed = true;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var bytes = Directory.GetFiles(folder).SelectMany(File.ReadAllBytes).ToArray();
        foreach (var text in new[] { "SecretKaroo1!", "thandi", "wp-admin" })
        {
            Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)));
            Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(text)));
        }
        Assert.DoesNotContain(typeof(Client).GetProperties(), p => p.PropertyType == typeof(SiteLogin) || p.PropertyType == typeof(IReadOnlyList<SiteLogin>));
    }

    [Fact]
    public void Deleting_a_client_takes_its_logins_and_undo_brings_both_back()
    {
        var karoo = clients.Add("Karoo Coffee Roasters");
        var other = clients.Add("Bosveld Builders");
        clients.SaveLogin(Login(karoo.Id, "WordPress admin"));
        clients.SaveLogin(Login(karoo.Id, "cPanel"));
        clients.SaveLogin(Login(other.Id, "Wix"));

        var gone = clients.Delete(karoo.Id);
        Assert.Equal(2, gone.Count);
        Assert.Empty(clients.Logins(karoo.Id));
        Assert.Single(clients.Logins(other.Id));

        clients.Restore(karoo, gone);
        Assert.Equal(2, clients.Logins(karoo.Id).Count);
    }

    [Fact]
    public void A_deleted_login_comes_back_with_undo_but_not_once_the_client_is_gone()
    {
        var karoo = clients.Add("Karoo Coffee Roasters");
        var (saved, _) = clients.SaveLogin(Login(karoo.Id, "WordPress admin"));
        var gone = clients.DeleteLogin(saved!.Id)!;
        Assert.Empty(clients.Logins(karoo.Id));
        Assert.NotNull(clients.RestoreLogin(gone));
        Assert.Single(clients.Logins(karoo.Id));

        clients.DeleteLogin(saved.Id);
        clients.Delete(karoo.Id);
        Assert.Null(clients.RestoreLogin(gone));
        Assert.Null(clients.SaveLogin(Login(karoo.Id, "cPanel")).Saved);
    }
}
