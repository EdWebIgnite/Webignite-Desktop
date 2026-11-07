namespace Webignite.Core.Assistants;

/// <summary>
/// How much Claude and ChatGPT may write in one go, so a runaway or tricked call can't fill the app with megabytes,
/// and the rule that they never empty a piece of writing that has something in it (that would be a delete).
/// </summary>
public static class AssistantLimits
{
    public const int Title = 300;
    public const int Text = 20_000;

    /// <summary>A problem when <paramref name="typed"/> is longer than <paramref name="max"/>; empty when it's fine.</summary>
    public static string TooLong(string? typed, string what, int max) =>
        typed is not null && typed.Length > max ? $"Keep {what} under {max} characters." : "";

    /// <summary>A problem when writing that has something in it would be emptied; empty when it's fine.</summary>
    public static string Emptying(string? before, string? after, string what) =>
        after is not null && after.Trim().Length == 0 && !string.IsNullOrWhiteSpace(before)
            ? $"You can't clear {what}. Eddie can do that in Webignite if it should go."
            : "";

    /// <summary>The first problem among several, or empty.</summary>
    public static string First(params string[] problems) => problems.FirstOrDefault(p => p.Length > 0) ?? "";
}
