using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Webignite.Core.Xero;
using Webignite.Xero.Auth;

namespace Webignite.Xero;

/// <summary>A Xero problem worded for a person.</summary>
public class XeroException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The connection broke during a write: it may or may not have reached Xero. Never retried.</summary>
public sealed class XeroUncertainException(string message, Exception? inner = null) : XeroException(message, inner);

/// <summary>
/// The parts of Xero's accounting API the app uses. Reads may be repeated; writes are made once,
/// with an idempotency key, and a broken write is reported as uncertain instead of tried again.
/// </summary>
public sealed class XeroApi(XeroSession session, HttpClient http, string tenantId)
{
    private static readonly Uri Base = new("https://api.xero.com/api.xro/2.0/");

    public async Task<JsonObject> GetAsync(string path, CancellationToken cancel = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = await Request(HttpMethod.Get, path, cancel);
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancel);
            }
            catch (HttpRequestException e)
            {
                throw new XeroException("Couldn't reach Xero. Check the internet connection and try again.", e);
            }
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 2)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                    await Task.Delay(wait > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : wait, cancel);
                    continue;
                }
                await Check(response, cancel);
                return await Json(response, cancel);
            }
        }
    }

    /// <summary>Creates records. One attempt only.</summary>
    public Task<JsonObject> PutAsync(string path, JsonObject body, string idempotencyKey, CancellationToken cancel = default) =>
        WriteAsync(HttpMethod.Put, path, body, idempotencyKey, cancel);

    /// <summary>Changes a record, e.g. approves an invoice. One attempt only.</summary>
    public Task<JsonObject> PostAsync(string path, JsonObject body, string idempotencyKey, CancellationToken cancel = default) =>
        WriteAsync(HttpMethod.Post, path, body, idempotencyKey, cancel);

    private async Task<JsonObject> WriteAsync(HttpMethod method, string path, JsonObject body, string idempotencyKey, CancellationToken cancel)
    {
        using var request = await Request(method, path, cancel);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(body);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancel);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new XeroUncertainException("The connection to Xero broke while saving, so it may or may not be in Xero. Use Check Xero before doing anything else.", e);
        }
        using (response)
        {
            if ((int)response.StatusCode >= 500)
                throw new XeroUncertainException($"Xero had a problem while saving ({(int)response.StatusCode}), so it may or may not be in Xero. Use Check Xero before doing anything else.");
            await Check(response, cancel);
            JsonObject json;
            try
            {
                json = await Json(response, cancel);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new XeroUncertainException("Xero's reply couldn't be read, so it may or may not be in Xero. Use Check Xero.", e);
            }
            return json;
        }
    }

    public async Task<byte[]> PdfAsync(string path, CancellationToken cancel = default)
    {
        using var request = await Request(HttpMethod.Get, path, cancel);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));
        try
        {
            using var response = await http.SendAsync(request, cancel);
            await Check(response, cancel);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancel);
            return bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' ? bytes : throw new XeroException("Xero didn't send a PDF.");
        }
        catch (HttpRequestException e)
        {
            throw new XeroException("Couldn't download the PDF from Xero. Try again.", e);
        }
    }

    /// <summary>Every page of a list, e.g. Contacts.</summary>
    public async Task<IReadOnlyList<JsonObject>> AllAsync(string resource, string query = "", CancellationToken cancel = default)
    {
        var rows = new List<JsonObject>();
        for (var page = 1; page <= 200; page++)
        {
            var json = await GetAsync($"{resource}?page={page}{query}", cancel);
            var list = json[resource] as JsonArray ?? [];
            rows.AddRange(list.OfType<JsonObject>());
            if (list.Count < 100)
                return rows;
        }
        throw new XeroException($"Xero has too many {resource.ToLowerInvariant()} to read in one go.");
    }

    public async Task<IReadOnlyList<XeroContact>> ContactsAsync(CancellationToken cancel = default) =>
        (await AllAsync("Contacts", "&includeArchived=true", cancel)).Select(ToContact).ToList();

    public static XeroContact ToContact(JsonObject c) =>
        new(Str(c["ContactID"]), Str(c["Name"]), Str(c["EmailAddress"]) is { Length: > 0 } e ? e : null, Str(c["ContactStatus"]) == "ACTIVE");

    /// <summary>A contact's quotes or invoices, for duplicate checks and finding a draft after a broken write.</summary>
    public async Task<IReadOnlyList<XeroExisting>> ExistingAsync(bool quotes, string contactId, CancellationToken cancel = default)
    {
        var rows = quotes
            ? await AllAsync("Quotes", "&ContactID=" + contactId, cancel)
            : await AllAsync("Invoices", "&ContactIDs=" + contactId + "&where=" + Uri.EscapeDataString("Type==\"ACCREC\""), cancel);
        return rows.Select(r => new XeroExisting(
            Str(r[quotes ? "QuoteID" : "InvoiceID"]),
            Str(r[quotes ? "QuoteNumber" : "InvoiceNumber"]) is { Length: > 0 } n ? n : null,
            Str(r["Contact"]?["ContactID"]),
            XeroPayload.NormalizeDate(Str(r["DateString"]) is { Length: > 0 } d ? d : Str(r["Date"])) is { } iso ? DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            Str(r["CurrencyCode"]),
            r["Total"] is JsonValue v && v.TryGetValue<decimal>(out var total) ? total : null,
            Str(r["Status"]))).ToList();
    }

    private async Task<HttpRequestMessage> Request(HttpMethod method, string path, CancellationToken cancel)
    {
        var request = new HttpRequestMessage(method, new Uri(Base, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await session.AccessTokenAsync(cancel));
        request.Headers.Add("xero-tenant-id", tenantId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    // Raw Xero replies can hold customer details, so only the status code is shown, plus Xero's own validation messages.
    private static async Task Check(HttpResponseMessage response, CancellationToken cancel)
    {
        if (response.IsSuccessStatusCode)
            return;
        var code = (int)response.StatusCode;
        if (code is 401 or 403)
            throw new XeroException("Xero refused access. Connect Xero again in Settings.");
        if (code == 429)
            throw new XeroException("Xero says too many requests. Wait a minute and try again.");
        var messages = new List<string>();
        try
        {
            if (await response.Content.ReadFromJsonAsync<JsonObject>(cancel) is { } body)
                foreach (var element in body["Elements"] as JsonArray ?? [])
                    foreach (var error in element?["ValidationErrors"] as JsonArray ?? [])
                        if (Str(error?["Message"]) is { Length: > 0 } m)
                            messages.Add(m);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Not JSON; the status code is enough.
        }
        throw new XeroException(messages.Count > 0 ? "Xero said: " + string.Join(" ", messages.Distinct()) : $"Xero turned the request down ({code}).");
    }

    private static async Task<JsonObject> Json(HttpResponseMessage response, CancellationToken cancel) =>
        await response.Content.ReadFromJsonAsync<JsonObject>(cancel) ?? throw new XeroException("Xero sent an empty reply.");

    internal static string Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node?.ToString() ?? "";
}
