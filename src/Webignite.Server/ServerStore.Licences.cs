namespace Webignite.Server;

/// <summary>Each workspace's plan: a trial from when it was made, a subscription paid until a day, or free.</summary>
public sealed partial class ServerStore
{
    public sealed record WorkspacePlan(string WorkspaceId, string Plan, DateTimeOffset Created, DateTimeOffset? PaidUntil);

    public WorkspacePlan? PlanOf(string workspaceId) =>
        Read("SELECT id, plan, created, paid_until FROM workspaces WHERE id = $w",
            r => new WorkspacePlan(r.GetString(0), r.GetString(1), When(r.GetString(2)), Text(r, 3) is { } paid ? When(paid) : null),
            ("$w", workspaceId)).FirstOrDefault();

    /// <summary>Sets the plan of the workspace an account owns. False when there's no such owner.</summary>
    public bool SetPlan(string ownerEmail, string plan, DateTimeOffset? paidUntil) =>
        Write("""
            UPDATE workspaces SET plan = $plan, paid_until = $paid
            WHERE id IN (SELECT m.workspace_id FROM members m JOIN accounts a ON a.id = m.account_id WHERE a.email = $email AND m.role = 'owner')
            """, ("$plan", plan), ("$paid", paidUntil is { } p ? When(p) : null), ("$email", ownerEmail)) > 0;

    private int Write(string sql, params (string Name, object? Value)[] values)
    {
        using var db = Open();
        using var command = Command(db, sql, values);
        return command.ExecuteNonQuery();
    }
}
