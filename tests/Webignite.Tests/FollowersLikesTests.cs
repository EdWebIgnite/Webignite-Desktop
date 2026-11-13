using System.Security.Cryptography;
using Webignite.Core.Projects;
using Webignite.Core.Sync;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>Followers, likes and @mentions on tasks and comments, as Asana's.</summary>
public sealed class FollowersLikesTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-follow-").FullName;
    private readonly LocalStore store;
    private readonly Clock clock = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.FromHours(2)));
    private readonly ProjectService projects;
    private readonly TeamSettings team = new()
    {
        Me = new Teammate { Id = "eddie", Name = "Eddie" },
        Partner = new Teammate { Id = "lize", Name = "Lize Botha" },
    };
    private string who = "eddie";

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }

    public FollowersLikesTests()
    {
        store = new LocalStore(Path.Combine(folder, "webignite.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        store.SetSetting("team", team);
        projects = new ProjectService(store, () => who, clock, Path.Combine(folder, "attachments"));
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private ProjectTask NewTask(string title = "Send the wireframes", string? assigned = null, TaskRepeat repeat = TaskRepeat.None)
    {
        var project = projects.Add(new Project { Id = ProjectRules.NewId(), Name = "Kloof Street Dental", Shared = true }).Saved!;
        return projects.AddTask(new ProjectTask
        {
            Id = ProjectRules.NewId(), ProjectId = project.Id, Title = title, AssignedTo = assigned, Repeat = repeat,
            Due = repeat == TaskRepeat.None ? null : new DateOnly(2026, 11, 5),
        }).Saved!;
    }

    [Fact]
    public void Mentions_are_found_by_name_after_an_at_sign()
    {
        Assert.Equal(["lize"], TaskTalk.Mentions("Thanks @Lize Botha, looks good", team));
        Assert.Equal(["lize", "eddie"], TaskTalk.Mentions("@lize botha can you check? cc @EDDIE", team));
        Assert.Empty(TaskTalk.Mentions("Mail it to eddie@webignite.co.za", team));
        Assert.Empty(TaskTalk.Mentions("@Eddiekins isn't anyone", team));
        Assert.Equal(["lize"], TaskTalk.Mentions("@Lize Botha @Lize Botha", team));

        Assert.Equal("Li", TaskTalk.MentionTyped("thanks @Li"));
        Assert.Equal("", TaskTalk.MentionTyped("thanks @"));
        Assert.Null(TaskTalk.MentionTyped("eddie@web"));
        Assert.Null(TaskTalk.MentionTyped("no mention here"));
        Assert.Equal("thanks @Lize Botha ", TaskTalk.CompleteMention("thanks @Li", "Lize Botha"));
    }

    [Fact]
    public void Whoever_makes_is_given_comments_on_or_is_mentioned_on_a_task_follows_it()
    {
        var task = NewTask();
        Assert.Equal(["eddie"], task.Followers);

        task = projects.ChangeTask(task.Id, t => t with { AssignedTo = "lize" }).Saved!;
        Assert.Equal(["eddie", "lize"], task.Followers);

        // Lize stops following; Eddie's comment mentioning her brings her back.
        who = "lize";
        Assert.Equal(["eddie"], projects.FollowTask(task.Id, false)!.Followers);
        who = "eddie";
        var comment = projects.AddComment(task.Id, "@Lize Botha the client signed off").Saved!;
        Assert.Equal(["lize"], comment.Mentions);
        Assert.Equal(["eddie", "lize"], projects.GetTask(task.Id)!.Followers);

        // Following and liking leave no line in the activity feed.
        var lines = projects.Activity(task.Id).Count;
        projects.LikeTask(task.Id, true);
        projects.FollowTask(task.Id, false);
        Assert.Equal(lines, projects.Activity(task.Id).Count);
    }

    [Fact]
    public void Claude_and_ChatGPT_never_follow()
    {
        who = Actors.Claude;
        var task = NewTask(assigned: Actors.Claude);
        Assert.Empty(task.Followers);
        projects.AddComment(task.Id, "Drafted the copy");
        Assert.Empty(projects.GetTask(task.Id)!.Followers);
    }

    [Fact]
    public void Likes_on_tasks_and_comments_toggle_per_person()
    {
        var task = NewTask();
        Assert.Equal(["eddie"], projects.LikeTask(task.Id, true)!.LikedBy);
        Assert.Equal(["eddie"], projects.LikeTask(task.Id, true)!.LikedBy);
        who = "lize";
        Assert.Equal(["eddie", "lize"], projects.LikeTask(task.Id, true)!.LikedBy);
        Assert.Equal(["eddie"], projects.LikeTask(task.Id, false)!.LikedBy);

        var comment = projects.AddComment(task.Id, "Done").Saved!;
        who = "eddie";
        Assert.Equal(["eddie"], projects.LikeComment(comment.Id, true)!.LikedBy);
        Assert.Empty(projects.LikeComment(comment.Id, false)!.LikedBy);
    }

    [Fact]
    public void A_repeating_task_ticked_off_is_the_same_task_so_it_keeps_its_followers_and_likes()
    {
        var task = NewTask("Check backups", assigned: "lize", repeat: TaskRepeat.Weekly);
        projects.LikeTask(task.Id, true);
        var again = projects.ChangeTask(task.Id, t => t with { Done = true }).Saved!;
        Assert.Equal(task.Id, again.Id);
        Assert.Equal(["eddie", "lize"], again.Followers);
        Assert.Equal(["eddie"], again.LikedBy);
    }

    [Fact]
    public void Two_people_liking_at_once_on_two_pcs_are_both_kept_and_a_later_unlike_wins()
    {
        var start = new DateTimeOffset(2026, 11, 5, 7, 0, 0, TimeSpan.Zero);
        var made = ProjectRules.Stamped(null, new ProjectTask { Id = "t", ProjectId = "p", Title = "Launch" }, new Stamp("eddie", start));
        var mine = ProjectRules.Stamped(made, made with { LikedBy = ["eddie"], Followers = ["eddie"] }, new Stamp("eddie", start.AddMinutes(2)));
        var hers = ProjectRules.Stamped(made, made with { LikedBy = ["lize"] }, new Stamp("lize", start.AddMinutes(3)));

        var merged = RecordMerge.Merge(mine, hers);
        Assert.Equal(["eddie", "lize"], merged.LikedBy.Order());
        Assert.Equal(["eddie"], merged.Followers);
        Assert.Equal(merged.LikedBy.Order(), RecordMerge.Merge(hers, mine).LikedBy.Order());

        // Eddie takes his like back later on his PC; Lize's copy still has it. The later stamp wins.
        var unliked = ProjectRules.Stamped(merged, merged with { LikedBy = ["lize"] }, new Stamp("eddie", start.AddMinutes(5)));
        Assert.Equal(["lize"], RecordMerge.Merge(merged, unliked).LikedBy);
        Assert.Equal(["lize"], RecordMerge.Merge(unliked, merged).LikedBy);
    }

    [Fact]
    public void A_comments_mentions_travel_with_its_text()
    {
        var start = new DateTimeOffset(2026, 11, 5, 7, 0, 0, TimeSpan.Zero);
        var made = ProjectRules.Stamped(null, new TaskComment { Id = "c", TaskId = "t", ProjectId = "p", Text = "Hi" }, new Stamp("eddie", start));
        var edited = ProjectRules.Stamped(made, made with { Text = "Hi @Lize Botha", Mentions = ["lize"] }, new Stamp("eddie", start.AddMinutes(1)));
        var liked = ProjectRules.Stamped(made, made with { LikedBy = ["lize"] }, new Stamp("lize", start.AddMinutes(2)));
        var merged = RecordMerge.Merge(edited, liked);
        Assert.Equal("Hi @Lize Botha", merged.Text);
        Assert.Equal(["lize"], merged.Mentions);
        Assert.Equal(["lize"], merged.LikedBy);
    }
}
