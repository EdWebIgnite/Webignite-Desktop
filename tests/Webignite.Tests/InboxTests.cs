using System.Globalization;
using System.Security.Cryptography;
using Webignite.Core.Projects;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>Inbox, as Asana's: what your partner did that concerns you.</summary>
public sealed class InboxTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-inbox-").FullName;
    private readonly LocalStore store;
    private readonly Clock clock = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly TeamSettings team = new()
    {
        Me = new Teammate { Id = "eddie", Name = "Eddie" },
        Partner = new Teammate { Id = "lize", Name = "Lize" },
    };
    private readonly ProjectService mine;
    private readonly ProjectService hers;
    private readonly InboxService inbox;

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public InboxTests()
    {
        store = new LocalStore(Path.Combine(folder, "webignite.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        store.SetSetting("team", team);
        mine = new ProjectService(store, () => "eddie", clock, Path.Combine(folder, "a"));
        hers = new ProjectService(store, () => "lize", clock, Path.Combine(folder, "a"));
        inbox = new InboxService(store, () => "eddie", () => team, clock);
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private static (Project Project, ProjectTask Task) Setup(ProjectService by, string title, string? assigned = null)
    {
        var project = by.Add(new Project { Id = ProjectRules.NewId(), Name = "Kloof Street Dental", Shared = true }).Saved!;
        var task = by.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, Title = title, AssignedTo = assigned }).Saved!;
        return (project, task);
    }

    private void Later() => clock.Now = clock.Now.AddMinutes(1);

    [Fact]
    public void What_my_partner_does_on_my_tasks_shows_and_what_I_do_does_not()
    {
        _ = inbox.State; // Inbox starts now, looking back two weeks
        var (_, task) = Setup(mine, "Write the copy");
        Later();
        hers.AddComment(task.Id, "Draft is in the shared folder");
        Later();
        hers.ChangeTask(task.Id, t => t with { Done = true });
        Later();
        mine.AddComment(task.Id, "Thanks");

        var items = inbox.Items();
        Assert.Equal([InboxKind.Completed, InboxKind.Commented], items.Select(i => i.Kind));
        Assert.Equal("Lize", items[0].Who);
        Assert.Equal("marked this task complete", items[0].Line);
        Assert.Equal("Draft is in the shared folder", items[1].Snippet);
        Assert.Equal("Write the copy", items[1].TaskTitle);
        Assert.Equal(2, inbox.Unread());
    }

    [Fact]
    public void Being_given_a_task_mentioned_or_liked_shows_even_on_tasks_I_dont_follow()
    {
        _ = inbox.State;
        var (_, task) = Setup(hers, "Book the photographer");
        Later();
        hers.AddComment(task.Id, "@Eddie can you confirm the date?");
        Later();
        var (_, other) = Setup(hers, "Order the banners");
        hers.ChangeTask(other.Id, t => t with { AssignedTo = "eddie" });
        Later();
        var myComment = mine.AddComment(task.Id, "Friday works").Saved!;
        Later();
        hers.LikeComment(myComment.Id, true);

        var kinds = inbox.Items().Select(i => (i.Kind, i.Line)).ToList();
        Assert.Contains((InboxKind.Mentioned, "mentioned you"), kinds);
        Assert.Contains((InboxKind.Assigned, "assigned this task to you"), kinds);
        Assert.Contains((InboxKind.Liked, "liked your comment"), kinds);
        Assert.Equal(InboxKind.Liked, inbox.Items()[0].Kind);

        // She takes the like back: it goes from Inbox.
        hers.LikeComment(myComment.Id, false);
        Assert.DoesNotContain(inbox.Items(), i => i.Kind == InboxKind.Liked);
    }

    [Fact]
    public void Being_added_to_a_project_shows_once_and_again_if_shared_again()
    {
        _ = inbox.State;
        var project = hers.Add(new Project { Id = ProjectRules.NewId(), Name = "Bosveld Builders website" }).Saved!;
        Assert.DoesNotContain(inbox.Items(), i => i.Kind == InboxKind.Added); // hers alone so far
        Later();
        hers.Change(project.Id, p => p with { Shared = true });
        var added = Assert.Single(inbox.Items(), i => i.Kind == InboxKind.Added);
        Assert.Equal("Lize added you to Bosveld Builders website", InboxRules.Headline(added));
        Assert.Equal(project.Id, added.ProjectId);

        // What I share myself isn't news to me.
        mine.Add(new Project { Id = ProjectRules.NewId(), Name = "My own admin", Shared = true });
        Assert.Single(inbox.Items(), i => i.Kind == InboxKind.Added);

        Later();
        hers.Change(project.Id, p => p with { Shared = false });
        Later();
        hers.Change(project.Id, p => p with { Shared = true });
        var again = Assert.Single(inbox.Items(), i => i.Kind == InboxKind.Added);
        Assert.NotEqual(added.Key, again.Key);
    }

    [Fact]
    public void Claude_and_ChatGPT_and_old_news_stay_out()
    {
        var (_, task) = Setup(mine, "Old task");
        hers.AddComment(task.Id, "From long ago");
        clock.Now = clock.Now.AddDays(30);
        _ = inbox.State; // first looked at a month later: starts two weeks back
        var claude = new ProjectService(store, () => Actors.Claude, clock, Path.Combine(folder, "a"));
        claude.AddComment(task.Id, "I drafted the copy");
        Assert.Empty(inbox.Items());
    }

    [Fact]
    public void Read_and_archived_are_remembered_and_counted()
    {
        _ = inbox.State;
        var (_, task) = Setup(mine, "Launch");
        Later();
        hers.AddComment(task.Id, "One");
        Later();
        hers.AddComment(task.Id, "Two");
        var items = inbox.Items();
        var changes = 0;
        inbox.Changed += () => changes++;

        inbox.MarkRead([items[0].Key]);
        Assert.Equal(1, inbox.Unread());
        inbox.Archive(items[1].Key);
        Assert.Equal(0, inbox.Unread());
        Assert.Contains(items[1].Key, inbox.State.Archived);
        inbox.Unarchive(items[1].Key);
        Assert.DoesNotContain(items[1].Key, inbox.State.Archived);
        Assert.Equal(0, inbox.Unread()); // archiving read it
        Assert.Equal(3, changes);

        // The keys stay the same each time Inbox is worked out.
        Assert.Equal(items.Select(i => i.Key), inbox.Items().Select(i => i.Key));
    }

    [Fact]
    public void Only_what_arrives_after_the_first_look_is_announced()
    {
        _ = inbox.State;
        var (_, task) = Setup(mine, "Launch");
        Later();
        hers.AddComment(task.Id, "Waiting already");
        Assert.Empty(inbox.NewlyArrived());
        Later();
        hers.AddComment(task.Id, "@Eddie new one");
        var fresh = Assert.Single(inbox.NewlyArrived());
        Assert.Equal("Lize mentioned you on Launch", InboxRules.Headline(fresh));
        Assert.Empty(inbox.NewlyArrived());
    }

    [Fact]
    public void Items_group_by_day_and_marks_are_kept_to_a_limit()
    {
        var now = new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal("Today", InboxRules.Group(now.AddHours(-1), now, TimeZoneInfo.Utc));
        Assert.Equal("Yesterday", InboxRules.Group(now.AddDays(-1), now, TimeZoneInfo.Utc));
        Assert.Equal("This week", InboxRules.Group(now.AddDays(-4), now, TimeZoneInfo.Utc));
        Assert.Equal("Earlier", InboxRules.Group(now.AddDays(-9), now, TimeZoneInfo.Utc));

        var state = InboxRules.MarkRead(new InboxState(), Enumerable.Range(0, InboxRules.Remembered + 10).Select(i => i.ToString(CultureInfo.InvariantCulture)));
        Assert.Equal(InboxRules.Remembered, state.Read.Count);
        Assert.Equal("10", state.Read[0]);
    }
}
