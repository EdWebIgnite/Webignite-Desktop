namespace Webignite.Core.Documents;

public sealed record Customer
{
    public required string Name { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Attention { get; init; }
    public IReadOnlyList<string> AddressLines { get; init; } = [];
    public string? City { get; init; }
    public string? Region { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
}
