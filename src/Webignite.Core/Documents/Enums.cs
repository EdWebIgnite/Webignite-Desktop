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

/// <summary>Webignite is not VAT registered, so this is the only treatment for now.</summary>
public enum TaxTreatment
{
    NoVat,
}
