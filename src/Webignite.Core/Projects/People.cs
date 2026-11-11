using System.Globalization;

namespace Webignite.Core.Projects;

/// <summary>
/// The names of the two of you. Each PC names its own person (Settings, Your organisation) and sends that name
/// with each full sync, so the other PC shows it too. Until a name is typed, it comes from the person's sign-in
/// email, so nobody shows as "Me" or "Partner" once signed in.
/// </summary>
public static class PeopleRules
{
    public const string DefaultMe = "Me";

    /// <summary>Still the name Webignite made up, not one anyone chose.</summary>
    public static bool IsDefault(Teammate person) =>
        string.IsNullOrWhiteSpace(person.Name) || person.Name is DefaultMe or ProjectRules.DefaultPartner;

    /// <summary>A name from an email address: "eddie@webignite.co.za" is Eddie, "jaco.van.wyk@..." Jaco Van Wyk.</summary>
    public static string NameFromEmail(string? email)
    {
        var local = (email ?? "").Split('@')[0].Split('+')[0];
        var words = local.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Any(char.IsLetter))
            .Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..].ToLower(CultureInfo.InvariantCulture));
        return string.Join(' ', words);
    }

    /// <summary>
    /// The team with made-up names replaced from the workspace's emails: mine from my sign-in, my partner's from
    /// the other person in the workspace. Names someone typed stay. Returns the same team when nothing changes.
    /// </summary>
    public static TeamSettings Named(TeamSettings team, string? myEmail, IEnumerable<string> workspaceEmails)
    {
        if (team.Me is not { } me || team.Partner is not { } partner)
            return team;
        var theirs = workspaceEmails.FirstOrDefault(e => !string.Equals(e, myEmail, StringComparison.OrdinalIgnoreCase));
        var newMe = me;
        if (myEmail is { Length: > 0 } && IsDefault(me) && NameFromEmail(myEmail) is { Length: > 0 } mine)
            newMe = me with { Name = mine };
        var newPartner = partner;
        if (theirs is { Length: > 0 })
        {
            if (string.IsNullOrWhiteSpace(newPartner.Email))
                newPartner = newPartner with { Email = theirs };
            if (IsDefault(newPartner) && NameFromEmail(theirs) is { Length: > 0 } name && !string.Equals(name, newMe.Name, StringComparison.OrdinalIgnoreCase))
                newPartner = newPartner with { Name = name };
        }
        return newMe == me && newPartner == partner ? team : team with { Me = newMe, Partner = newPartner };
    }

    /// <summary>
    /// A person as their own PC sent it. My partner's own name replaces whatever this PC called them; my own name
    /// from my other PC is taken only while this one still has the made-up one. Made-up names are ignored.
    /// Returns the same team when nothing changes.
    /// </summary>
    public static TeamSettings TakeIn(TeamSettings team, Teammate person)
    {
        if (IsDefault(person))
            return team;
        var name = person.Name.Trim();
        if (team.Partner is { } partner && partner.Id == person.Id)
        {
            var taken = partner with { Name = name, Email = person.Email is { Length: > 0 } email ? email : partner.Email };
            return taken == partner ? team : team with { Partner = taken };
        }
        if (team.Me is { } me && me.Id == person.Id && IsDefault(me))
            return team with { Me = me with { Name = name } };
        return team;
    }
}
