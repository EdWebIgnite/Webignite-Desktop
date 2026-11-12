using System.Security.Cryptography;
using Webignite.Data;
using Webignite.Xero.Auth;

namespace Webignite.Desktop.Tests;

/// <summary>
/// The app's services over a new, empty database in a throwaway folder, with nothing that reaches the internet,
/// Xero or Windows' own password store. Each test makes its own, so no test sees another's data.
/// </summary>
public sealed class TestApp : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("webignite-desktop-tests-").FullName;

    public TestApp()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        Store = new LocalStore(Path.Combine(folder, "test.db"), new Vault(key));
        Services = new AppServices(Store) { TokenStore = new MemoryTokenStore(), VaultKeySource = () => key };
        Services.Settings.Appearance = new AppearanceSettings { Motion = Motion.Off };
    }

    public LocalStore Store { get; }
    public AppServices Services { get; }

    /// <summary>A saved invoice or quote with one line.</summary>
    public StoredDocument Add(DocumentKind kind, string customer, string line, decimal price, DocumentStatus status = DocumentStatus.Draft)
    {
        var document = DocumentDefaults.Complete(
            new Document
            {
                Kind = kind,
                Origin = DocumentOrigin.New,
                Customer = new Customer { Name = customer },
                Lines = [new Line { Description = line, Quantity = 1, UnitPrice = price }],
            },
            Services.Settings.Documents, DateOnly.FromDateTime(DateTime.Today));
        return Store.Save(new StoredDocument { Id = StoredDocument.NewId(), Document = document, Status = status });
    }

    public void Dispose()
    {
        Services.Dispose();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // Windows can hold the database file a moment longer; the folder is in temp either way.
        }
    }
}
