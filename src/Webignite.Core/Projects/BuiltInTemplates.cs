namespace Webignite.Core.Projects;

/// <summary>A template that comes with the app, for the gallery: what it's for, where it's filed, and its look.</summary>
public sealed record BuiltInTemplate(ProjectTemplate Template, string Category, string Description, string Icon, string Colour, bool Board)
{
    public string Id => Template.Id;
    public string Name => Template.Name;
}

/// <summary>
/// The templates that come with the app, laid out after Asana's gallery and filed under its categories. Asana
/// shows a template's contents only once signed in, so the headings and tasks here are our own, written for a
/// small web and marketing agency from what Asana says each template is for. Days count from the project's start.
/// </summary>
public static class BuiltInTemplates
{
    public const string Marketing = "Marketing";
    public const string Operations = "Operations & PMO";
    public const string Design = "Design";
    public const string It = "IT";
    public const string Product = "Product & engineering";
    public const string People = "HR";
    public const string Sales = "Sales & CX";

    private static readonly string[] Order = [Marketing, Operations, Design, It, Product, People, Sales];

    /// <summary>The categories that have templates, in the gallery's order.</summary>
    public static IReadOnlyList<string> Categories => [.. Order.Where(c => All.Any(t => t.Category == c))];

    private static TemplateTask T(string title, string heading, int? due = null, int? start = null, string notes = "",
        TaskPriority priority = TaskPriority.Normal, string[]? tags = null, string[]? subtasks = null, TaskRepeat repeat = TaskRepeat.None,
        bool milestone = false) => new()
        {
            Milestone = milestone,
            Title = title,
            Heading = heading,
            DueDay = due,
            StartDay = start,
            Notes = notes,
            Priority = priority,
            Tags = tags ?? [],
            Subtasks = subtasks ?? [],
            Repeat = repeat,
        };

    private static BuiltInTemplate Make(string id, string name, string category, string description, string icon, string colour, bool board,
        string[] headings, params TemplateTask[] tasks) =>
        new(new ProjectTemplate { Id = "builtin-" + id, Name = name, Headings = headings, Tasks = tasks }, category, description, icon, colour, board);

    public static IReadOnlyList<BuiltInTemplate> All { get; } =
    [
        Make("web-design", "Web design process", Design,
            "Take a website from the first brief to launch day: goals, sitemap, design, build, testing and go-live.",
            "PageLayout", "Blue", board: false,
            ["Discovery and goals", "Planning and scope", "Design", "Development", "Testing and launch"],
            T("Kick-off call with the client", "Discovery and goals", 2, notes: "Goals, audience, competitors, sites they like and don't like."),
            T("Collect logins, logo and brand files", "Discovery and goals", 4, subtasks: ["Domain and hosting logins", "Logo in vector", "Brand colours and fonts", "Photos"]),
            T("Write the project brief", "Discovery and goals", 5),
            T("Agree the sitemap", "Planning and scope", 8),
            T("Wireframes for the main pages", "Planning and scope", 12, subtasks: ["Home", "About", "Services", "Contact"]),
            T("Confirm scope and quote", "Planning and scope", 13, notes: "Anything new after this is a change request."),
            T("Content from the client", "Planning and scope", 15, notes: "Copy, photos and any downloads for each page."),
            T("Home page design", "Design", 19),
            T("Inner page designs", "Design", 23),
            T("Client feedback on the designs", "Design", 25, priority: TaskPriority.High),
            T("Build the pages", "Development", 32, start: 26),
            T("Contact form and spam filter", "Development", 33),
            T("SEO basics: titles, descriptions and sitemap", "Development", 34),
            T("Test on phone, tablet and desktop", "Testing and launch", 36),
            T("Speed test and fix what's slow", "Testing and launch", 37),
            T("Client sign-off", "Testing and launch", 38, priority: TaskPriority.High, milestone: true),
            T("Point the domain and go live", "Testing and launch", 40, milestone: true),
            T("Hand over logins and a short how-to", "Testing and launch", 42)),

        Make("client-onboarding", "Client onboarding", Sales,
            "Welcome a new client properly: contract and first invoice, a welcome pack, access to their tools, and a kick-off.",
            "People", "Aqua", board: false,
            ["Internal kick-off", "Welcome", "Contract and billing", "Access and setup", "Kick-off and first month"],
            T("Agree who looks after the client", "Internal kick-off", 1),
            T("Set up the client in Webignite", "Internal kick-off", 1, notes: "Contacts, website and the Xero contact."),
            T("Send the welcome mail", "Welcome", 2),
            T("Send the brand questionnaire", "Welcome", 2),
            T("Send the contract to sign", "Contract and billing", 2, priority: TaskPriority.High),
            T("Send the first invoice", "Contract and billing", 3),
            T("Check the first invoice is paid", "Contract and billing", 10),
            T("Get access to their website, domain and analytics", "Access and setup", 5, subtasks: ["Website admin", "Domain registrar", "Google Analytics", "Google Search Console", "Google Business Profile"]),
            T("Save their logins in Webignite", "Access and setup", 6),
            T("Kick-off meeting", "Kick-off and first month", 7),
            T("Present the plan for the first month", "Kick-off and first month", 10),
            T("Two-week check-in", "Kick-off and first month", 14),
            T("First monthly report", "Kick-off and first month", 30)),

        Make("content-calendar", "Content calendar", Marketing,
            "Plan posts, blogs and mailers in one place, and take each from idea to draft, edit, approval and publishing.",
            "Calendar", "Orange", board: true,
            ["Ideas", "Writing", "Editing", "Approved", "Published"],
            T("Blog: five signs your website needs a refresh", "Ideas", tags: ["Blog"]),
            T("Facebook and Instagram posts for the month", "Ideas", tags: ["Social"]),
            T("Monthly mailer", "Writing", 10, tags: ["Email"], subtasks: ["Write the copy", "Pick the photos", "Test send"]),
            T("Case study: latest website launch", "Writing", 14, tags: ["Blog"]),
            T("LinkedIn post about the case study", "Editing", 16, tags: ["Social"]),
            T("Google Business Profile post", "Approved", 7, tags: ["Local"]),
            T("Plan next month's content", "Ideas", 25, repeat: TaskRepeat.Monthly)),

        Make("creative-requests", "Creative requests", Design,
            "Take design requests in one place and move each from new to delivered, so nothing waits in someone's inbox.",
            "LightBulb", "Purple", board: true,
            ["New requests", "In progress", "In review", "Approved", "Delivered"],
            T("Example: social banner for a client promotion", "New requests", 3,
                notes: "What it's for, sizes, wording, the deadline and who signs it off.", tags: ["Social"]),
            T("Example: logo refresh", "In progress", 7, tags: ["Brand"], subtasks: ["Three rough ideas", "Refine the chosen one", "Export all sizes"]),
            T("Example: flyer for print", "In review", 5, tags: ["Print"])),

        Make("bug-tracking", "Bug tracking", Product,
            "Log website bugs with the steps to repeat them, sort them by how bad they are, and see each through to fixed.",
            "Bug", "Red", board: true,
            ["Triage", "To fix", "Fixing", "Ready to test", "Fixed"],
            T("Example: contact form doesn't send on mobile", "Triage", priority: TaskPriority.High,
                notes: "Steps to repeat:\n1. Open the contact page on a phone\n2. Fill in the form\n3. Tap Send\n\nWhat happens:\nWhat should happen:\nBrowser and device:",
                tags: ["Forms"]),
            T("Example: menu overlaps the logo on tablets", "To fix", tags: ["Layout"]),
            T("Example: images load slowly on the home page", "Fixing", priority: TaskPriority.Low, tags: ["Speed"])),

        Make("meeting-agenda", "Meeting agenda", Operations,
            "Run focused meetings: what it's for, topics with an owner and a time slot, actions afterwards, and the papers to read.",
            "Notebook", "YellowOrange", board: false,
            ["Meeting overview", "Topic suggestions", "Action items", "Reference materials"],
            T("Purpose, date and who's coming", "Meeting overview", 0),
            T("Example topic: where the projects stand (10 minutes)", "Topic suggestions", 0),
            T("Example topic: money in and owed (10 minutes)", "Topic suggestions", 0),
            T("Example action: send the notes round", "Action items", 1),
            T("Last meeting's notes", "Reference materials")),

        Make("one-on-one", "1:1 meeting agenda", People,
            "Keep regular one-on-ones on track: what to talk about this week, what to come back to, wins, roadblocks and goals.",
            "ChatBubbles", "Pink", board: false,
            ["Discuss this week", "Revisit later", "Roadblocks and wins", "Goals", "Action items"],
            T("How was the week?", "Discuss this week", 0, repeat: TaskRepeat.Weekly),
            T("Example: workload over the next month", "Discuss this week", 0),
            T("Example: training to look at", "Revisit later"),
            T("A win to share", "Roadblocks and wins"),
            T("Anything in the way", "Roadblocks and wins"),
            T("Goals for this quarter", "Goals"),
            T("Example action: book the course", "Action items", 7)),

        Make("kanban", "Kanban board", Operations,
            "See work move across the board from to do to done, and spot where it gets stuck.",
            "Board", "Indigo", board: true,
            ["To do", "In progress", "Waiting on someone", "Done"],
            T("Example: renew a client's domain", "To do", 5),
            T("Example: update plugins on client sites", "To do", 7, repeat: TaskRepeat.Monthly),
            T("Example: SEO report for a client", "In progress", 3),
            T("Example: waiting for photos from a client", "Waiting on someone")),

        Make("project-timeline", "Project timeline", Operations,
            "Map out a project's stages with start and end dates, so the milestones and deadlines are clear from day one.",
            "Timeline", "BlueGreen", board: false,
            ["Project brief", "Planning", "Doing the work", "Milestones", "Wrap-up"],
            T("Write the brief: goals, scope, budget and dates", "Project brief", 2),
            T("Agree the plan and who does what", "Planning", 5, start: 3),
            T("Book the people and tools", "Planning", 6, start: 5),
            T("First stage", "Doing the work", 14, start: 7),
            T("Second stage", "Doing the work", 24, start: 15),
            T("Milestone: first stage signed off", "Milestones", 14, priority: TaskPriority.High),
            T("Milestone: project delivered", "Milestones", 28, priority: TaskPriority.High),
            T("Look back: what went well and what to change", "Wrap-up", 30)),

        Make("request-tracking", "Request tracking", Operations,
            "Capture requests as they come in, sort them, and track each from new to complete with a turnaround to aim for.",
            "Ticket", "Yellow", board: true,
            ["New", "In progress", "Pending", "Complete"],
            T("Example: client asks for a new page", "New", 3, notes: "Who asked, what they need, by when. Aim to reply within 3 working days.", tags: ["Website"]),
            T("Example: change the opening hours on the site", "In progress", 1, tags: ["Website"]),
            T("Example: quote for a second site", "Pending", 3, tags: ["Sales"])),

        Make("new-hire", "New hire checklist", People,
            "Get a new person settled: everything ready before day one, then their first day, first week and first month.",
            "Rocket", "YellowGreen", board: false,
            ["Before day one", "Day one", "First week", "First month"],
            T("Laptop, email address and logins ready", "Before day one", -3, subtasks: ["Laptop set up", "Email address", "Webignite access", "Shared folders"]),
            T("Send the welcome mail with the first-day plan", "Before day one", -2),
            T("Contract and paperwork signed", "Before day one", -5, priority: TaskPriority.High),
            T("Welcome and a walk through the tools", "Day one", 0),
            T("Lunch with the team", "Day one", 0),
            T("Shadow a client call", "First week", 3),
            T("First small project", "First week", 5),
            T("Check-in: how's the first month going?", "First month", 30)),
    ];

    public static BuiltInTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    public static IReadOnlyList<BuiltInTemplate> In(string category) => [.. All.Where(t => t.Category == category)];
}
