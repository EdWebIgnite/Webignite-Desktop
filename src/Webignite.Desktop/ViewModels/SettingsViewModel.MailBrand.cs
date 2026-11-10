using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Documents;

namespace Webignite.Desktop.ViewModels;

// Settings, Email wording: how quote and invoice mails look (the branded layout around the words). Shared with the
// other PC, like the wording; kept in settings rather than in the app, as it holds the bank account.
public partial class SettingsViewModel
{
    [ObservableProperty] public partial string BrandCompany { get; set; } = "";
    [ObservableProperty] public partial string BrandAddress { get; set; } = "";
    [ObservableProperty] public partial string BrandPhone { get; set; } = "";
    [ObservableProperty] public partial string BrandEmail { get; set; } = "";
    [ObservableProperty] public partial string BrandWebsite { get; set; } = "";
    [ObservableProperty] public partial string BrandBank { get; set; } = "";
    [ObservableProperty] public partial string BrandTerms { get; set; } = "";
    [ObservableProperty] public partial string BrandMessage { get; set; } = "";
    [ObservableProperty] public partial bool BrandProblem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBrandLogo))]
    public partial Bitmap? BrandLogo { get; private set; }

    public bool HasBrandLogo => BrandLogo is not null;

    private (string Base64, string Type)? brandLogoData;

    private void LoadBrand()
    {
        var brand = services.Settings.MailBrand;
        BrandCompany = brand.CompanyName;
        BrandAddress = brand.Address;
        BrandPhone = brand.Phone;
        BrandEmail = brand.Email;
        BrandWebsite = brand.Website;
        BrandBank = brand.BankDetails;
        BrandTerms = brand.TermsLink;
        ShowLogo(brand.Logo is { Length: > 0 } logo ? (logo, brand.LogoType) : null);
    }

    /// <summary>The logo picked in the view: PNG or JPEG, at most 400 KB.</summary>
    public void SetBrandLogo(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MailBrand.LargestLogo)
        {
            SayBrand("The logo must be a PNG or JPEG under 400 KB. Make it smaller (Website images can shrink it) and try again.", problem: true);
            return;
        }
        var type = Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" ? "image/jpeg" : "image/png";
        ShowLogo((Convert.ToBase64String(File.ReadAllBytes(path)), type));
        SayBrand("Logo picked. Save to keep it.", problem: false);
    }

    [RelayCommand]
    private void RemoveBrandLogo()
    {
        ShowLogo(null);
        SayBrand("Logo taken off. Save to keep it that way.", problem: false);
    }

    [RelayCommand]
    private void SaveBrand()
    {
        if (BrandTerms.Trim() is { Length: > 0 } terms && !Uri.TryCreate(terms, UriKind.Absolute, out _))
        {
            SayBrand("The terms link should be a full web address, starting with https://.", problem: true);
            return;
        }
        services.Settings.MailBrand = new MailBrand
        {
            Logo = brandLogoData?.Base64,
            LogoType = brandLogoData?.Type ?? "image/png",
            CompanyName = BrandCompany.Trim(),
            Address = BrandAddress.Trim(),
            Phone = BrandPhone.Trim(),
            Email = BrandEmail.Trim(),
            Website = BrandWebsite.Trim(),
            BankDetails = BrandBank.Trim(),
            TermsLink = BrandTerms.Trim(),
        };
        SayBrand($"Saved at {DateTime.Now:HH:mm}. Quote and invoice mails now go out in this layout, on both PCs.", problem: false);
    }

    /// <summary>Opens a sample invoice mail to yourself in the layout, to see how it looks before clients do.</summary>
    [RelayCommand]
    private void SendBrandTest()
    {
        SaveBrand();
        if (BrandProblem)
            return;
        if (services.Drafts.InvoiceMailbox is not { } mailbox)
        {
            SayBrand(MailDrafts.NoInvoiceMailbox, problem: true);
            return;
        }
        var sample = new DocumentEmailValues
        {
            Kind = DocumentKind.Invoice,
            ContactName = "Test client",
            Number = "INV-TEST",
            Total = 1000m,
            AmountDue = 1000m,
            DueOrExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(7),
            OnlineLink = "https://www.xero.com",
            TradingName = services.Settings.Xero.TenantName ?? "Webignite",
        };
        var opened = services.Drafts.Open(new MailDraft(mailbox, mailbox.Address, "Test: how our invoice mails look",
            "Hi,\n\nThis is how an invoice mail looks. The button would open the invoice in Xero.\n\nThanks")
        {
            Note = "A test to yourself. It goes out in the branded layout, so you see what clients see.",
            Layout = () => DocumentMailLayout.Build(sample, services.Settings.MailBrand, mailbox.Address),
        });
        SayBrand(opened ? "The test mail is open in Mail. Send it to see it in your inbox." : MailDrafts.AnotherOpen, problem: !opened);
    }

    private void ShowLogo((string Base64, string Type)? logo)
    {
        brandLogoData = logo;
        var old = BrandLogo;
        BrandLogo = null;
        old?.Dispose();
        if (logo is { } data)
        {
            try
            {
                using var stream = new MemoryStream(Convert.FromBase64String(data.Base64));
                BrandLogo = Bitmap.DecodeToHeight(stream, 60);
            }
            catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                brandLogoData = null;
                SayBrand("That picture couldn't be read. Pick a PNG or JPEG.", problem: true);
            }
        }
    }

    private void SayBrand(string message, bool problem)
    {
        BrandMessage = message;
        BrandProblem = problem;
    }
}
