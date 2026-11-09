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

    /// <summary>The active sales accounts that have a code, for choosing where quotes and invoices go.</summary>
    public async Task<IReadOnlyList<XeroSalesAccount>> SalesAccountsAsync(CancellationToken cancel = default) =>
        ((await GetAsync("Accounts", cancel))["Accounts"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(a => XeroRules.IsSalesAccount(Str(a["Type"]), Str(a["Status"])) && Str(a["Code"]).Length > 0)
            .Select(a => new XeroSalesAccount(Str(a["Code"]), Str(a["Name"])))
            .ToList();

    /// <summary>The items in Xero's Products and Services, for linking quote and invoice lines to them.</summary>
    public async Task<IReadOnlyList<XeroItem>> ItemsAsync(CancellationToken cancel = default) =>
        ((await GetAsync("Items", cancel))["Items"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(i => new XeroItem(Str(i["Code"]), Str(i["Name"])))
            .Where(i => i.Code.Length > 0)
            .ToList();

    /// <summary>The organisation's branding themes, in Xero's own order.</summary>
    public async Task<IReadOnlyList<XeroBrandingTheme>> BrandingThemesAsync(CancellationToken cancel = default) =>
        ((await GetAsync("BrandingThemes", cancel))["BrandingThemes"] as JsonArray ?? []).OfType<JsonObject>()
            .OrderBy(t => t["SortOrder"]?.GetValue<int>() ?? 0)
            .Select(t => new XeroBrandingTheme(Str(t["BrandingThemeID"]), Str(t["Name"])))
            .Where(t => t.Id.Length > 0)
            .ToList();

    /// <summary>Whether the organisation has the "No VAT" tax rate drafts are written with.</summary>
    public async Task<bool> HasNoVatRateAsync(CancellationToken cancel = default) =>
        XeroRules.HasNoVatRate(((await GetAsync("TaxRates", cancel))["TaxRates"] as JsonArray ?? []).Select(t => Str(t?["TaxType"])));

    public async Task<IReadOnlyList<XeroContact>> ContactsAsync(CancellationToken cancel = default) =>
        (await AllAsync("Contacts", "&includeArchived=true", cancel)).Select(ToContact).ToList();

    /// <summary>Every contact with the details a client page has, archived ones included, for bringing customers in.</summary>
    public async Task<IReadOnlyList<Webignite.Core.Clients.XeroCustomer>> CustomersAsync(CancellationToken cancel = default) =>
        (await AllAsync("Contacts", "&includeArchived=true", cancel)).Select(ToCustomer).ToList();

    public static Webignite.Core.Clients.XeroCustomer ToCustomer(JsonObject c)
    {
        var person = string.Join(' ', new[] { Str(c["FirstName"]), Str(c["LastName"]) }.Where(p => p.Length > 0));
        return new Webignite.Core.Clients.XeroCustomer(
            Str(c["ContactID"]),
            Str(c["Name"]),
            Str(c["EmailAddress"]) is { Length: > 0 } email ? email : null,
            person.Length > 0 ? person : null,
            Phone(c["Phones"] as JsonArray),
            Address(c["Addresses"] as JsonArray),
            Str(c["Website"]) is { Length: > 0 } site ? site : null,
            Str(c["ContactStatus"]) == "ACTIVE",
            Flag(c["IsCustomer"]),
            Flag(c["IsSupplier"]));
    }

    private static bool Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static readonly string[] PhoneTypes = ["MOBILE", "DEFAULT", "DDI"];
    private static readonly string[] AddressTypes = ["STREET", "POBOX"];
    private static readonly string[] AddressParts = ["AddressLine1", "AddressLine2", "AddressLine3", "AddressLine4", "City", "Region", "PostalCode", "Country"];

    // The mobile first, then the main number: area code and number, with the country code when it isn't 27.
    private static string? Phone(JsonArray? phones)
    {
        if (phones is null)
            return null;
        foreach (var type in PhoneTypes)
        {
            if (phones.OfType<JsonObject>().FirstOrDefault(p => Str(p["PhoneType"]) == type && Str(p["PhoneNumber"]).Length > 0) is not { } phone)
                continue;
            var country = Str(phone["PhoneCountryCode"]).TrimStart('+', '0');
            var number = string.Join(' ', new[] { Str(phone["PhoneAreaCode"]), Str(phone["PhoneNumber"]) }.Where(p => p.Length > 0));
            return country.Length > 0 && country != "27" ? "+" + country + " " + number : number;
        }
        return null;
    }

    // The street address, else the postal one, on one line.
    private static string? Address(JsonArray? addresses)
    {
        if (addresses is null)
            return null;
        foreach (var type in AddressTypes)
        {
            if (addresses.OfType<JsonObject>().FirstOrDefault(a => Str(a["AddressType"]) == type) is not { } address)
                continue;
            var parts = AddressParts.Select(k => Str(address[k])).Where(p => p.Length > 0).ToList();
            if (parts.Count > 0)
                return string.Join(", ", parts);
        }
        return null;
    }

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
