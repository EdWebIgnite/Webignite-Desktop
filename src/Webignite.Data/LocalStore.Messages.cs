using Webignite.Core.Projects;

namespace Webignite.Data;

/// <summary>Messages on projects' Messages tabs. The project's id is in the clear to find them by; the rest is encrypted.</summary>
public sealed partial class LocalStore
{
    private const string ProjectMessageContext = "project-message:";

    public ProjectMessage SaveMessage(ProjectMessage message)
    {
        Run("INSERT INTO project_messages (id, project_id, payload) VALUES ($id, $project, $payload) ON CONFLICT (id) DO UPDATE SET project_id = $project, payload = $payload",
            ("$id", message.Id), ("$project", message.ProjectId), ("$payload", Seal(ProjectMessageContext + message.Id, message)));
        return message;
    }

    public ProjectMessage? GetMessage(string id) => Find<ProjectMessage>("project_messages", ProjectMessageContext, id);

    public IReadOnlyList<ProjectMessage> ListMessages(string projectId) =>
        ReadAll<ProjectMessage>(ProjectMessageContext, "SELECT id, payload FROM project_messages WHERE project_id = $project", ("$project", projectId));

    public IReadOnlyList<ProjectMessage> ListAllMessages() => ReadAll<ProjectMessage>(ProjectMessageContext, "SELECT id, payload FROM project_messages");
}
