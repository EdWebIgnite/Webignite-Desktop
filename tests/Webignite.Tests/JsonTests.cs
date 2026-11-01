using System.Text.Json;

namespace Webignite.Tests;

public class JsonTests
{
    private const string Chat = """
        {
          "kind": "quote",
          "origin": "new",
          "customer": { "name": "Karoo Coffee Roasters", "email": "accounts@example.co.za" },
          "lines": [
            { "description": "Website redesign", "quantity": "1", "unit_price": "8500.00" },
            { "description": "Monthly hosting", "quantity": 12, "unit_price": 450 }
          ],
          "notes": "Valid for 30 days."
        }
        """;

    [Fact]
    public void Reads_the_snake_case_shape_assistants_send()
    {
        var doc = DocumentJson.Parse(Chat);
        Assert.Equal(DocumentKind.Quote, doc.Kind);
        Assert.Equal(8500m, doc.Lines[0].UnitPrice);
        Assert.Equal(13900m, Samples.Completed(doc).Total);
    }

    [Fact]
    public void Unknown_fields_are_rejected() =>
        Assert.ThrowsAny<JsonException>(() => DocumentJson.Parse(Chat.Replace("\"notes\"", "\"note\"")));

    [Fact]
    public void Round_trips()
    {
        var doc = Samples.Completed(DocumentJson.Parse(Chat));
        var again = DocumentJson.Parse(DocumentJson.Write(doc));
        Assert.Equal(doc.Total, again.Total);
        Assert.Equal(doc.Date, again.Date);
        Assert.Contains("\"pdf_import\"", DocumentJson.Write(doc with { Origin = DocumentOrigin.PdfImport }));
    }
}
