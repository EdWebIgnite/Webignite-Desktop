using System.Text.Json;

namespace Webignite.Core.Sync;

/// <summary>
/// Settings that belong to the business, not to one PC, so both of you have the same: how long quotes and invoices
/// run, the email wording, the price list, project templates, the income plan, and the Xero app with its sales account
/// and branding theme. Each Xero sign-in stays on its own PC (in Credential Manager), as do mail, looks and layout.
/// </summary>
public static class SharedSettings
{
    public static readonly IReadOnlyList<string> Names = ["documents", "email-templates", "price-list", "project-templates", "income-plan", "xero", "mail-brand"];

    /// <summary>The setting that holds when each shared setting was last changed on this PC or arrived.</summary>
    public const string StampsName = "shared-settings-changed";

    public static bool IsShared(string name) => Names.Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// Whether a setting arriving from the other PC replaces this PC's: the later change wins. Settings changed before
    /// they synced carry no time; then the workspace owner's copy wins, so a partner joining gets the owner's setup.
    /// </summary>
    public static bool TakeArriving(DateTimeOffset? here, DateTimeOffset arriving, bool ownerHere) =>
        here is not { } mine || arriving > mine || arriving == mine && !ownerHere;
}

/// <summary>A shared setting as it travels: its name, its value as the PC keeps it, and when it was last changed.</summary>
public sealed record SharedSetting(string Name, JsonElement Value, DateTimeOffset Updated);
