using Webignite.Core.Xero;

namespace Webignite.Core.Documents;

/// <summary>Why a document is on the To approve list, most urgent first.</summary>
public enum WaitingKind
{
    /// <summary>Something went wrong talking to Xero and needs a look.</summary>
    XeroProblem,

    /// <summary>Claude or ChatGPT prepared it; nothing has gone to Xero.</summary>
    FromAssistant,

    /// <summary>A draft in Xero, not approved or emailed yet.</summary>
    XeroDraft,

    /// <summary>Approved in Xero but not emailed to the customer.</summary>
    NotEmailed,
}

public sealed record WaitingItem(StoredDocument Stored, WaitingKind Kind, string Reason);

public static class Approvals
{
    /// <summary>Everything that needs Eddie, most urgent first and oldest first within each kind.</summary>
    public static IReadOnlyList<WaitingItem> Waiting(IEnumerable<StoredDocument> documents) =>
        documents.Select(Why).OfType<WaitingItem>()
            .OrderBy(w => w.Kind)
            .ThenBy(w => w.Stored.Created)
            .ToList();

    /// <summary>
    /// Whether it can be thrown away from To approve: only what Claude or ChatGPT prepared and never reached Xero.
    /// Anything in Xero is dealt with in Xero.
    /// </summary>
    public static bool CanReject(WaitingItem item) => item.Kind == WaitingKind.FromAssistant && item.Stored.Xero.Id is null;

    /// <summary>
    /// Whether it can be taken off the list by hand: anything that's in Xero (for one deleted or changed there, say).
    /// It stays in its list and Xero isn't touched. What never reached Xero is rejected instead.
    /// </summary>
    public static bool CanRemove(WaitingItem item) => item.Stored.Xero.Id is not null;

    /// <summary>
    /// Whether Claude or ChatGPT may change it: only while it has never reached Xero, been approved, emailed or
    /// settled, and only one made here (an imported one keeps the figures of where it came from). From then on it
    /// is Eddie's to change in the app, where Xero's copy is kept in step.
    /// </summary>
    public static bool AssistantMayChange(StoredDocument s) =>
        AssistantMayChangeHere(s) || AssistantMayChangeXeroDraft(s);

    /// <summary>One that has never reached Xero: changed in place, then it waits under To approve to be created in Xero.</summary>
    public static bool AssistantMayChangeHere(StoredDocument s) =>
        s is { Xero: { Id: null, State: XeroWriteState.None }, Delivery: { Approved: null, Emailed: null }, Settled: null, Document.Origin: DocumentOrigin.New };

    /// <summary>
    /// A draft in Xero that Webignite made and nobody has approved, emailed or settled (<see cref="XeroEdits.CanChange"/>),
    /// or one Claude or ChatGPT is already changing. The change is kept here, and Xero keeps its copy until Eddie
    /// checks it and clicks Update draft in Xero. Not one Eddie is changing himself in the app.
    /// </summary>
    public static bool AssistantMayChangeXeroDraft(StoredDocument s) =>
        XeroEdits.CanChange(s)
        || s is { Xero.State: XeroWriteState.Editing, ChangedByAssistant: not null, Delivery: { Approved: null, Emailed: null }, Settled: null }
            && s.Document.Origin != DocumentOrigin.Xero;

    /// <summary>
    /// Whether Eddie can delete it here: only while it has never reached Xero. Once Xero has it, it is voided or
    /// deleted there (Void in Xero), so the two never disagree.
    /// </summary>
    public static bool CanDelete(StoredDocument s) =>
        s is { Xero: { Id: null, State: XeroWriteState.None }, Delivery: { Approved: null, Emailed: null }, Settled: null, Document.Origin: not DocumentOrigin.Xero }
        // An old one brought in from a spreadsheet is only a record here, whatever became of it.
        || s is { Document.Origin: DocumentOrigin.Import, Xero.Id: null };

    public static WaitingItem? Why(StoredDocument s)
    {
        var what = s.Document.Kind == DocumentKind.Quote ? "quote" : "invoice";
        var by = s.Source == DocumentSource.ChatGpt ? "ChatGPT" : "Claude";
        return s switch
        {
            // Settled (paid, accepted, deleted or voided in Xero) or taken off by hand: nothing left to do here.
            { Settled: not null } or { OffApprovals: not null } => null,
            { Xero.State: XeroWriteState.Writing or XeroWriteState.Uncertain } =>
                new(s, WaitingKind.XeroProblem, "It isn't clear whether Xero got it. Open it and use Check Xero."),
            { Xero.State: XeroWriteState.Mismatch } =>
                new(s, WaitingKind.XeroProblem, "Xero's copy differs from this one. Open it to see how."),
            { Xero.State: XeroWriteState.Editing, ChangedByAssistant: not null } =>
                new(s, WaitingKind.XeroDraft, $"{by} changed this draft {what}; Xero still has the old copy. Open it to check, then Update draft in Xero, or Discard changes to keep Xero's."),
            { Xero.State: XeroWriteState.Editing } =>
                new(s, WaitingKind.XeroDraft, "Being changed here; Xero still has the old copy. Open it and use Update draft in Xero."),
            // Drafts made in Xero itself are dealt with in Xero.
            { Document.Origin: DocumentOrigin.Xero, Delivery.Emailed: null } => null,
            { Xero.State: XeroWriteState.None, Status: DocumentStatus.AwaitingApproval } =>
                new(s, WaitingKind.FromAssistant, s.ChangedByAssistant is null
                    ? $"{by} prepared this {what}. Check it, then create it in Xero or reject it."
                    : $"{by} changed this {what}. Check it, then create it in Xero or reject it."),
            { Xero.State: XeroWriteState.Verified, Delivery: { Emailed: null, Approved: null } } =>
                new(s, WaitingKind.XeroDraft, s.Document.Kind == DocumentKind.Quote
                    ? "Draft quote in Xero, not emailed yet."
                    : "Draft invoice in Xero, not approved or emailed yet."),
            { Xero.State: XeroWriteState.Verified, Delivery: { Emailed: null, Approved: not null } } =>
                new(s, WaitingKind.NotEmailed, "Approved in Xero but not emailed to the customer."),
            _ => null,
        };
    }
}
