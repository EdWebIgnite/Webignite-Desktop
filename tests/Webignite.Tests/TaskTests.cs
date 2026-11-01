using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Webignite.Core.Tasks;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class TaskTests : IDisposable
{
    private static readonly DateOnly Today = Samples.Today;
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;

    private static TaskItem Task(string title, DateOnly? due = null, bool done = false) =>
        new() { Id = TaskItem.NewId(), Title = title, Due = due, Done = done, Created = Now };

    private static StoredDocument Stored(Document doc, DocumentStatus status, DateTimeOffset when, string? number = null) =>
        new() { Id = StoredDocument.NewId(), Document = Samples.Completed(doc), Status = status, XeroNumber = number, Created = when, Updated = when };

    [Fact]
    public void Groups_by_due_date()
    {
        Assert.Equal(TaskGroup.Overdue, TaskGroups.Of(Task("a", Today.AddDays(-1)), Today));
        Assert.Equal(TaskGroup.Today, TaskGroups.Of(Task("a", Today), Today));
        Assert.Equal(TaskGroup.Upcoming, TaskGroups.Of(Task("a", Today.AddDays(3)), Today));
        Assert.Equal(TaskGroup.Someday, TaskGroups.Of(Task("a"), Today));
        Assert.Equal(TaskGroup.Done, TaskGroups.Of(Task("a", Today.AddDays(-9), done: true), Today));
    }

    [Fact]
    public void Sorts_soonest_first()
    {
        var sorted = TaskGroups.Sort([Task("later", Today.AddDays(5)), Task("none"), Task("soon", Today)]).Select(t => t.Title);
        Assert.Equal(["soon", "later", "none"], sorted);
    }

    [Fact]
    public void Quote_in_xero_gets_a_follow_up()
    {
        var quote = Stored(Samples.NewInvoice() with { Kind = DocumentKind.Quote }, DocumentStatus.InXero, Now.AddDays(-1), "QU-0012");
        var task = Assert.Single(AutomaticTasks.Suggest([quote], new HashSet<string>(), new TaskSettings(), Today, Now));

        Assert.Equal("Follow up QU-0012 for Karoo Coffee Roasters", task.Title);
        Assert.Equal(Today.AddDays(4), task.Due);
        Assert.Equal(TaskSource.Auto, task.Source);
    }

    [Fact]
    public void Overdue_invoice_gets_a_chase()
    {
        var doc = Samples.NewInvoice() with { Date = Today.AddDays(-20), DueOrExpiryDate = Today.AddDays(-13) };
        var invoice = Stored(doc, DocumentStatus.InXero, Now.AddDays(-20), "INV-0101");
        var task = Assert.Single(AutomaticTasks.Suggest([invoice], new HashSet<string>(), new TaskSettings(), Today, Now));
        Assert.Equal("Chase overdue INV-0101 for Karoo Coffee Roasters, R 450.00", task.Title);
    }

    [Fact]
    public void Invoice_not_yet_due_gets_nothing()
    {
        var invoice = Stored(Samples.NewInvoice(), DocumentStatus.InXero, Now, "INV-0102");
        Assert.Empty(AutomaticTasks.Suggest([invoice], new HashSet<string>(), new TaskSettings(), Today, Now));
    }

    [Fact]
    public void Old_draft_gets_a_reminder_but_a_new_one_does_not()
    {
        var old = Stored(Samples.NewInvoice(), DocumentStatus.Draft, Now.AddDays(-4));
        var fresh = Stored(Samples.NewInvoice(), DocumentStatus.Draft, Now);
        var task = Assert.Single(AutomaticTasks.Suggest([old, fresh], new HashSet<string>(), new TaskSettings(), Today, Now));
        Assert.Equal("Finish the draft invoice for Karoo Coffee Roasters", task.Title);
    }

    [Fact]
    public void Store_raises_each_reminder_once_even_after_it_is_deleted()
    {
        using var store = new LocalStore(Path.Combine(folder, "t.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        store.Save(Stored(Samples.NewInvoice(), DocumentStatus.Draft, Now.AddDays(-5)) with { });
        // Save resets Created to now, so look a week ahead for the draft to count as old.
        var later = Today.AddDays(7);

        Assert.Equal(1, store.RaiseAutomaticTasks(new TaskSettings(), later));
        Assert.Equal(0, store.RaiseAutomaticTasks(new TaskSettings(), later));

        store.DeleteTask(Assert.Single(store.ListTasks()).Id);
        Assert.Empty(store.ListTasks());
        Assert.Equal(0, store.RaiseAutomaticTasks(new TaskSettings(), later));
    }

    [Fact]
    public void Store_saves_ticks_and_deletes_tasks()
    {
        using var store = new LocalStore(Path.Combine(folder, "t.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        var task = store.SaveTask(Task("Call Karoo about the logo", Today));
        store.SaveTask(task with { Done = true, DoneAt = Now });

        Assert.True(Assert.Single(store.ListTasks()).Done);
        store.DeleteTask(task.Id);
        Assert.Empty(store.ListTasks());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }
}
