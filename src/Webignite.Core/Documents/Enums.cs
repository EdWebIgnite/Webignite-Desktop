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
}

