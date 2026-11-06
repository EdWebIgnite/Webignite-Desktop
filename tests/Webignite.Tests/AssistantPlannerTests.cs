using System.Security.Cryptography;
using Webignite.Core.Clients;
using Webignite.Core.Tasks;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

public sealed class AssistantPlannerTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-").FullName;
    private readonly LocalStore store;
    private readonly AssistantPlanner planner;

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public AssistantPlannerTests()
    {
        store = new LocalStore(Path.Combine(folder, "test.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        planner = new AssistantPlanner(store, new FixedClock(), Path.Combine(folder, "attachments"));
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void A_project_from_a_template_comes_with_its_sections_tasks_and_client()
    {
        store.SaveClient(new Client { Id = "c", Name = "Karoo Coffee Roasters" });
        var made = planner.CreateProject("Karoo Coffee website", template: "web design", client: "karoo coffee", due: "2026-12-15",
            start: "2026-11-02", description: "Five pages and a shop.");

        Assert.Equal("Karoo Coffee Roasters", made.Project.Client);
        Assert.Equal("2026-12-15", made.Project.Due);
        Assert.Equal("Five pages and a shop.", made.Description);
        Assert.Contains("Design", made.Headings);
        Assert.Contains(made.Tasks, t => t.Title == "Agree the sitemap" && t.Heading == "Planning and scope");
        Assert.Single(planner.ListProjects());
        Assert.Contains("projects", AssistantSignal.Read(store)!.Areas);
    }

    [Fact]
    public void Tasks_are_added_given_to_someone_changed_and_ticked_off()
    {
        planner.CreateProject("Brochure site");
        planner.AddHeading("brochure", "Build");
        var added = planner.AddTask("Brochure site", "Contact form", heading: "build", assignee: "me", due: "2026-11-10",
            priority: "high", tags: ["forms"]);

        Assert.Equal("Build", added.Task.Heading);
        Assert.Equal("2026-11-10", added.Task.Due);
        Assert.Equal("High", added.Task.Priority);
        Assert.Equal(["forms"], added.Task.Tags);
        Assert.NotNull(added.Task.Assignee);
        Assert.Single(planner.ListTasks(assignee: "me"));
        Assert.Empty(planner.ListTasks(assignee: "no one"));

        var changed = planner.UpdateTask("contact form", title: "Contact form with spam check", due: "none", done: true);
        Assert.Equal("Contact form with spam check", changed.Task.Title);
        Assert.Null(changed.Task.Due);
        Assert.True(changed.Task.Done);
        Assert.Empty(planner.ListTasks());
        Assert.Single(planner.ListTasks(includeDone: true));
    }

    [Fact]
    public void Subtasks_comments_fields_and_dependencies()
    {
        planner.CreateProject("Shop");
        planner.AddTask("Shop", "Product pages");
        planner.AddTask("Shop", "Photos from the client");
        planner.AddTask("Shop", "Size guide", parentTask: "product pages");
        planner.AddField("Shop", "Stage", "choice", ["Waiting", "Busy", "Done"]);

        planner.SetField("Product pages", "stage", "Busy");
        planner.SetDependency("Product pages", "Photos from the client");
        var task = planner.AddComment("Product pages", "Asked for the photos by mail.", "Claude");

        Assert.Equal("Busy", task.Task.Fields["Stage"]);
        Assert.Equal(["Photos from the client"], task.Task.WaitingOn);
        Assert.Equal("Size guide", Assert.Single(task.Subtasks).Title);
        Assert.Equal("Asked for the photos by mail.\n(Claude)", Assert.Single(task.Comments).Text);
        Assert.Equal(["Product pages"], planner.GetTask("Photos from the client").HoldingUp);

        var free = planner.SetDependency("Product pages", "Photos from the client", waits: false);
        Assert.Empty(free.Task.WaitingOn);
        Assert.Equal(3, planner.ListTasks("Shop").Count);
    }

    [Fact]
    public void Projects_change_get_a_status_update_and_finish_without_being_deleted()
    {
        planner.CreateProject("Retainer work");
        planner.UpdateProject("retainer", name: "Retainer work 2027", shared: true);
        planner.PostStatus("Retainer work 2027", "at risk", "Waiting on the client's copy.");
        planner.AddLink("Retainer work 2027", "https://staging.example.co.za", "Staging");
        var done = planner.UpdateProject("Retainer work 2027", status: "done");

        Assert.Equal("Retainer work 2027", done.Project.Name);
        Assert.True(done.Project.Shared);
        Assert.Equal("Waiting on the client's copy.", done.LatestUpdate!.Text);
        Assert.Equal("Staging", Assert.Single(done.Links).Title);
        Assert.Empty(planner.ListProjects());
        Assert.Single(planner.ListProjects(includeDone: true));
    }

    [Fact]
    public void Unclear_or_wrong_input_comes_back_in_plain_words()
    {
        planner.CreateProject("Karoo shop");
        planner.CreateProject("Karoo website");

        Assert.Contains("more than one", Assert.Throws<ArgumentException>(() => planner.GetProject("karoo")).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => planner.GetProject("Nothing like it"));
        Assert.Throws<ArgumentException>(() => planner.AddTask("Karoo shop", "Logo", due: "next Tuesday"));
        Assert.Throws<ArgumentException>(() => planner.AddTask("Karoo shop", "Logo", assignee: "the intern"));
        Assert.Throws<ArgumentException>(() => planner.UpdateProject("Karoo shop", status: "deleted"));
        Assert.Throws<ArgumentException>(() => planner.SetField("Karoo shop", "Stage", "Busy"));
    }

    [Fact]
    public void To_dos_are_changed_and_ticked_off_but_never_deleted()
    {
        var tasks = new AssistantTasks(store, new FixedClock());
        tasks.Add("Send the sitemap", TaskSource.Claude, "2026-11-05");

        var changed = tasks.Update("sitemap", title: "Send the sitemap to Karoo", due: "2026-11-06", notes: "PDF");
        Assert.Equal("Send the sitemap to Karoo", changed.Title);
        Assert.Equal("2026-11-06", changed.Due);

        var done = tasks.Update("Send the sitemap to Karoo", done: true);
        Assert.True(done.Done);
        var saved = Assert.Single(store.ListTasks());
        Assert.NotNull(saved.DoneAt);
        Assert.Contains("todos", AssistantSignal.Read(store)!.Areas);

        Assert.False(tasks.Update(saved.Id, done: false).Done);
        Assert.Null(Assert.Single(store.ListTasks()).DoneAt);
    }
}
