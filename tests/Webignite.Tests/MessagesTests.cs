using System.Globalization;
using System.Security.Cryptography;
using Webignite.Core.Projects;
using Webignite.Core.Sync;
using Webignite.Data;
using Webignite.Services;

namespace Webignite.Tests;

/// <summary>A project's Messages tab, as Asana's: conversations with replies, @mentions and likes.</summary>
public sealed class MessagesTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-messages-").FullName;
    private readonly LocalStore store;
    private readonly Clock clock = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly TeamSettings team = new()
    {
        Me = new Teammate { Id = "eddie", Name = "Eddie" },
        Partner = new Teammate { Id = "lize", Name = "Lize" },
    };
    private readonly ProjectService mine;
    private readonly ProjectService hers;

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public MessagesTests()
    {
        store = new LocalStore(Path.Combine(folder, "webignite.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
        store.SetSetting("team", team);
        mine = new ProjectService(store, () => "eddie", clock, Path.Combine(folder, "a"));
        hers = new ProjectService(store, () => "lize", clock, Path.Combine(folder, "a"));
    }

    public void Dispose()
    {
        store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private Project NewProject() => mine.Add(new Project { Id = ProjectRules.NewId(), Name = "Kloof Street Dental", Shared = true }).Saved!;

    private void Later() => clock.Now = clock.Now.AddMinutes(1);

    [Fact]
    public void A_message_needs_words_and_a_subject_and_a_reply_only_words()
    {
        Assert.Equal("Write something first.", MessageRules.Check("Kick-off", " ", reply: false));
        Assert.Equal("Give it a subject, so it can be found later.", MessageRules.Check(" ", "Hello", reply: false));
        Assert.Equal("", MessageRules.Check("", "Thanks", reply: true));
        Assert.NotEqual("", MessageRules.Check(new string('x', MessageRules.LongestSubject + 1), "Hello", reply: false));

        var project = NewProject();
        Assert.Equal("Give it a subject, so it can be found later.", mine.PostMessage(project.Id, "", "Hi").Problem);
        Assert.Equal("That message is gone.", mine.PostMessage(project.Id, "", "Hi", "nope").Problem);
    }

    [Fact]
    public void Conversations_list_with_their_replies_the_latest_word_first()
    {
        var project = NewProject();
        var kickOff = mine.PostMessage(project.Id, "Kick-off", "Here's the plan for the site").Saved!;
        Later();
        var launch = mine.PostMessage(project.Id, "Launch date", "Can we go live on the 20th?").Saved!;
        Later();
        var reply = hers.PostMessage(project.Id, "ignored", "@Eddie the plan looks good", kickOff.Id).Saved!;
        Assert.Equal("", reply.Subject);
        Assert.Equal(["eddie"], reply.Mentions);

        var threads = mine.Messages(project.Id);
        Assert.Equal([kickOff.Id, launch.Id], threads.Select(t => t.Start.Id));
        Assert.Equal(reply.Id, Assert.Single(threads[0].Replies).Id);

        // Deleting a conversation takes its replies with it; Undo brings it back.
        var gone = mine.DeleteMessage(kickOff.Id)!;
        Assert.Equal([launch.Id], mine.Messages(project.Id).Select(t => t.Start.Id));
        mine.RestoreMessage(gone);
        Assert.Single(mine.Messages(project.Id)[0].Replies);

        Assert.Equal(["eddie"], mine.LikeMessage(reply.Id, true)!.LikedBy);
        Assert.Equal("Here's the plan, updated", mine.EditMessage(kickOff.Id, "Kick-off", "Here's the plan, updated")!.Text);
    }

    [Fact]
    public void My_partners_messages_and_likes_on_mine_show_in_my_inbox()
    {
        var inbox = new InboxService(store, () => "eddie", () => team, clock);
        _ = inbox.State;
        var project = NewProject();
        var mineMessage = mine.PostMessage(project.Id, "Kick-off", "Plan attached").Saved!;
        Later();
        hers.PostMessage(project.Id, "", "Looks good", mineMessage.Id);
        Later();
        hers.PostMessage(project.Id, "Hosting", "@Eddie which plan are we on?");
        Later();
        hers.LikeMessage(mineMessage.Id, true);
        Later();
        mine.PostMessage(project.Id, "", "Thanks", mineMessage.Id);

        var items = inbox.Items();
        Assert.Equal(
            [(InboxKind.Liked, "liked your message", "Kick-off"), (InboxKind.Mentioned, "mentioned you in a message", "Hosting"), (InboxKind.Message, "replied to a message", "Kick-off")],
            items.Select(i => (i.Kind, i.Line, i.TaskTitle)));
        Assert.All(items, i => Assert.Equal("", i.TaskId));
        Assert.Equal(mineMessage.Id, items[0].MessageId);
        Assert.Equal("Lize replied to a message: Kick-off", InboxRules.Headline(items[2]));
    }

    [Fact]
    public void A_messages_text_and_likes_merge_as_a_comments_do()
    {
        var start = new DateTimeOffset(2026, 11, 5, 7, 0, 0, TimeSpan.Zero);
        var made = ProjectRules.Stamped(null, new ProjectMessage { Id = "m", ProjectId = "p", Subject = "Kick-off", Text = "Hi" }, new Stamp("eddie", start));
        var edited = ProjectRules.Stamped(made, made with { Subject = "Kick-off meeting", Text = "Hi @Lize", Mentions = ["lize"] }, new Stamp("eddie", start.AddMinutes(1)));
        var liked = ProjectRules.Stamped(made, made with { LikedBy = ["lize"] }, new Stamp("lize", start.AddMinutes(2)));
        var merged = RecordMerge.Merge(edited, liked);
        Assert.Equal("Kick-off meeting", merged.Subject);
        Assert.Equal(["lize"], merged.Mentions);
        Assert.Equal(["lize"], merged.LikedBy);
        Assert.Contains(SyncKinds.Message, SyncKinds.All);
        Assert.Equal("1", merged.LikedBy.Count.ToString(CultureInfo.InvariantCulture));
    }
}
