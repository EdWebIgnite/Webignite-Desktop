using System.Security.Cryptography;
using Webignite.Core.Clients;
using Webignite.Core.Tasks;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class AssistantTasksTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;
    private readonly LocalStore store;
    private readonly AssistantTasks tasks;

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public AssistantTasksTests()
    {
        store = new LocalStore(Path.Combine(folder, "test.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        tasks = new AssistantTasks(store, new FixedClock());
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Assistants_add_to_dos_marked_as_theirs()
    {
        store.SaveClient(new Client { Id = "c", Name = "Karoo Coffee Roasters" });
        var added = tasks.Add(" Send the sitemap ", TaskSource.Claude, "2026-11-05", "karoo coffee roasters", "They asked by mail");
        Assert.False(added.AlreadyThere);
        var saved = Assert.Single(store.ListTasks());
        Assert.Equal("Send the sitemap", saved.Title);
        Assert.Equal(TaskSource.Claude, saved.Source);
        Assert.Equal(new DateOnly(2026, 11, 5), saved.Due);
        Assert.Equal("Karoo Coffee Roasters", saved.Customer);
    }

    [Fact]
    public void The_same_open_to_do_isnt_added_twice()
    {
        tasks.Add("Renew karoocoffee.co.za", TaskSource.ChatGpt);
        var again = tasks.Add("renew karoocoffee.co.za", TaskSource.Claude);
        Assert.True(again.AlreadyThere);
        Assert.Single(store.ListTasks());
    }

    [Theory]
    [InlineData("", null, "title is empty")]
    [InlineData("Call Pieter", "14/11/2026", "2026-11-14")]
    [InlineData("Call Pieter", "2026-10-01", "already passed")]
    public void Bad_input_is_explained(string title, string? due, string words) =>
        Assert.Contains(words, Assert.Throws<ArgumentException>(() => tasks.Add(title, TaskSource.Claude, due)).Message);

    [Fact]
    public void Only_assistants_use_this_door() =>
        Assert.Throws<ArgumentException>(() => tasks.Add("Hello", TaskSource.App));

    [Fact]
    public void The_list_puts_open_and_soonest_first()
    {
        store.SaveTask(new TaskItem { Id = "a", Title = "Later", Due = new DateOnly(2026, 12, 1), Created = DateTimeOffset.UtcNow });
        store.SaveTask(new TaskItem { Id = "b", Title = "Overdue", Due = new DateOnly(2026, 10, 30), Created = DateTimeOffset.UtcNow });
        store.SaveTask(new TaskItem { Id = "c", Title = "Finished", Done = true, Created = DateTimeOffset.UtcNow });
        store.SaveTask(new TaskItem { Id = "d", Title = "For Karoo", Customer = "Karoo Coffee Roasters", Created = DateTimeOffset.UtcNow });

        var open = tasks.List();
        Assert.Equal(["Overdue", "Later", "For Karoo"], open.Select(t => t.Title));
        Assert.True(open[0].Overdue);
        Assert.Equal("Eddie", open[0].From);
        Assert.Equal(4, tasks.List(includeDone: true).Count);
        Assert.Equal("For Karoo", Assert.Single(tasks.List(customer: "karoo")).Title);
    }
}
