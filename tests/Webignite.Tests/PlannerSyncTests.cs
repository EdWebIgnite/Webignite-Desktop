using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Webignite.Core.Projects;
using Webignite.Core.Sync;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>Shared projects going between two PCs through the real sync server, running in memory.</summary>
public sealed class PlannerSyncTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-sync-").FullName;
    private readonly WebApplicationFactory<Program> server;
    private readonly ManualClock clock = new(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
    private readonly List<IDisposable> made = [];

    public PlannerSyncTests()
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
    }

    public void Dispose()
    {
        foreach (var thing in Enumerable.Reverse(made))
            thing.Dispose();
        server.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>One PC: its own database, vault, sign-in, projects and sync.</summary>
    private sealed record Pc(LocalStore Store, AccountClient Account, ProjectService Projects, SyncService Sync)
    {
        public Teammate Me => Store.GetSetting<TeamSettings>("team")!.Me!;
    }

    private Pc NewPc(string name, string myName)
    {
        var store = new LocalStore(Path.Combine(folder, name + ".db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        made.Add(store);
        store.SetSetting("team", ProjectRules.Ensure(new TeamSettings(), myName));
        var account = new AccountClient(store, new MemorySecretStore(), server.Server.CreateHandler());
        made.Add(account);
        Assert.Null(account.UseServer("http://localhost"));
        var projects = new ProjectService(store, () => store.GetSetting<TeamSettings>("team")!.Me!.Id, clock, Path.Combine(folder, name + "-files"));
        var sync = new SyncService(store, account, clock);
        made.Add(sync);
        return new Pc(store, account, projects, sync);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>Eddie makes the workspace on his PC; his partner joins with an invite on hers.</summary>
    private async Task<(Pc Owner, Pc Member)> TwoPcs()
    {
        var owner = NewPc("eddie", "Eddie");
        Assert.True((await owner.Account.SignUpAsync("eddie@webignite.co.za", "karoo rooibos sunrise", "Webignite", null, Cancel)).Worked);
        var invite = await owner.Account.InviteAsync(Cancel);
        var member = NewPc("lize", "Lize");
        var joined = await member.Account.SignUpAsync("lize@webignite.co.za", "fynbos table mountain", null, invite.Value!.Code, Cancel);
        Assert.True(joined.Worked, joined.Problem);
        return (owner, member);
    }

    private static async Task<SyncResult> Sync(Pc pc)
    {
        var result = await pc.Sync.SyncAsync(Cancel);
        Assert.True(result.Worked, result.Problem);
        return result;
    }

    private static Project Shared(Pc pc, string name, bool shared = true) =>
        pc.Projects.Add(new Project { Id = ProjectRules.NewId(), Name = name, Shared = shared }).Saved!;

    private static ProjectTask Task(Pc pc, Project project, string title) =>
        pc.Projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, Title = title }).Saved!;

    [Fact]
    public async Task A_shared_project_made_on_one_pc_shows_on_the_other_and_personal_ones_stay()
    {
        var (owner, member) = await TwoPcs();
        var project = Shared(owner, "Kloof Street Dental site");
        var task = Task(owner, project, "Send the wireframes");
        owner.Projects.AddComment(task.Id, "Client wants it by Friday");
        var personal = Shared(owner, "My own admin", shared: false);
        Task(owner, personal, "Renew licence disc");

        var sent = await Sync(owner);
        Assert.True(sent.Sent >= 4); // the team, the project, the task, the comment, the activity
        await Sync(member);

        Assert.Equal("Kloof Street Dental site", member.Store.GetProject(project.Id)!.Name);
        Assert.Equal("Send the wireframes", member.Store.GetProjectTask(task.Id)!.Title);
        Assert.Single(member.Store.ListComments(task.Id));
        Assert.NotEmpty(member.Store.ListActivity(task.Id));
        Assert.Null(member.Store.GetProject(personal.Id));
        Assert.DoesNotContain(member.Store.ListProjectTasks(), t => t.ProjectId == personal.Id);

        // Nothing changed: nothing goes again.
        Assert.Equal(0, (await Sync(owner)).Sent);
    }

    [Fact]
    public async Task Messages_on_a_shared_project_reach_the_other_pc_with_their_replies()
    {
        var (owner, member) = await TwoPcs();
        var project = Shared(owner, "Kloof Street Dental site");
        var kickOff = owner.Projects.PostMessage(project.Id, "Kick-off", "Here's the plan").Saved!;
        await Sync(owner);
        await Sync(member);
        Assert.Equal("Kick-off", Assert.Single(member.Projects.Messages(project.Id)).Start.Subject);

        clock.Now = clock.Now.AddMinutes(1);
        member.Projects.PostMessage(project.Id, "", "Looks good", kickOff.Id);
        await Sync(member);
        await Sync(owner);
        Assert.Equal("Looks good", Assert.Single(owner.Projects.Messages(project.Id)[0].Replies).Text);
    }

    [Fact]
    public async Task Different_fields_changed_on_each_pc_are_both_kept_and_the_same_field_goes_to_the_later_change()
    {
        var (owner, member) = await TwoPcs();
        var project = Shared(owner, "Retainer: Bokaap Bakery");
        var task = Task(owner, project, "Monthly SEO report");
        await Sync(owner);
        await Sync(member);

        clock.Now = clock.Now.AddMinutes(5);
        owner.Projects.ChangeTask(task.Id, t => t with { Title = "Monthly SEO report (October)", Notes = "Eddie's notes" });
        clock.Now = clock.Now.AddMinutes(5);
        member.Projects.ChangeTask(task.Id, t => t with { Due = new DateOnly(2026, 11, 14), Notes = "Lize's notes" });

        await Sync(owner);
        await Sync(member);
        await Sync(owner);

        foreach (var pc in new[] { owner, member })
        {
            var merged = pc.Store.GetProjectTask(task.Id)!;
            Assert.Equal("Monthly SEO report (October)", merged.Title);
            Assert.Equal(new DateOnly(2026, 11, 14), merged.Due);
            Assert.Equal("Lize's notes", merged.Notes); // both changed it; hers was later
        }
        // Settled: neither PC has anything left to send.
        Assert.Equal(0, (await Sync(member)).Sent);
        Assert.Equal(0, (await Sync(owner)).Sent);
    }

    [Fact]
    public async Task A_deletion_reaches_the_other_pc()
    {
        var (owner, member) = await TwoPcs();
        var project = Shared(owner, "Old campaign");
        var task = Task(owner, project, "Archive the ads");
        await Sync(owner);
        await Sync(member);

        clock.Now = clock.Now.AddMinutes(1);
        Assert.NotNull(member.Projects.Delete(project.Id));
        await Sync(member);
        await Sync(owner);

        Assert.Null(owner.Projects.Get(project.Id));
        Assert.Null(owner.Projects.GetTask(task.Id));
    }

    [Fact]
    public async Task A_task_moved_to_a_personal_project_or_a_project_no_longer_shared_goes_from_the_other_pc()
    {
        var (owner, member) = await TwoPcs();
        var project = Shared(owner, "Kloof Street Dental site");
        var task = Task(owner, project, "Private note to self");
        var other = Shared(owner, "Bokaap Bakery");
        var kept = Task(owner, other, "Stays with the project");
        var personal = Shared(owner, "My own admin", shared: false);
        await Sync(owner);
        await Sync(member);
        Assert.NotNull(member.Projects.GetTask(task.Id));

        clock.Now = clock.Now.AddMinutes(1);
        owner.Projects.MoveTask(task.Id, personal.Id);
        owner.Projects.Change(other.Id, p => p with { Shared = false });
        await Sync(owner);
        await Sync(member);

        Assert.Null(member.Projects.GetTask(task.Id));
        Assert.Null(member.Projects.Get(other.Id));
        Assert.Equal(0, (await Sync(member)).Sent); // nothing of them goes back
        Assert.Null(member.Store.GetProject(personal.Id));
        // On Eddie's PC nothing changed, and nothing more goes.
        Assert.Equal("Private note to self", owner.Projects.GetTask(task.Id)!.Title);
        Assert.NotNull(owner.Projects.Get(other.Id));
        Assert.Equal(0, (await Sync(owner)).Sent);

        // Shared again: it comes back on the other PC.
        clock.Now = clock.Now.AddMinutes(1);
        owner.Projects.Change(other.Id, p => p with { Shared = true });
        await Sync(owner);
        await Sync(member);
        Assert.NotNull(member.Projects.GetTask(kept.Id));

        // And a task moved back into a shared project comes back too.
        clock.Now = clock.Now.AddMinutes(1);
        owner.Projects.MoveTask(task.Id, project.Id);
        await Sync(owner);
        await Sync(member);
        Assert.Equal("Private note to self", member.Projects.GetTask(task.Id)!.Title);
    }

    [Fact]
    public async Task Tasks_given_to_someone_stay_given_to_the_right_person_on_the_partners_pc()
    {
        var (owner, member) = await TwoPcs();
        var lizeBefore = member.Me.Id;
        var partnerOnEddiesPc = owner.Store.GetSetting<TeamSettings>("team")!.Partner!.Id;

        // Lize shares a project of her own before Eddie's PC has ever synced: hers waits for his ids.
        var hers = Shared(member, "Lize's launch checklist");
        var herTask = member.Projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = hers.Id, Title = "Book the photographer", AssignedTo = lizeBefore }).Saved!;
        var waiting = await Sync(member);
        Assert.True(waiting.WaitingForOwner);
        Assert.Equal(0, waiting.Sent);

        var project = Shared(owner, "Kloof Street Dental site");
        var forLize = owner.Projects.AddTask(new ProjectTask { Id = ProjectRules.NewId(), ProjectId = project.Id, Title = "Write the copy", AssignedTo = partnerOnEddiesPc }).Saved!;
        await Sync(owner);
        await Sync(member);

        // Lize's PC now uses Eddie's ids for both of them, and her own records were changed over.
        var team = member.Store.GetSetting<TeamSettings>("team")!;
        Assert.Equal(partnerOnEddiesPc, team.Me!.Id);
        Assert.Equal("Lize", team.Me.Name);
        Assert.Equal("Eddie", team.Partner!.Name);
        Assert.Equal(team.Me.Id, member.Store.GetProjectTask(forLize.Id)!.AssignedTo);
        Assert.Equal(team.Me.Id, member.Store.GetProjectTask(herTask.Id)!.AssignedTo);
        Assert.Equal(team.Me.Id, member.Store.GetProjectTask(herTask.Id)!.Created.By);
        Assert.Contains(member.Projects.Mine(team.Me.Id), t => t.Id == forLize.Id);

        // And her project reaches Eddie, given to the person he calls his partner.
        await Sync(member);
        await Sync(owner);
        Assert.Equal(partnerOnEddiesPc, owner.Store.GetProjectTask(herTask.Id)!.AssignedTo);
    }

    [Fact]
    public async Task A_second_pc_of_the_owners_takes_on_the_ids_the_first_one_sent()
    {
        var (owner, _) = await TwoPcs();
        var project = Shared(owner, "Kloof Street Dental site");
        await Sync(owner);

        var laptop = NewPc("laptop", "Eddie");
        Assert.True((await laptop.Account.SignInAsync("eddie@webignite.co.za", "karoo rooibos sunrise", cancel: Cancel)).Worked);
        await Sync(laptop);
        Assert.Equal(owner.Me.Id, laptop.Me.Id);

        var made = Task(laptop, laptop.Store.GetProject(project.Id)!, "Written on the laptop");
        await Sync(laptop);
        await Sync(owner);
        Assert.Equal(owner.Me.Id, owner.Store.GetProjectTask(made.Id)!.Created.By);
    }

    [Fact]
    public async Task A_record_the_server_changed_or_swapped_is_not_taken_in()
    {
        var (owner, member) = await TwoPcs();
        var project = Shared(owner, "Real project");
        await Sync(owner);

        // Someone with the server's database swaps one record's locked content for another's.
        var key = RecordLock.KeyFrom(owner.Account.WorkspaceKey!);
        var json = JsonSerializer.Serialize(new Project { Id = "other", Name = "Planted" }, RecordLock.Json);
        var lockedForAnother = RecordLock.Seal(SyncKinds.Project, "other", json, key);
        Assert.Null(RecordLock.Open(SyncKinds.Project, project.Id, lockedForAnother, key));
        Assert.True((await owner.Account.PushAsync([new SyncRecord(SyncKinds.Project, project.Id, lockedForAnother)], Cancel)).Worked);
        Assert.True((await owner.Account.PushAsync([new SyncRecord(SyncKinds.Task, "junk", "bm90IGxvY2tlZA==")], Cancel)).Worked);

        var result = await Sync(member);
        Assert.Equal(2, result.Unreadable);
        Assert.Null(member.Store.GetProject(project.Id));
        Assert.Null(member.Store.GetProject("other"));
    }

    [Fact]
    public async Task The_server_keeps_records_to_its_workspace_and_refuses_odd_ones()
    {
        var (owner, _) = await TwoPcs();
        var stranger = NewPc("stranger", "Stranger");
        Assert.True((await stranger.Account.SignUpAsync("someone@example.com", "another long password", "Elsewhere", null, Cancel)).Worked);

        Assert.True((await owner.Account.PushAsync([new SyncRecord(SyncKinds.Project, "p1", "bG9ja2Vk")], Cancel)).Worked);
        var theirs = await stranger.Account.PullAsync(0, Cancel);
        Assert.Empty(theirs.Value!.Records);

        Assert.False((await owner.Account.PushAsync([new SyncRecord("invoice", "p1", "bG9ja2Vk")], Cancel)).Worked);
        Assert.False((await owner.Account.PushAsync([new SyncRecord(SyncKinds.Project, "../p1", "bG9ja2Vk")], Cancel)).Worked);
        Assert.False((await owner.Account.PushAsync([new SyncRecord(SyncKinds.Project, "p2", "")], Cancel)).Worked);
        var tooMany = Enumerable.Range(0, SyncLimits.RecordsPerPush + 1).Select(i => new SyncRecord(SyncKinds.Task, $"t{i}", "bG9ja2Vk")).ToList();
        Assert.False((await owner.Account.PushAsync(tooMany, Cancel)).Worked);
    }

    [Fact]
    public async Task Pulling_comes_in_pages_and_picks_up_where_it_left_off()
    {
        var (owner, member) = await TwoPcs();
        var batch = Enumerable.Range(0, SyncLimits.RecordsPerPull + 20).Select(i => new SyncRecord(SyncKinds.Task, $"t{i}", "bG9ja2Vk")).ToList();
        Assert.True((await owner.Account.PushAsync(batch[..SyncLimits.RecordsPerPush], Cancel)).Worked);
        Assert.True((await owner.Account.PushAsync(batch[SyncLimits.RecordsPerPush..], Cancel)).Worked);

        var first = (await member.Account.PullAsync(0, Cancel)).Value!;
        Assert.True(first.More);
        Assert.Equal(SyncLimits.RecordsPerPull, first.Records.Count);
        var second = (await member.Account.PullAsync(first.Cursor, Cancel)).Value!;
        Assert.False(second.More);
        Assert.Equal(20, second.Records.Count);

        // A record sent again moves to the end, so a PC that already had it gets the new copy.
        Assert.True((await owner.Account.PushAsync([batch[0] with { Payload = "bmV3ZXI=" }], Cancel)).Worked);
        var third = (await member.Account.PullAsync(second.Cursor, Cancel)).Value!;
        Assert.Equal("bmV3ZXI=", Assert.Single(third.Records).Payload);
    }
}
