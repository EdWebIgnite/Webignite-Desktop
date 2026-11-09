namespace Webignite.Core.Documents;

/// <summary>Attaching a file to a quote or invoice in Xero, such as a timesheet or a signed proposal.</summary>
public static class DocumentAttachments
{
    /// <summary>Xero's limit for one attached file.</summary>
    public const long MaxBytes = 25L * 1024 * 1024;

    /// <summary>An invoice once it's in Xero (checked, or being changed). Xero takes attachments on invoices, not quotes.</summary>
    public static bool CanAttach(StoredDocument s) =>
        s.Document.Kind == DocumentKind.Invoice
        && s.Xero.Id is not null && s.Xero.State is Xero.XeroWriteState.Verified or Xero.XeroWriteState.Mismatch or Xero.XeroWriteState.Editing;

    /// <summary>Why the file can't be attached, or null when it can.</summary>
    public static string? Check(string name, long bytes) =>
        bytes == 0 ? $"{name} is empty."
        : bytes > MaxBytes ? $"{name} is bigger than the 25 MB Xero takes. Shrink it first (PDF tools or the picture viewer can)."
        : null;

    /// <summary>What Xero is told the file is, from its extension.</summary>
    public static string MediaType(string name) => Path.GetExtension(name).ToUpperInvariant() switch
    {
        ".PDF" => "application/pdf",
        ".PNG" => "image/png",
        ".JPG" or ".JPEG" => "image/jpeg",
        ".GIF" => "image/gif",
        ".DOCX" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".XLSX" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".CSV" => "text/csv",
        ".TXT" => "text/plain",
        _ => "application/octet-stream",
    };

    /// <summary>One line per attached file, for the document's page.</summary>
    public static IReadOnlyList<string> Describe(StoredDocument s) =>
        s.Attachments.Select(a => $"{a.Name} is attached in Xero{(a.Online ? ", shown on the online invoice" : "")}.").ToList();
}
