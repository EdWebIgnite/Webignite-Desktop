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
        projects = new ProjectService(store, () => who, clock, Path.Combine(folder, "attachments"));
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
    public void Comments_and_what_happened_make_the_task_feed()
    {
        var project = NewProject();
        var build = projects.AddHeading(project.Id, "Build").Saved!;
        var task = NewTask(project.Id, "Contact form");
        clock.Now = clock.Now.AddMinutes(1);
        projects.ChangeTask(task.Id, t => t with { Done = true, HeadingId = build.Id, Order = 9 });
        NewTask(project.Id, "Spam filter", parent: task.Id);
        who = "jaco";
        var (comment, problem) = projects.AddComment(task.Id, "  Tested on my phone, works.  ");
        Assert.Null(problem);
        Assert.NotNull(projects.AddComment(task.Id, " ").Problem);

        Assert.Equal("Tested on my phone, works.", Assert.Single(projects.Comments(task.Id)).Text);
        Assert.Equal("jaco", comment!.Created.By);
        Assert.Equal([ActivityKind.Created, ActivityKind.Completed, ActivityKind.Moved, ActivityKind.SubtaskAdded],
            projects.Activity(task.Id).Select(a => a.Kind));
        Assert.Equal(1, projects.CommentCounts()[task.Id]);

        projects.EditComment(comment.Id, "Tested on phone and tablet.");
        Assert.Equal("Tested on phone and tablet.", projects.Comments(task.Id)[0].Text);
        var gone = projects.DeleteComment(comment.Id)!;
        Assert.Empty(projects.Comments(task.Id));
        Assert.False(projects.CommentCounts().ContainsKey(task.Id));
        projects.RestoreComment(gone);
        Assert.Single(projects.Comments(task.Id));
    }

    [Fact]
    public void A_file_attached_is_copied_with_the_app_and_deleted_when_taken_off()
    {
        var project = NewProject();
        var task = NewTask(project.Id, "Home page photos");
        var source = Path.Combine(folder, "Shopfront.jpg");
        File.WriteAllBytes(source, [0xFF, 0xD8, 0xFF, 1, 2, 3]);

        var (saved, problem) = projects.Attach(task.Id, source);

        Assert.Null(problem);
        var attachment = Assert.Single(saved!.Attachments);
        Assert.Equal(("Shopfront.jpg", 6L, "eddie"), (attachment.FileName, attachment.Size, attachment.AddedBy));
        Assert.Equal(attachment.Id, saved.CoverId);
        var kept = projects.PathOf(task.Id, attachment);
        Assert.True(File.Exists(kept));
        File.Delete(source);
        Assert.True(File.Exists(kept));

        var after = projects.Detach(task.Id, attachment.Id)!;
        Assert.Empty(after.Attachments);
        Assert.Null(after.CoverId);
        Assert.False(File.Exists(kept));
        Assert.NotNull(projects.Attach(task.Id, source).Problem);
    }

    [Fact]
    public void A_task_dropped_on_another_project_moves_there_with_its_subtasks()
    {
        var from = NewProject();
        var to = NewProject("Karoo Coffee website");
        var heading = projects.AddHeading(from.Id, "Build").Saved!;
        NewTask(to.Id, "Already there");
        var task = NewTask(from.Id, "Contact form", heading.Id);
        var sub = NewTask(from.Id, "Spam filter", heading.Id, task.Id);
        var lone = NewTask(from.Id, "Lone subtask", null, NewTask(from.Id, "Parent").Id);
        clock.Now = clock.Now.AddMinutes(5);

        var moved = projects.MoveTask(task.Id, to.Id).Saved!;

        Assert.Equal(to.Id, moved.ProjectId);
        Assert.Null(moved.HeadingId);
        Assert.Equal("Contact form", projects.AllTasks(to.Id).Where(t => t.ParentId is null).OrderBy(t => t.Order).Last().Title);
        Assert.Equal(to.Id, projects.GetTask(sub.Id)!.ProjectId);
        Assert.Equal(task.Id, projects.GetTask(sub.Id)!.ParentId);
        Assert.Equal(clock.GetUtcNow(), moved.Fields["project"].At);
        Assert.Null(projects.MoveTask(lone.Id, to.Id).Saved!.ParentId);
        Assert.NotEmpty(projects.MoveTask(task.Id, "gone").Problems);
    }

    [Fact]
    public void A_heading_dragged_lands_before_another_or_last_and_its_order_is_kept()
    {
        var project = NewProject();
        var design = projects.AddHeading(project.Id, "Design").Saved!;
        var build = projects.AddHeading(project.Id, "Build").Saved!;
        var launch = projects.AddHeading(project.Id, "Launch").Saved!;
        var headings = projects.Headings(project.Id);

        var order = ProjectRules.PlaceHeading(headings, launch.Id, design.Id)!.Value;
        projects.ChangeHeading(launch.Id, h => h with { Order = order });
        Assert.Equal(["Launch", "Design", "Build"], projects.Headings(project.Id).OrderBy(h => h.Order).Select(h => h.Name));

        var last = ProjectRules.PlaceHeading(projects.Headings(project.Id), launch.Id, null)!.Value;
        projects.ChangeHeading(launch.Id, h => h with { Order = last });
        Assert.Equal(["Design", "Build", "Launch"], projects.Headings(project.Id).OrderBy(h => h.Order).Select(h => h.Name));
        Assert.Null(ProjectRules.PlaceHeading(projects.Headings(project.Id), build.Id, launch.Id));
        Assert.Null(ProjectRules.PlaceHeading(projects.Headings(project.Id), launch.Id, null));
    }

    [Fact]
    public void A_project_dragged_among_starred_ones_is_starred_and_lands_between_its_neighbours()
    {
        Project P(string id, double order, bool star = false) => new() { Id = id, Name = id, Order = order, Favourite = star };
        var shown = ProjectLooks.SidebarOrder([P("a", 1, true), P("b", 2, true), P("c", 3), P("d", 4), P("e", 5)]);
        Assert.Equal((1.5, true), ProjectLooks.PlaceAt(shown, "e", "a", below: true));
        Assert.Equal((3.5, false), ProjectLooks.PlaceAt(shown, "a", "c", below: true));
        Assert.Equal((2.0, false), ProjectLooks.PlaceAt(shown, "e", "c", below: false));
        Assert.Null(ProjectLooks.PlaceAt(shown, "c", "d", below: false));
        Assert.Null(ProjectLooks.PlaceAt(shown, "c", "gone", below: false));
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
    public void A_board_column_adds_at_the_top_or_the_bottom()
    {
        var project = NewProject();
        var design = projects.AddHeading(project.Id, "Design").Saved!;
        var middle = NewTask(project.Id, "Wireframes", design.Id);
        var last = NewTask(project.Id, "Colours", design.Id);
        var top = projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, HeadingId = design.Id, Title = "Brief" }, first: true).Saved!;
        Assert.Equal(["Brief", "Wireframes", "Colours"], projects.AllTasks(project.Id).Select(t => t.Title));
        Assert.True(top.Order < middle.Order && middle.Order < last.Order);
        // At the top of an empty heading it still comes first once others follow.
        Assert.Equal(1, ProjectRules.FirstOrder([]));
        Assert.Equal(-1, ProjectRules.FirstOrder([0, 3]));
    }

    [Fact]
    public void Status_updates_and_key_resources_are_kept_on_the_project_and_stamped()
    {
        var project = NewProject();
        var (posted, problems) = projects.PostStatus(project.Id, ProjectHealth.OnTrack, "Home page signed off.");
        Assert.Empty(problems);
        Assert.Equal(ProjectHealth.OnTrack, posted!.Health);
        Assert.Equal(who, Assert.Single(posted.Updates).By);
        Assert.True(posted.Fields.ContainsKey("updates"));
        Assert.Single(projects.PostStatus(project.Id, ProjectHealth.None, "").Problems);

        var (linked, _) = projects.AddLink(project.Id, "Staging", "staging.bosveld.co.za");
        Assert.Equal("https://staging.bosveld.co.za", Assert.Single(projects.Get(project.Id)!.Links).Url);
        Assert.Single(projects.AddLink(project.Id, "", "nope").Problems);
        projects.RemoveLink(project.Id, linked!.Links[0].Id);
        Assert.Empty(projects.Get(project.Id)!.Links);
    }

    [Fact]
    public void Custom_fields_are_kept_on_the_project_and_their_values_on_tasks()
    {
        var project = NewProject();
        var (saved, problems) = projects.AddField(project.Id, "Hours", FieldKind.Number, []);
        Assert.Empty(problems);
        var field = Assert.Single(saved!.CustomFields);
        Assert.True(saved.Fields.ContainsKey("customfields"));
        var task = NewTask(project.Id, "Wireframes");
        Assert.Equal("4.5", projects.SetFieldValue(task.Id, field.Id, "4.5", System.Globalization.CultureInfo.InvariantCulture).Saved!.Values[field.Id]);
        Assert.Single(projects.SetFieldValue(task.Id, field.Id, "lots", System.Globalization.CultureInfo.InvariantCulture).Problems);
        Assert.Empty(projects.SetFieldValue(task.Id, field.Id, "", System.Globalization.CultureInfo.InvariantCulture).Saved!.Values);
        projects.RemoveField(project.Id, field.Id);
        Assert.Empty(projects.Get(project.Id)!.CustomFields);
    }

    [Fact]
    public void A_template_keeps_milestones_custom_fields_and_what_waits_on_what()
    {
        var project = NewProject();
        var field = projects.AddField(project.Id, "Stage", FieldKind.Choice, ["Not started", "Doing"]).Saved!.CustomFields.Single();
        var design = NewTask(project.Id, "Design sign-off");
        var build = NewTask(project.Id, "Build the pages");
        projects.ChangeTask(design.Id, t => t with { Milestone = true });
        projects.SetFieldValue(design.Id, field.Id, "Doing", System.Globalization.CultureInfo.InvariantCulture);
        projects.AddDependency(build.Id, design.Id);

        var template = projects.TemplateOf(project.Id, "Website");
        Assert.Single(template.Fields);
        Assert.True(template.Tasks[0].Milestone);
        Assert.Equal([0], template.Tasks[1].WaitsOn);

        var made = projects.AddFromTemplate(template, "Karoo website", new DateOnly(2026, 12, 1));
        var newField = Assert.Single(made.CustomFields);
        Assert.NotEqual(field.Id, newField.Id);
        var tasks = projects.AllTasks(made.Id);
        var newDesign = tasks.Single(t => t.Title == "Design sign-off");
        var newBuild = tasks.Single(t => t.Title == "Build the pages");
        Assert.True(newDesign.Milestone);
        Assert.Equal("Doing", CustomFields.Show(newField, newDesign.Values[newField.Id], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal([newDesign.Id], newBuild.BlockedBy);
        // The original stays as it was.
        Assert.Equal([design.Id], projects.GetTask(build.Id)!.BlockedBy);
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
    public void Undoing_a_heading_delete_keeps_what_changed_on_its_tasks_meanwhile()
    {
        var project = NewProject();
        var design = projects.AddHeading(project.Id, "Design").Saved!;
        var build = projects.AddHeading(project.Id, "Build").Saved!;
        var wire = NewTask(project.Id, "Wireframes", design.Id);
        var mobile = NewTask(project.Id, "Mobile", design.Id, wire.Id);
        var logo = NewTask(project.Id, "Logo", design.Id);

        var gone = projects.DeleteHeading(design.Id)!;
        projects.ChangeTask(wire.Id, t => t with { Done = true, Notes = "Signed off" });
        projects.ChangeTask(logo.Id, t => t with { HeadingId = build.Id });

        projects.Restore(gone);

        Assert.Equal(["Design", "Build"], projects.Headings(project.Id).Select(h => h.Name));
        var back = projects.GetTask(wire.Id)!;
        Assert.Equal((design.Id, true, "Signed off"), (back.HeadingId, back.Done, back.Notes));
        Assert.Equal(design.Id, projects.GetTask(mobile.Id)!.HeadingId);
        // Put under another heading meanwhile, it stays there.
        Assert.Equal(build.Id, projects.GetTask(logo.Id)!.HeadingId);
        Assert.Contains(projects.Activity(wire.Id), a => a.Kind == ActivityKind.Moved && a.Value == "Design");
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

/// <summary>Repeating tasks and project templates.</summary>
public sealed class ProjectRepeatTemplateTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    private static ProjectTask Task(string id, TaskRepeat repeat = TaskRepeat.None, DateOnly? due = null, DateOnly? start = null) =>
        new() { Id = id, ProjectId = "p", Title = id, Repeat = repeat, Due = due, Start = start, Done = true, HeadingId = "h" };

    [Fact]
    public void A_repeating_task_ticked_off_comes_back_a_step_later()
    {
        var weekly = ProjectRules.Next(Task("Backups", TaskRepeat.Weekly, Today, Today.AddDays(-1)), Today, 2.5)!;
        Assert.False(weekly.Done);
        Assert.NotEqual("Backups", weekly.Id);
        Assert.Equal((Today.AddDays(7), Today.AddDays(6), "h", 2.5), (weekly.Due!.Value, weekly.Start!.Value, weekly.HeadingId!, weekly.Order));

        Assert.Equal(new DateOnly(2026, 12, 5), ProjectRules.Next(Task("Report", TaskRepeat.Monthly, Today), Today, 1)!.Due);
        Assert.Equal(Today.AddDays(1), ProjectRules.Next(Task("Check", TaskRepeat.Daily), Today, 1)!.Due);
        Assert.Null(ProjectRules.Next(Task("Once"), Today, 1));
    }

    [Fact]
    public void A_repeating_task_ticked_off_late_comes_back_in_the_future()
    {
        var late = ProjectRules.Next(Task("Backups", TaskRepeat.Weekly, Today.AddDays(-20)), Today, 1)!;
        Assert.Equal(Today.AddDays(1), late.Due); // -20, -13, -6, +1
    }

    [Fact]
    public void The_next_one_moves_its_subtasks_dates_by_the_same_step_and_leaves_files_and_waits_behind()
    {
        var parent = Task("Report", TaskRepeat.Weekly, Today, Today.AddDays(-2)) with
        {
            Attachments = [new TaskAttachment("f1", "Shop front.jpg", 10, DateTimeOffset.UnixEpoch, "me")],
            CoverId = "f1",
        };
        var subtasks = new[]
        {
            new ProjectTask { Id = "s1", ProjectId = "p", ParentId = "Report", HeadingId = "h", Title = "Draft", Done = true, Start = Today.AddDays(-2), Due = Today.AddDays(-1),
                Attachments = [new TaskAttachment("f2", "Notes.pdf", 10, DateTimeOffset.UnixEpoch, "me")], CoverId = "f2", BlockedBy = ["s2"], Order = 1 },
            new ProjectTask { Id = "s2", ProjectId = "p", ParentId = "Report", HeadingId = "h", Title = "Send", Order = 2 },
            new ProjectTask { Id = "s3", ProjectId = "p", ParentId = "Report", Title = "Gone", Deleted = new Stamp("me", DateTimeOffset.UnixEpoch), Order = 3 },
            new ProjectTask { Id = "o", ProjectId = "p", Title = "Someone else's" },
        };

        var made = ProjectRules.Next(parent, subtasks, Today, 2);

        var next = made[0];
        Assert.Equal(("Report", Today.AddDays(7), Today.AddDays(5)), (next.RepeatOf, next.Due!.Value, next.Start!.Value));
        Assert.Empty(next.Attachments);
        Assert.Null(next.CoverId);
        Assert.Equal(["Draft", "Send"], made.Skip(1).Select(t => t.Title));
        var draft = made[1];
        Assert.Equal((next.Id, false, Today.AddDays(5), Today.AddDays(6)), (draft.ParentId, draft.Done, draft.Start!.Value, draft.Due!.Value));
        Assert.Empty(draft.Attachments);
        Assert.Null(draft.CoverId);
        Assert.Empty(draft.BlockedBy);
        Assert.NotEqual("s1", draft.Id);
        Assert.Null(made[2].Due);
        Assert.Empty(ProjectRules.Next(Task("Once"), subtasks, Today, 1));
    }

    [Fact]
    public void Subtasks_of_a_repeating_task_with_no_date_move_on_from_today()
    {
        var parent = Task("Check", TaskRepeat.Daily);
        var sub = new ProjectTask { Id = "s", ProjectId = "p", ParentId = "Check", Title = "Look at the logs", Due = Today.AddDays(3) };
        var made = ProjectRules.Next(parent, [sub], Today, 1);
        Assert.Equal(Today.AddDays(1), made[0].Due);
        Assert.Equal(Today.AddDays(4), made[1].Due);
    }

    [Fact]
    public void A_repeating_task_knows_when_its_next_one_is_already_there()
    {
        var done = Task("Backups", TaskRepeat.Weekly, Today);
        var next = ProjectRules.Next(done, Today, 1)!;
        Assert.True(ProjectRules.HasNext(done, [done, next]));
        Assert.False(ProjectRules.HasNext(done, [done, next with { Deleted = new Stamp("me", DateTimeOffset.UnixEpoch) }]));
        Assert.False(ProjectRules.HasNext(done, [done]));
    }

    [Fact]
    public void Monthly_and_yearly_repeats_come_back_to_their_day_when_the_month_allows()
    {
        var day = new DateOnly(2027, 1, 31);
        DateOnly Tick(ref ProjectTask task)
        {
            task = ProjectRules.Next(task with { Done = true }, task.Due!.Value, 1)!;
            return task.Due!.Value;
        }
        var monthly = Task("Invoice run", TaskRepeat.Monthly, day);
        Assert.Equal(new DateOnly(2027, 2, 28), Tick(ref monthly));
        Assert.Equal(new DateOnly(2027, 3, 31), Tick(ref monthly));
        Assert.Equal(new DateOnly(2027, 4, 30), Tick(ref monthly));
        Assert.Equal(new DateOnly(2027, 5, 31), Tick(ref monthly));

        var yearly = Task("Leap day", TaskRepeat.Yearly, new DateOnly(2028, 2, 29));
        Assert.Equal(new DateOnly(2029, 2, 28), Tick(ref yearly));
        Assert.Equal(new DateOnly(2030, 2, 28), Tick(ref yearly));
        Assert.Equal(new DateOnly(2031, 2, 28), Tick(ref yearly));
        Assert.Equal(new DateOnly(2032, 2, 29), Tick(ref yearly));

        // Moved by hand to the 15th, it carries on from the 15th.
        var moved = monthly with { Due = new DateOnly(2027, 6, 15) };
        Assert.Equal(new DateOnly(2027, 7, 15), Tick(ref moved));
        // Ticked off late, it skips to the next one still to come, on its day.
        var late = ProjectRules.Next(Task("Invoice run", TaskRepeat.Monthly, day) with { RepeatFrom = null }, new DateOnly(2027, 3, 10), 1)!;
        Assert.Equal(new DateOnly(2027, 3, 31), late.Due);
    }

    [Fact]
    public void A_template_keeps_headings_tasks_subtasks_and_dates_as_days_from_the_start()
    {
        var design = new Heading { Id = "h1", ProjectId = "p", Name = "Design", Order = 1 };
        var build = new Heading { Id = "h2", ProjectId = "p", Name = "Build", Order = 2 };
        var tasks = new[]
        {
            new ProjectTask { Id = "a", ProjectId = "p", HeadingId = "h1", Title = "Wireframes", Due = new DateOnly(2026, 11, 10), Start = new DateOnly(2026, 11, 3),
                AssignedTo = "me", Done = true, Priority = TaskPriority.High, Tags = ["Design"], Order = 1 },
            new ProjectTask { Id = "a1", ProjectId = "p", HeadingId = "h1", ParentId = "a", Title = "Mobile", Order = 1 },
            new ProjectTask { Id = "b", ProjectId = "p", HeadingId = "h2", Title = "Build pages", Due = new DateOnly(2026, 11, 24), Order = 1 },
            new ProjectTask { Id = "r", ProjectId = "p", Title = "Weekly check (ticked)", Repeat = TaskRepeat.Weekly, Done = true, Due = new DateOnly(2026, 11, 3), Order = 1 },
        };

        var template = ProjectRules.TemplateOf(" Website build ", [build, design], tasks);

        Assert.Equal("Website build", template.Name);
        Assert.Equal(["Design", "Build"], template.Headings);
        Assert.Equal(["Wireframes", "Build pages"], template.Tasks.Select(t => t.Title));
        Assert.Equal((0, 7), (template.Tasks[0].StartDay!.Value, template.Tasks[0].DueDay!.Value));
        Assert.Equal(21, template.Tasks[1].DueDay);
        Assert.Equal(["Mobile"], template.Tasks[0].Subtasks);

        var (project, headings, made) = ProjectRules.FromTemplate(template, "", new DateOnly(2027, 1, 4), 3);
        Assert.Equal(("Website build", 3.0), (project.Name, project.Order));
        Assert.Equal(["Design", "Build"], headings.Select(h => h.Name));
        var wire = made.First(t => t.Title == "Wireframes");
        Assert.Equal((new DateOnly(2027, 1, 4), new DateOnly(2027, 1, 11)), (wire.Start!.Value, wire.Due!.Value));
        Assert.False(wire.Done);
        Assert.Null(wire.AssignedTo);
        Assert.Equal((TaskPriority.High, "Design"), (wire.Priority, Assert.Single(wire.Tags)));
        Assert.Equal(headings[0].Id, wire.HeadingId);
        Assert.Equal(wire.Id, made.Single(t => t.Title == "Mobile").ParentId);
        Assert.All(made, t => Assert.Equal(project.Id, t.ProjectId));
    }
}

/// <summary>The service side of repeats and templates.</summary>
public sealed class ProjectRepeatServiceTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-repeat-").FullName;
    private readonly LocalStore store;
    private readonly ProjectService projects;

    public ProjectRepeatServiceTests()
    {
        store = new LocalStore(Path.Combine(folder, "webignite.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        projects = new ProjectService(store, () => "me");
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Ticking_off_a_repeating_task_adds_the_next_with_fresh_subtasks_once()
    {
        var project = projects.Add(new Project { Id = ProjectRules.NewId(), Name = "Hosting" }).Saved!;
        var due = DateOnly.FromDateTime(DateTime.Today);
        var task = projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, Title = "Check backups", Repeat = TaskRepeat.Weekly, Due = due }).Saved!;
        var after = projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, Title = "Something after" }).Saved!;
        projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, ParentId = task.Id, Title = "Restore one file" });

        projects.ChangeTask(task.Id, t => t with { Done = true });
        projects.ChangeTask(task.Id, t => t with { Notes = "done" }); // a later change doesn't make another

        var open = projects.AllTasks(project.Id).Where(t => t.Title == "Check backups" && !t.Done).ToList();
        var next = Assert.Single(open);
        Assert.Equal(due.AddDays(7), next.Due);
        Assert.True(next.Order > task.Order && next.Order < after.Order);
        Assert.Equal("Restore one file", Assert.Single(projects.AllTasks(project.Id), t => t.ParentId == next.Id).Title);
    }

    [Fact]
    public void Ticking_a_repeating_task_off_on_and_off_again_makes_just_one_next()
    {
        var project = projects.Add(new Project { Id = ProjectRules.NewId(), Name = "Hosting" }).Saved!;
        var due = DateOnly.FromDateTime(DateTime.Today);
        var task = projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, Title = "Check backups", Repeat = TaskRepeat.Weekly, Due = due }).Saved!;
        projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, ParentId = task.Id, Title = "Restore one file", Due = due });

        projects.ChangeTask(task.Id, t => t with { Done = true });
        projects.ChangeTask(task.Id, t => t with { Done = false });
        projects.ChangeTask(task.Id, t => t with { Done = true });

        var next = Assert.Single(projects.AllTasks(project.Id), t => t.RepeatOf == task.Id);
        Assert.Equal(due.AddDays(7), Assert.Single(projects.AllTasks(project.Id), t => t.ParentId == next.Id).Due);

        // With the next one deleted, ticking it again makes another.
        projects.DeleteTask(next.Id);
        projects.ChangeTask(task.Id, t => t with { Done = false });
        projects.ChangeTask(task.Id, t => t with { Done = true });
        Assert.Single(projects.AllTasks(project.Id), t => t.RepeatOf == task.Id);
    }

    [Fact]
    public void A_project_started_from_a_template_has_everything_in_it_stamped()
    {
        var project = projects.Add(new Project { Id = ProjectRules.NewId(), Name = "Website" }).Saved!;
        var heading = projects.AddHeading(project.Id, "Design").Saved!;
        projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, HeadingId = heading.Id, Title = "Wireframes", Due = new DateOnly(2026, 11, 10) });

        var made = projects.AddFromTemplate(projects.TemplateOf(project.Id, "Website build"), "Karoo website", new DateOnly(2027, 1, 4));

        Assert.Equal("Karoo website", made.Name);
        Assert.Equal(["Design"], projects.Headings(made.Id).Select(h => h.Name));
        var task = Assert.Single(projects.AllTasks(made.Id));
        Assert.Equal(new DateOnly(2027, 1, 4), task.Due);
        Assert.Equal("me", task.Created.By);
    }
}

/// <summary>How a task's due date reads in a list.</summary>
public sealed class DueLabelTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    [Fact]
    public void Nearby_days_read_as_words()
    {
        Assert.Equal("Today", ProjectRules.DueLabel(Today, Today));
        Assert.Equal("Tomorrow", ProjectRules.DueLabel(Today.AddDays(1), Today));
        Assert.Equal("Yesterday", ProjectRules.DueLabel(Today.AddDays(-1), Today));
    }

    [Fact]
    public void The_coming_week_reads_as_the_day_and_later_as_the_date()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        Assert.Equal(Today.AddDays(3).ToString("dddd", culture), ProjectRules.DueLabel(Today.AddDays(3), Today));
        Assert.Equal(Today.AddDays(9).ToString("d MMM", culture), ProjectRules.DueLabel(Today.AddDays(9), Today));
        Assert.Equal(new DateOnly(2027, 2, 1).ToString("d MMM yyyy", culture), ProjectRules.DueLabel(new DateOnly(2027, 2, 1), Today));
    }
}
