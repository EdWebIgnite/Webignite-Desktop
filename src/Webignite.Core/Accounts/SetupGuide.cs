namespace Webignite.Core.Accounts;

/// <summary>Where this PC is in the setup guide: the step on screen, and whether it was finished or closed.</summary>
public sealed record SetupProgress(string At, bool Finished);

/// <summary>What a setup step waits for before it shows as done.</summary>
public enum SetupCheck { None, SignedIn, XeroConnected, ChatGptConnected }

/// <summary>
/// One step of the setup guide: which page it shows (a page's name, and for Settings which section), what it says,
/// and what it waits for, if anything.
/// </summary>
public sealed record SetupStep(string Key, string Title, string Text, string Page, string? Section = null, SetupCheck Check = SetupCheck.None,
    string DoneText = "");

/// <summary>
/// The setup guide: a strip across the top of the app that takes a new person through joining the workspace,
/// connecting Xero and ChatGPT, then round the main pages. It shows the real pages, so nothing is set up twice.
/// </summary>
public static class SetupGuide
{
    public static readonly IReadOnlyList<SetupStep> Steps =
    [
        new("join", "Join the workspace",
            "Type the invite code you were sent, your email and a password of your own, then click Create the account. Your password never leaves this PC. Keep the recovery key it shows you somewhere safe: it's the only way back in if you forget the password.",
            "Settings", "account", SetupCheck.SignedIn,
            "You're in. Quotes, invoices, clients, projects and the business's settings come over from the sync server."),
        new("xero", "Connect Xero",
            "The Xero app is filled in already. Click Connect Xero, sign in to Xero in your browser and pick the same organisation. Webignite only makes drafts in Xero: approving and sending happen when you click to email a quote or invoice.",
            "Settings", "xero", SetupCheck.XeroConnected,
            "Xero is connected."),
        new("chatgpt", "Connect ChatGPT",
            "Go through the four steps under ChatGPT: make a tunnel and a key on OpenAI's site, paste them here, click Connect ChatGPT, then add it in ChatGPT. ChatGPT can then draft quotes, to-dos and notes for you, but never approves or sends anything.",
            "Settings", "assistants", SetupCheck.ChatGptConnected,
            "ChatGPT's tunnel is running on this PC. Add it in ChatGPT (step 4) if you haven't yet."),
        new("overview", "Overview",
            "Your day at a glance: what needs you, money coming in, quotes waiting and today's tasks. Webignite opens here.",
            "Overview"),
        new("approvals", "To approve",
            "Quotes and invoices waiting for a yes. You both see the same list: once one of you approves and emails one, it's done on both PCs. Remove takes one off the list, such as a quote deleted in Xero.",
            "Approvals"),
        new("quotes", "Quotes",
            "Every quote and where it stands in Xero. Open one to change it, then email it from Webignite, never from Xero. Client accepted or Client declined marks it in Xero.",
            "Quotes"),
        new("invoices", "Invoices",
            "Every invoice, what's paid and what's late. Webignite checks Xero for payments every half hour and can chase late ones for you.",
            "Invoices"),
        new("clients", "Clients",
            "Each client in one place: their people, quotes, invoices, projects, files, notes and site logins.",
            "Clients"),
        new("planner", "Planner",
            "Projects and tasks you share. Use List, Board, Timeline or Calendar, give tasks to each other, and type @ in a comment to tell the other person. Inbox shows what's new for you.",
            "Projects"),
        new("chat", "Chat",
            "The Chat button at the bottom of the sidebar opens a small window for messages, files and screenshots between the two of you. Paste a screenshot with Ctrl+V.",
            "Overview"),
        new("search", "Find anything",
            "Press Ctrl+K to find a client, quote, invoice, task, note or file by name. Press F1 for the other shortcuts.",
            "Overview"),
        new("done", "You're set up",
            "What either of you changes reaches the other PC within a minute. You can go through this guide again from Settings, Account and sync.",
            "Overview"),
    ];

    /// <summary>Where the guide starts: joining, unless this PC is signed in already, then Xero.</summary>
    public static int Start(bool signedIn) => signedIn ? 1 : 0;

    public static int Next(int at) => Math.Min(at + 1, Steps.Count - 1);

    public static int Back(int at) => Math.Max(at - 1, 0);

    public static bool IsLast(int at) => at == Steps.Count - 1;

    /// <summary>"Step 2 of 12".</summary>
    public static string Progress(int at) => $"Step {at + 1} of {Steps.Count}";

    /// <summary>The main button: Next, Skip for now while a step isn't done yet, or Finish on the last.</summary>
    public static string NextLabel(int at, bool done) =>
        IsLast(at) ? "Finish" : Steps[at].Check != SetupCheck.None && !done ? "Skip for now" : "Next";
}
