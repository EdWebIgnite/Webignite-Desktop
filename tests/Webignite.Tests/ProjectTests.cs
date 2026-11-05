using System.Security.Cryptography;
using Webignite.Core.Projects;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>Projects groundwork: you and your partner, and every change recorded with who made it and when.</summary>
public sealed class ProjectTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-projects-").FullName;
    private readonly LocalStore store;
    private readonly Clock clock = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.FromHours(2)));
    private string who = "eddie";
    private readonly ProjectService projects;

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }

    public ProjectTests()
    {
        store = new LocalStore(Path.Combine(folder, "webignite.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        projects = new ProjectService(store, () => who, clock);
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private Project NewProject(string name = "Bosveld Builders website") => projects.Add(new Project { Id = ProjectRules.NewId(), Name = name }).Saved!;

    private ProjectTask NewTask(string projectId, string title, string? heading = null, string? parent = null) =>
        projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = projectId, Title = title, HeadingId = heading, ParentId = parent }).Saved!;

    [Fact]
    public void You_and_your_partner_are_made_once_and_can_be_renamed()
    {
        var team = ProjectRules.Ensure(new TeamSettings(), null);
        Assert.Equal("Me", team.Me!.Name);
        Assert.Equal("Partner", team.Partner!.Name);
        Assert.NotEqual(team.Me.Id, team.Partner.Id);
        Assert.Same(team.Me, ProjectRules.Ensure(team, "Someone else").Me);

        Assert.Empty(ProjectRules.Check(team.Me with { Name = "Eddie" }, team.Partner with { Name = "Jaco", Email = "jaco@webignite.co.za" }));
        Assert.Single(ProjectRules.Check(team.Me with { Name = "Eddie" }, team.Partner with { Name = " eddie " }));
        Assert.Single(ProjectRules.Check(team.Me with { Name = "Eddie" }, team.Partner with { Name = "Jaco", Email = "jaco" }));
        Assert.Equal(2, ProjectRules.Check(team.Me with { Name = "" }, team.Partner with { Name = " " }).Count);
    }

    [Fact]
    public void A_new_project_records_who_made_it_and_when_for_every_field()
    {
        var project = NewProject();
        var stored = store.GetProject(project.Id)!;

        Assert.Equal("eddie", stored.Created.By);
        Assert.Equal(clock.GetUtcNow(), stored.Created.At);
        Assert.Equal(stored.Created, stored.Changed);
        Assert.All(stored.Fields.Values, s => Assert.Equal(stored.Created, s));
        Assert.Contains("name", stored.Fields.Keys);
        Assert.Null(stored.Deleted);
    }

    [Fact]
    public void A_change_stamps_only_the_fields_that_changed()
    {
        var project = NewProject();
        var made = project.Created;
        clock.Now = clock.Now.AddHours(1);
        who = "jaco";

        var changed = projects.Change(project.Id, p => p with { Status = ProjectStatus.OnHold, Name = "Bosveld Builders website " }).Saved!;

        Assert.Equal("Bosveld Builders website", changed.Name);
        Assert.Equal(made, changed.Created);
        Assert.Equal(new Stamp("jaco", clock.GetUtcNow()), changed.Changed);
        Assert.Equal(new Stamp("jaco", clock.GetUtcNow()), changed.Fields["status"]);
        Assert.Equal(made, changed.Fields["name"]); // trimmed back to what it was, so not a change
        Assert.Equal(made, changed.Fields["due"]);

        clock.Now = clock.Now.AddHours(1);
        var same = projects.Change(project.Id, p => p).Saved!;
        Assert.Equal(changed.Changed, same.Changed);
    }

    [Fact]
    public void Projects_and_tasks_need_names_and_subtasks_go_one_level_deep()
    {
        Assert.Single(projects.Add(new Project { Id = ProjectRules.NewId(), Name = " " }).Problems);
        var project = NewProject();
        Assert.Single(projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, Title = "" }).Problems);

        var task = NewTask(project.Id, "Home page");
        var sub = NewTask(project.Id, "Hero photo", parent: task.Id);
        Assert.Equal(task.Id, sub.ParentId);
        Assert.Contains("one level", Assert.Single(projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, Title = "Crop it", ParentId = sub.Id }).Problems));
        Assert.Single(projects.ChangeTask(task.Id, t => t with { ParentId = NewTask(project.Id, "Other").Id }).Problems);
        Assert.Single(projects.ChangeTask(task.Id, t => t with { Start = new DateOnly(2026, 11, 10), Due = new DateOnly(2026, 11, 9) }).Problems);
    }

    [Fact]
    public void New_tasks_go_at_the_end_of_their_heading_and_moving_a_task_takes_its_subtasks_along()
    {
        var project = NewProject();
        var design = projects.AddHeading(project.Id, "Design").Saved!;
        var build = projects.AddHeading(project.Id, "Build").Saved!;
        Assert.Equal(["Design", "Build"], projects.Headings(project.Id).Select(h => h.Name));

        var first = NewTask(project.Id, "Wireframes", design.Id);
        var second = NewTask(project.Id, "Colours", design.Id);
        var elsewhere = NewTask(project.Id, "Hosting", build.Id);
        Assert.True(second.Order > first.Order);
        Assert.Equal(1, elsewhere.Order);
        var sub = NewTask(project.Id, "Mobile wireframe", parent: first.Id, heading: design.Id);

        projects.ChangeTask(first.Id, t => t with { HeadingId = build.Id });
        Assert.Equal(build.Id, projects.GetTask(sub.Id)!.HeadingId);
        Assert.Equal(1.5, ProjectRules.Between(1, 2));
        Assert.Equal(0, ProjectRules.Between(null, 1));
    }

    [Fact]
    public void Deleting_leaves_a_marker_with_who_and_when_and_undo_puts_it_all_back()
    {
        var project = NewProject();
        var design = projects.AddHeading(project.Id, "Design").Saved!;
        var task = NewTask(project.Id, "Wireframes", design.Id);
        NewTask(project.Id, "Mobile", design.Id, task.Id);
        clock.Now = clock.Now.AddMinutes(5);

        var gone = projects.Delete(project.Id)!;
        Assert.Empty(projects.Projects());
        Assert.Empty(projects.AllTasks(project.Id));
        var marker = store.GetProject(project.Id)!;
        Assert.Equal(new Stamp("eddie", clock.GetUtcNow()), marker.Deleted);
        Assert.All(store.ListProjectTasks(), t => Assert.NotNull(t.Deleted));

        clock.Now = clock.Now.AddMinutes(1);
        projects.Restore(gone);
        Assert.Single(projects.Projects());
        Assert.Equal(2, projects.AllTasks(project.Id).Count);
        Assert.Equal(["Design"], projects.Headings(project.Id).Select(h => h.Name));
        Assert.Equal(clock.GetUtcNow(), store.GetProject(project.Id)!.Fields["deleted"].At);
    }

    [Fact]
    public void Deleting_a_heading_keeps_its_tasks_under_no_heading_until_undone()
    {
        var project = NewProject();
        var design = projects.AddHeading(project.Id, "Design").Saved!;
        var task = NewTask(project.Id, "Wireframes", design.Id);

        var gone = projects.DeleteHeading(design.Id)!;
        Assert.Empty(projects.Headings(project.Id));
        Assert.Null(projects.GetTask(task.Id)!.HeadingId);

        projects.Restore(gone);
        Assert.Equal(design.Id, projects.GetTask(task.Id)!.HeadingId);
    }

    [Fact]
    public void Changes_reach_pages_with_the_projects_id()
    {
        var seen = new List<string?>();
        projects.Changed += seen.Add;
        var project = NewProject();
        NewTask(project.Id, "Wireframes");
        Assert.Equal([null, project.Id], seen);
    }
}

/// <summary>The list view's rules: sections by heading, and where a dragged task lands.</summary>
public sealed class ProjectListTests
{
    private static Stamp When => new("eddie", DateTimeOffset.UnixEpoch);

    private static ProjectTask Task(string id, double order, string? heading = null, string? parent = null, bool done = false) =>
        new() { Id = id, ProjectId = "p", Title = id, Order = order, HeadingId = heading, ParentId = parent, Done = done };

    [Fact]
    public void Tasks_are_shown_by_heading_with_loose_ones_first_and_subtasks_left_out()
    {
        var design = new Heading { Id = "h1", ProjectId = "p", Name = "Design", Order = 1 };
        var build = new Heading { Id = "h2", ProjectId = "p", Name = "Build", Order = 2 };
        var gone = new Heading { Id = "h3", ProjectId = "p", Name = "Gone", Order = 3, Deleted = When };
        var tasks = new[] { Task("b", 2, "h1"), Task("a", 1, "h1"), Task("loose", 1), Task("sub", 1, "h1", "a"), Task("orphan", 5, "h3") };

        var sections = ProjectRules.Sections([build, design, gone], tasks);

        Assert.Equal([null, "Design", "Build"], sections.Select(s => s.Heading?.Name));
        Assert.Equal(["loose", "orphan"], sections[0].Tasks.Select(t => t.Id));
        Assert.Equal(["a", "b"], sections[1].Tasks.Select(t => t.Id));
        Assert.Empty(sections[2].Tasks);
        Assert.Single(ProjectRules.Sections([], []));                  // nothing yet: one place to add tasks
        Assert.Equal(["Design"], ProjectRules.Sections([design], []).Select(s => s.Heading?.Name)); // headings only
    }

    [Fact]
    public void A_dragged_task_lands_between_its_new_neighbours()
    {
        var section = new[] { Task("a", 1), Task("b", 2), Task("c", 3) };

        Assert.Equal(0, ProjectRules.PlaceAt(section, 0, "c"));     // to the top
        Assert.Equal(1.5, ProjectRules.PlaceAt(section, 1, "c"));   // between a and b
        Assert.Equal(2.5, ProjectRules.PlaceAt(section, 2, "a"));   // a to between b and c: counts the gap a leaves
        Assert.Equal(4, ProjectRules.PlaceAt(section, 3, "a"));     // a to below c
        Assert.Equal(4, ProjectRules.PlaceAt(section, 3, "new"));   // from another heading, to the end
        Assert.Equal(1, ProjectRules.PlaceAt([], 0, "new"));
    }

    [Fact]
    public void Subtasks_are_counted_and_done_projects_go_last_or_away()
    {
        Assert.Equal("1 of 2", ProjectRules.SubtaskCount([Task("x", 1, done: true), Task("y", 2)]));
        Assert.Equal("", ProjectRules.SubtaskCount([]));
        var listed = ProjectRules.Listed([
            new Project { Id = "1", Name = "Done", Status = ProjectStatus.Done, Order = 1 },
            new Project { Id = "2", Name = "Live", Order = 2 },
            new Project { Id = "3", Name = "Gone", Order = 0, Deleted = When },
        ], showDone: true);
        Assert.Equal(["Live", "Done"], listed.Select(p => p.Name));
        Assert.Equal(["Live"], ProjectRules.Listed(listed, showDone: false).Select(p => p.Name));
    }
}

/// <summary>The task page: links, and who made and changed a task.</summary>
public sealed class ProjectTaskPageTests
{
    [Fact]
    public void A_task_says_who_made_it_and_who_changed_it_last()
    {
        var made = new Stamp("me", new DateTimeOffset(2026, 11, 5, 7, 0, 0, TimeSpan.Zero));
        var task = new ProjectTask { Id = "t", ProjectId = "p", Title = "Home page", Created = made, Changed = made };
        var names = new Dictionary<string, string> { ["me"] = "Eddie", ["partner"] = "Jaco" };
        var zone = TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        Assert.Equal("Made by Eddie on 5 Nov.", ProjectRules.History(task, names, zone, culture));
        var changed = task with { Changed = new Stamp("partner", new DateTimeOffset(2026, 11, 6, 12, 20, 0, TimeSpan.Zero)) };
        Assert.Equal("Made by Eddie on 5 Nov. Changed by Jaco on 6 Nov at 14:20.", ProjectRules.History(changed, names, zone, culture));
        Assert.Contains("someone else", ProjectRules.History(task with { Created = made with { By = "x" } }, names, zone, culture));
    }

    [Fact]
    public void Linking_a_mail_keeps_where_it_is_and_what_it_says()
    {
        var mail = new Webignite.Core.Mail.MailSummary
        {
            AccountId = "a", Folder = "INBOX", Uid = 41, MessageId = "<x@y>", Subject = "New product photos",
            From = new Webignite.Core.Mail.MailPerson("Thandi Mokoena", "thandi@karoocoffee.co.za"), Date = DateTimeOffset.UnixEpoch,
        };
        var link = TaskMail.Of(mail);
        Assert.Equal(new TaskMail("a", "INBOX", 41, "<x@y>", "New product photos", "Thandi Mokoena", DateTimeOffset.UnixEpoch), link);
        var before = new ProjectTask { Id = "t", ProjectId = "p", Title = "Photos" };
        var stamped = ProjectRules.Stamped(ProjectRules.Stamped(null, before, new Stamp("me", DateTimeOffset.UnixEpoch)), before with { Mail = link },
            new Stamp("me", DateTimeOffset.UnixEpoch.AddHours(1)));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddHours(1), stamped.Fields["mail"].At);
        Assert.Equal(DateTimeOffset.UnixEpoch, stamped.Fields["title"].At);
    }

    private static Webignite.Core.Documents.StoredDocument Doc(string id, string customer, Webignite.Core.Documents.DocumentKind kind, string? number, decimal? total, int day) => new()
    {
        Id = id,
        Updated = new DateTimeOffset(2026, 11, day, 9, 0, 0, TimeSpan.Zero),
        Document = new Webignite.Core.Documents.Document
        {
            Kind = kind, Origin = Webignite.Core.Documents.DocumentOrigin.New, Number = number, Total = total,
            Customer = new Webignite.Core.Documents.Customer { Name = customer }, Lines = [],
        },
    };

    [Fact]
    public void A_task_offers_its_clients_quotes_and_invoices_newest_first()
    {
        var karoo = new Webignite.Core.Clients.Client { Id = "c1", Name = "Karoo Coffee Roasters" };
        var docs = new[]
        {
            Doc("q", "Karoo Coffee Roasters", Webignite.Core.Documents.DocumentKind.Quote, "QU-0012", 4500m, 1),
            Doc("i", "karoo  coffee roasters", Webignite.Core.Documents.DocumentKind.Invoice, "INV-0107", 2725m, 3),
            Doc("x", "Fynbos Physio", Webignite.Core.Documents.DocumentKind.Invoice, null, null, 4),
        };

        Assert.Equal(["i", "q"], ProjectRules.DocumentsFor(karoo, docs).Select(d => d.Id));
        Assert.Equal(["x", "i", "q"], ProjectRules.DocumentsFor(null, docs).Select(d => d.Id));
        Assert.Equal("Invoice INV-0107, karoo  coffee roasters, " + Webignite.Core.Documents.Money.Rand(2725m), ProjectRules.DocumentLabel(docs[1]));
        Assert.Equal("Draft invoice, Fynbos Physio", ProjectRules.DocumentLabel(docs[2]));
    }
}

/// <summary>Priority and tags on tasks, and filtering by them.</summary>
public sealed class ProjectTagTests
{
    private static ProjectTask Task(string id, TaskPriority priority = TaskPriority.Normal, params string[] tags) =>
        new() { Id = id, ProjectId = "p", Title = id, Priority = priority, Tags = tags };

    [Fact]
    public void Typed_tags_are_tidied_and_follow_how_a_tag_is_already_written()
    {
        Assert.Equal(["Hosting", "SEO"], ProjectRules.Tags(" hosting, SEO,, Hosting ", ["Hosting"]));
        Assert.Equal(["new one"], ProjectRules.Tags("new one", []));
        Assert.Empty(ProjectRules.Tags(" , ", []));
        Assert.Equal(["Hosting", "SEO"], ProjectRules.AllTags([Task("a", tags: ["SEO", "Hosting"]), Task("b", tags: ["seo"])]));
    }

    [Fact]
    public void The_filter_shows_tasks_with_the_priority_and_tag_picked()
    {
        var high = Task("a", TaskPriority.High, "SEO");
        var low = Task("b", TaskPriority.Low, "Hosting");

        Assert.True(ProjectRules.Shows(high, null, null));
        Assert.True(ProjectRules.Shows(high, TaskPriority.High, "seo"));
        Assert.False(ProjectRules.Shows(low, TaskPriority.High, null));
        Assert.False(ProjectRules.Shows(low, null, "SEO"));
    }

    [Fact]
    public void A_task_added_while_filtered_gets_the_filter_so_it_stays_in_view()
    {
        var added = ProjectRules.Filtered(Task("c"), TaskPriority.High, "SEO");
        Assert.Equal(TaskPriority.High, added.Priority);
        Assert.Equal(["SEO"], added.Tags);
        Assert.Equal(["SEO"], ProjectRules.Filtered(Task("d", tags: ["seo"]), null, "SEO").Tags.Select(t => t.ToUpperInvariant()));
    }

    [Fact]
    public void Changing_tags_is_recorded_like_any_other_field()
    {
        var made = new Stamp("me", DateTimeOffset.UnixEpoch);
        var task = ProjectRules.Stamped(null, Task("t", tags: ["SEO"]), made);
        var same = ProjectRules.Stamped(task, task with { Tags = ["SEO"] }, made with { At = made.At.AddHours(1) });
        Assert.Equal(made, same.Fields["tags"]);
        var changed = ProjectRules.Stamped(task, task with { Tags = ["SEO", "Hosting"], Priority = TaskPriority.High }, made with { At = made.At.AddHours(2) });
        Assert.Equal(made.At.AddHours(2), changed.Fields["tags"].At);
        Assert.Equal(made.At.AddHours(2), changed.Fields["priority"].At);
    }
}

/// <summary>My tasks across projects, and the ties to the calendar, mail and the timer.</summary>
public sealed class MyTaskTests
{
    // Thursday 5 November 2026; the week runs Monday 2 to Sunday 8.
    private static readonly DateOnly Today = new(2026, 11, 5);

    private static ProjectTask Task(string id, string project = "p", string? who = "me", DateOnly? due = null, bool done = false, TaskPriority priority = TaskPriority.Normal) =>
        new() { Id = id, ProjectId = project, Title = id, AssignedTo = who, Due = due, Done = done, Priority = priority };

    [Fact]
    public void My_tasks_are_open_tasks_given_to_me_in_live_projects()
    {
        var projects = new[]
        {
            new Project { Id = "p", Name = "Site" },
            new Project { Id = "done", Name = "Old", Status = ProjectStatus.Done },
            new Project { Id = "gone", Name = "Gone", Deleted = new Stamp("me", DateTimeOffset.UnixEpoch) },
        };
        var tasks = new[] { Task("mine"), Task("theirs", who: "partner"), Task("ticked", done: true), Task("old", "done"), Task("lost", "gone"), Task("nobody", who: null) };

        Assert.Equal(["mine"], ProjectRules.Mine(tasks, projects, "me").Select(t => t.Id));
    }

    [Fact]
    public void My_tasks_are_grouped_by_when_they_are_due_high_first()
    {
        var tasks = new[]
        {
            Task("late", due: Today.AddDays(-2)), Task("today", due: Today), Task("today-high", due: Today, priority: TaskPriority.High),
            Task("sunday", due: new DateOnly(2026, 11, 8)), Task("next-week", due: new DateOnly(2026, 11, 9)), Task("whenever"),
        };

        var groups = ProjectRules.ByWhen(tasks, Today);

        Assert.Equal(["Overdue", "Today", "This week", "Later", "No due date"], groups.Select(g => g.Title));
        Assert.Equal(["today-high", "today"], groups[1].Tasks.Select(t => t.Id));
        Assert.Equal(["sunday"], groups[2].Tasks.Select(t => t.Id));
        Assert.Equal(["Today"], ProjectRules.ByWhen([Task("x", due: Today)], Today).Select(g => g.Title));
    }

    [Fact]
    public void My_tasks_due_in_a_range_show_on_the_calendar_with_their_project()
    {
        var items = ProjectRules.Agenda(Today, Today.AddDays(6), [Task("Wireframes", due: Today), Task("Later", due: Today.AddDays(10)), Task("Whenever")],
            new Dictionary<string, string> { ["p"] = "Bosveld Builders website" });

        var item = Assert.Single(items);
        Assert.Equal("Wireframes (Bosveld Builders website)", item.Title);
        Assert.Equal(Webignite.Core.Calendar.AgendaKind.ProjectTask, item.Kind);
        var all = Webignite.Core.Calendar.Agenda.Build(Today, Today, [], [], [], items);
        Assert.Single(all);
    }

    [Fact]
    public void A_task_from_a_mail_is_named_after_it_given_to_me_and_linked()
    {
        var mail = new Webignite.Core.Mail.MailSummary
        {
            AccountId = "a", Folder = "INBOX", Uid = 7, Subject = "  Site down? ",
            From = new Webignite.Core.Mail.MailPerson("Pieter van Wyk", "pieter@bosveldbuilders.co.za"),
        };
        var task = ProjectRules.FromMail(mail, "p", "me", "c1");
        Assert.Equal("Site down?", task.Title);
        Assert.Equal(("me", "c1", (uint)7), (task.AssignedTo, task.ClientId, task.Mail!.Uid));
        Assert.Equal("Mail from Pieter van Wyk", ProjectRules.FromMail(mail with { Subject = "" }, "p", "me", null).Title);
        Assert.Equal("Wireframes, Bosveld Builders website", ProjectRules.TimerNote(Task("Wireframes"), "Bosveld Builders website"));
    }
}
