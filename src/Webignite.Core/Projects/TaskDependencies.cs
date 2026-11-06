namespace Webignite.Core.Projects;

/// <summary>
/// Dependencies, as Asana's: a task can wait on others in its project ("blocked by"), and the ones it holds up
/// are "blocking". A task can't wait on itself, nor on one that is already waiting on it, however far round.
/// </summary>
public static class TaskDependencies
{
    /// <summary>The tasks it's waiting on that are still in the project.</summary>
    public static IReadOnlyList<ProjectTask> BlockedBy(ProjectTask task, IEnumerable<ProjectTask> tasks)
    {
        var byId = Live(tasks, task.ProjectId).ToDictionary(t => t.Id);
        return [.. task.BlockedBy.Select(id => byId.GetValueOrDefault(id)).OfType<ProjectTask>()];
    }

    /// <summary>The tasks waiting on it.</summary>
    public static IReadOnlyList<ProjectTask> Blocking(ProjectTask task, IEnumerable<ProjectTask> tasks) =>
        [.. Live(tasks, task.ProjectId).Where(t => t.BlockedBy.Contains(task.Id)).OrderBy(t => t.Order)];

    /// <summary>Still waiting: one of the tasks it's waiting on isn't done yet.</summary>
    public static bool IsWaiting(ProjectTask task, IEnumerable<ProjectTask> tasks) => BlockedBy(task, tasks).Any(t => !t.Done);

    /// <summary>The open tasks it's still waiting on, by name, for its tip: "Wireframes, Copy".</summary>
    public static string WaitingOn(ProjectTask task, IEnumerable<ProjectTask> tasks) =>
        string.Join(", ", BlockedBy(task, tasks).Where(t => !t.Done).Select(t => t.Title));

    /// <summary>What's wrong with making a task wait on another, or empty when it can.</summary>
    public static string Problem(ProjectTask task, string blockerId, IEnumerable<ProjectTask> tasks)
    {
        var all = Live(tasks, task.ProjectId).ToDictionary(t => t.Id);
        if (blockerId == task.Id)
            return "A task can't wait on itself.";
        if (!all.ContainsKey(blockerId))
            return "That task isn't in this project.";
        if (task.BlockedBy.Contains(blockerId))
            return "It's already waiting on that task.";
        if (Reaches(blockerId, task.Id, all))
            return "That task is already waiting on this one, so this one can't wait on it.";
        return "";
    }

    /// <summary>The tasks in its project it could wait on, in their order: not itself, not one already picked, none that would go round in a circle.</summary>
    public static IReadOnlyList<ProjectTask> Choices(ProjectTask task, IEnumerable<ProjectTask> tasks)
    {
        var list = tasks.ToList();
        return [.. Live(list, task.ProjectId).Where(t => Problem(task, t.Id, list).Length == 0).OrderBy(t => t.ParentId is not null).ThenBy(t => t.Order)];
    }

    public static ProjectTask Add(ProjectTask task, string blockerId) => task.BlockedBy.Contains(blockerId) ? task : task with { BlockedBy = [.. task.BlockedBy, blockerId] };

    public static ProjectTask Remove(ProjectTask task, string blockerId) => task with { BlockedBy = [.. task.BlockedBy.Where(id => id != blockerId)] };

    private static IEnumerable<ProjectTask> Live(IEnumerable<ProjectTask> tasks, string projectId) =>
        tasks.Where(t => t.Deleted is null && t.ProjectId == projectId);

    // Whether "from" waits on "to", directly or through others.
    private static bool Reaches(string from, string to, Dictionary<string, ProjectTask> all)
    {
        var seen = new HashSet<string>();
        var stack = new Stack<string>([from]);
        while (stack.TryPop(out var id))
        {
            if (id == to)
                return true;
            if (!seen.Add(id) || !all.TryGetValue(id, out var task))
                continue;
            foreach (var next in task.BlockedBy)
                stack.Push(next);
        }
        return false;
    }
}
