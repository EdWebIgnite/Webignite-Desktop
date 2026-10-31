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

    public static WaitingItem? Why(StoredDocument s)
    {
        var what = s.Document.Kind == DocumentKind.Quote ? "quote" : "invoice";
        var by = s.Source == DocumentSource.ChatGpt ? "ChatGPT" : "Claude";
        return s switch
        {
            { XeroState: XeroWriteState.Writing or XeroWriteState.Uncertain } =>
                new(s, WaitingKind.XeroProblem, "It isn't clear whether Xero got it. Open it and use Check Xero."),
            { XeroState: XeroWriteState.Mismatch } =>
                new(s, WaitingKind.XeroProblem, "Xero's copy differs from this one. Open it to see how."),
            { XeroState: XeroWriteState.None, Status: DocumentStatus.AwaitingApproval } =>
                new(s, WaitingKind.FromAssistant, $"{by} prepared this {what}. Check it, then create it in Xero or reject it."),
            { XeroState: XeroWriteState.Verified, Emailed: null, Approved: null } =>
                new(s, WaitingKind.XeroDraft, s.Document.Kind == DocumentKind.Quote
                    ? "Draft quote in Xero, not emailed yet."
                    : "Draft invoice in Xero, not approved or emailed yet."),
            { XeroState: XeroWriteState.Verified, Emailed: null, Approved: not null } =>
                new(s, WaitingKind.NotEmailed, "Approved in Xero but not emailed to the customer."),
            _ => null,
        };
    }
}
