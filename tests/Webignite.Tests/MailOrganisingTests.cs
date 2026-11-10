using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Webignite.Core.Mail;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class MailCategoryTests
{
    private static MailSummary Mail(params string[] keywords) => new()
    {
        AccountId = "a",
        Folder = "INBOX",
        Uid = 1,
        Keywords = keywords,
    };

    [Theory]
    [InlineData("Waiting", "waiting")]
    [InlineData("  Waiting on client ", "waiting_on_client")]
    [InlineData("Accounts & tax", "accounts_tax")]
    [InlineData("Café", "cafe")]
    [InlineData("!!!", "category")]
    public void Keywords_are_plain_words_the_server_accepts(string name, string keyword) =>
        Assert.Equal(keyword, MailCategories.KeywordFor(name, []));

    [Fact]
    public void A_keyword_already_used_gets_a_number()
    {
        Assert.Equal("waiting_2", MailCategories.KeywordFor("Waiting", ["Waiting"]));
        Assert.Equal("waiting_3", MailCategories.KeywordFor("Waiting", ["waiting", "waiting_2"]));
    }

    [Fact]
    public void You_make_your_own_categories_and_each_gets_the_next_free_colour()
    {
        var list = MailCategories.Add([], "Waiting");
        list = MailCategories.Add(list, "Accounts");
        list = MailCategories.Add(list, "Personal", "teal");
        Assert.Equal(["blue", "purple", "teal"], list.Select(c => c.Colour));
        Assert.Equal(["waiting", "accounts", "personal"], list.Select(c => c.Keyword));
        Assert.Equal("orange", MailCategories.NextColour(list));
    }

    [Fact]
    public void There_are_sixteen_colours_red_and_green_too_but_a_new_category_starts_calm()
    {
        Assert.Equal(16, MailCategories.Colours.Count);
        Assert.Contains("red", MailCategories.Colours);
        Assert.Contains("green", MailCategories.Colours);
        Assert.Equal("red", MailCategories.Add([], "Urgent", "red")[0].Colour);
        Assert.Equal("blue", MailCategories.Add([], "Later")[0].Colour);
        Assert.Equal("blue", MailCategories.Add([], "Later", "no such colour")[0].Colour);
        Assert.Equal(MailCategories.Colours.Order(), MailCategories.SuggestedOrder.Order());
    }

    [Fact]
    public void Names_must_be_there_short_and_not_taken()
    {
        var list = MailCategories.Add([], "Waiting");
        Assert.Equal("Give the category a name.", MailCategories.Check(list, "  "));
        Assert.Equal("There is already a category with that name.", MailCategories.Check(list, "waiting"));
        Assert.NotNull(MailCategories.Check(list, new string('x', 31)));
        Assert.Null(MailCategories.Check(list, "Waiting", editing: "waiting"));
        Assert.Throws<ArgumentException>(() => MailCategories.Add(list, "WAITING"));
    }

    [Fact]
    public void Renaming_keeps_the_keyword_so_mail_stays_in_the_category()
    {
        var list = MailCategories.Add([], "Waiting");
        var renamed = MailCategories.Change(list, "waiting", "Waiting on client", "pink");
        Assert.Equal(new MailCategory("waiting", "Waiting on client", "pink"), renamed.Single());
        Assert.Single(MailCategories.Of(renamed, Mail("Waiting")));
        Assert.Empty(MailCategories.Remove(renamed, "waiting"));
    }

    [Fact]
    public void Mail_goes_in_and_out_of_categories_once()
    {
        var mail = MailCategories.With(Mail("$label1"), "waiting", true);
        Assert.Equal(["$label1", "waiting"], mail.Keywords);
        Assert.Same(mail.Keywords, MailCategories.With(mail, "WAITING", true).Keywords);
        Assert.Equal(["$label1"], MailCategories.With(mail, "Waiting", false).Keywords);
    }

    [Fact]
    public void Only_your_categories_show_as_dots_in_list_order()
    {
        var list = MailCategories.Add(MailCategories.Add([], "Waiting"), "Accounts");
        var dots = MailCategories.Of(list, Mail("accounts", "$junk", "waiting"));
        Assert.Equal(["Waiting", "Accounts"], dots.Select(c => c.Name));
    }

    [Fact]
    public void Keyword_lists_match_in_any_order_and_case() =>
        Assert.True(MailCategories.SameKeywords(["Waiting", "accounts"], ["accounts", "waiting"]));

    [Fact]
    public void Categories_are_saved_in_snake_case()
    {
        var json = JsonSerializer.Serialize(MailCategories.Add([], "Waiting").ToList(), DocumentJson.Options);
        using var parsed = JsonDocument.Parse(json);
        var first = parsed.RootElement[0];
        Assert.Equal(("waiting", "Waiting", "blue"),
            (first.GetProperty("keyword").GetString(), first.GetProperty("name").GetString(), first.GetProperty("colour").GetString()));
    }

    [Theory]
    [InlineData(MailFilter.All, true, true, true)]
    [InlineData(MailFilter.Unread, false, true, false)]
    [InlineData(MailFilter.Important, false, false, true)]
    public void Quick_filters_keep_the_right_mail(MailFilter filter, bool keepsRead, bool keepsUnread, bool keepsImportant)
    {
        var read = Mail() with { Seen = true };
        Assert.Equal(keepsRead, MailFilters.Keep(read, filter));
        Assert.Equal(keepsUnread, MailFilters.Keep(read with { Seen = false }, filter));
        Assert.Equal(keepsImportant, MailFilters.Keep(read with { Flagged = true }, filter));
    }

    [Fact]
    public void The_attachments_filter_keeps_only_mail_with_files()
    {
        Assert.False(MailFilters.Keep(Mail(), MailFilter.Attachments));
        Assert.True(MailFilters.Keep(Mail() with { HasAttachments = true }, MailFilter.Attachments));
    }
}

public sealed class MailFolderNameTests
{
    private static MailFolder Folder(string path, FolderRole role = FolderRole.Other) =>
        new() { AccountId = "a", Path = path, Name = MailFolders.DisplayName(path, '.', role), Role = role };

    private static readonly MailFolder[] Xneelo =
    [
        Folder("INBOX", FolderRole.Inbox), Folder("INBOX.Sent", FolderRole.Sent), Folder("INBOX.Archive", FolderRole.Archive),
        Folder("INBOX.Clients"), Folder("INBOX.Clients.Bosveld"),
    ];

    [Fact]
    public void Folder_names_are_checked_before_asking_the_server()
    {
        var siblings = FolderNames.Children(Xneelo, "INBOX");
        Assert.Equal(["Sent", "Archive", "Clients"], siblings);
        Assert.Null(FolderNames.Check("Banking", siblings));
        Assert.Equal("Give the folder a name.", FolderNames.Check(" ", siblings));
        Assert.Equal("There is already a folder with that name here.", FolderNames.Check("clients", siblings));
        Assert.NotNull(FolderNames.Check("Bank.Statements", siblings));
        Assert.NotNull(FolderNames.Check("Inbox", siblings));
        Assert.Null(FolderNames.Check("Clients", siblings, renaming: "Clients"));
    }

    [Fact]
    public void A_folder_knows_where_it_sits()
    {
        Assert.Equal("INBOX.Clients", FolderNames.Parent("INBOX.Clients.Bosveld"));
        Assert.Equal("", FolderNames.Parent("INBOX"));
        Assert.Equal(["Bosveld"], FolderNames.Children(Xneelo, "INBOX.Clients"));
    }

    [Fact]
    public void Only_your_own_folders_can_be_renamed_or_removed()
    {
        Assert.True(FolderNames.CanChange(Folder("INBOX.Clients")));
        Assert.False(FolderNames.CanChange(Folder("INBOX", FolderRole.Inbox)));
        Assert.False(FolderNames.CanChange(Folder("INBOX.Archive", FolderRole.Archive)));
        Assert.Equal("INBOX.Archive", FolderNames.Archive(Xneelo)!.Path);
    }

    [Fact]
    public void Mail_can_move_anywhere_but_where_it_is_and_drafts()
    {
        var targets = FolderNames.MoveTargets([.. Xneelo, Folder("INBOX.Drafts", FolderRole.Drafts)], "INBOX");
        Assert.DoesNotContain(targets, f => f.Path is "INBOX" or "INBOX.Drafts");
        Assert.Contains(targets, f => f.Path == "INBOX.Clients.Bosveld");
    }
}

public sealed class MailKeywordStoreTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;

    [Fact]
    public void Categories_on_mail_are_kept_on_this_pc_and_found_again()
    {
        using var store = new LocalStore(Path.Combine(folder, "mail.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        var account = store.SaveMailAccount(MailAccounts.Suggest("eddie@webignite.co.za", "Eddie"));
        store.SaveMailSummaries(
        [
            new MailSummary { AccountId = account.Id, Folder = "INBOX", Uid = 1, Keywords = ["waiting"], Flagged = true },
            new MailSummary { AccountId = account.Id, Folder = "INBOX", Uid = 2 },
        ]);
        Assert.Equal(["waiting"], store.GetMailSummary(account.Id, "INBOX", 1)!.Keywords);
        Assert.Empty(store.GetMailSummary(account.Id, "INBOX", 2)!.Keywords);
        Assert.Equal([1u], store.FindMail([account.Id], m => MailCategories.Has(m, "Waiting"), cancel: TestContext.Current.CancellationToken).Select(m => m.Uid));
        Assert.Equal([1u], store.FindMail([account.Id], m => m.Flagged, cancel: TestContext.Current.CancellationToken).Select(m => m.Uid));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void A_folded_mailbox_keeps_its_inbox_and_the_open_folder_in_sight()
    {
        Assert.True(MailboxFolding.Shows(folded: true, FolderRole.Inbox, isCurrent: false));
        Assert.False(MailboxFolding.Shows(folded: true, FolderRole.Sent, isCurrent: false));
        Assert.True(MailboxFolding.Shows(folded: true, FolderRole.Other, isCurrent: true));
        Assert.True(MailboxFolding.Shows(folded: false, FolderRole.Trash, isCurrent: false));

        var folded = MailboxFolding.Toggle(null, "eddie");
        Assert.Equal(["eddie"], folded);
        Assert.Equal(["eddie", "accounts"], MailboxFolding.Toggle(folded, "accounts"));
        Assert.Empty(MailboxFolding.Toggle(folded, "eddie"));
    }
}
