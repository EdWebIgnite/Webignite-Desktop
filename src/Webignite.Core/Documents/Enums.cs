namespace Webignite.Core.Documents;

public enum DocumentKind
{
    Quote,
    Invoice,
}

public enum DocumentOrigin
{
    /// <summary>Created from chat or in the app.</summary>
    New,

    /// <summary>Read from an old QuickBooks PDF during migration.</summary>
    PdfImport,

    /// <summary>Made in Xero itself (not from Webignite) and brought in to be seen here. Changed in Xero only.</summary>
    Xero,
}

