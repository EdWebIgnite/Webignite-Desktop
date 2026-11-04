using Webignite.Core.Renewals;
using Webignite.Core.Retainers;

namespace Webignite.Data;

/// <summary>Retainers and renewals (domains, hosting, licences). Everything but the id is encrypted.</summary>
public sealed partial class LocalStore
{
    private const string RetainerContext = "retainer:";
    private const string RenewalContext = "renewal:";

    public Retainer SaveRetainer(Retainer retainer) => Put("retainers", RetainerContext, retainer.Id, retainer);

    /// <summary>Stops a retainer. Invoices it already drafted stay.</summary>
    public void DeleteRetainer(string id) => Remove("retainers", id);

    public IReadOnlyList<Retainer> ListRetainers() =>
        ReadAll<Retainer>(RetainerContext, "SELECT id, payload FROM retainers")
            .OrderBy(r => r.ClientName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();

    public Renewal SaveRenewal(Renewal renewal) => Put("renewals", RenewalContext, renewal.Id, renewal);

    public void DeleteRenewal(string id) => Remove("renewals", id);

    /// <summary>All renewals, soonest first.</summary>
    public IReadOnlyList<Renewal> ListRenewals() =>
        ReadAll<Renewal>(RenewalContext, "SELECT id, payload FROM renewals")
            .OrderBy(r => r.RenewsOn).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
}
