using Webignite.Core.Projects;
using Webignite.Core.Tasks;

namespace Webignite.Tests;

public class MyWorkTests
{
    private static readonly DateOnly Today = new(2026, 11, 6); // a Friday

    private static ProjectTask Task(string title, DateOnly? due, TaskPriority priority = TaskPriority.Normal) =>
        new() { Id = title, ProjectId = "p", Title = title, Due = due, Priority = priority };

    private static TaskItem Todo(string title, DateOnly? due, bool done = false) =>
        new() { Id = title, Title = title, Due = due, Done = done, Created = DateTimeOffset.UnixEpoch };

    [Fact]
    public void Tasks_and_to_dos_share_one_list_by_when_they_are_due()
    {
        var groups = MyWork.ByWhen(
            [Task("Wireframes", Today.AddDays(-1)), Task("Launch", Today.AddDays(20)), Task("Copy", Today, TaskPriority.High)],
            [Todo("Call the bank", Today), Todo("Renew domain", Today.AddDays(1)), Todo("Someday", null), Todo("Old", Today.AddDays(-3), done: true)],
            Today);
        Assert.Equal(["Overdue", "Today", "This week", "Later", "No due date"], groups.Select(g => g.Title));
        Assert.Equal(["Copy", "Call the bank"], groups[1].Items.Select(i => i.Title));
        Assert.NotNull(groups[1].Items[1].Todo);
        Assert.Equal(["Renew domain"], groups[2].Items.Select(i => i.Title));
        Assert.DoesNotContain(groups.SelectMany(g => g.Items), i => i.Title == "Old");
    }

    [Fact]
    public void The_summary_counts_both()
    {
        Assert.Equal("Nothing on your plate right now.", MyWork.Summary(0, 0));
        Assert.Equal("1 task given to you, across your projects.", MyWork.Summary(1, 0));
        Assert.Equal("2 to-dos of your own.", MyWork.Summary(0, 2));
        Assert.Equal("3 tasks given to you, and 1 to-do of your own.", MyWork.Summary(3, 1));
    }

    [Fact]
    public void A_to_do_moved_into_a_project_keeps_its_words_date_and_what_it_is_about()
    {
        var todo = Todo("Send Bosveld the sitemap", Today) with { Notes = "The PDF from Friday", DocumentId = "doc1" };
        var task = MyWork.ToTask(todo, "p", "me", "client1", "t1");
        Assert.Equal(("t1", "p", "Send Bosveld the sitemap", "The PDF from Friday"), (task.Id, task.ProjectId, task.Title, task.Notes));
        Assert.Equal((Today, "me", "client1", "doc1"), (task.Due!.Value, task.AssignedTo!, task.ClientId!, task.DocumentId!));
    }
}
