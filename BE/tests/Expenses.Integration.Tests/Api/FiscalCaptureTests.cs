using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Expenses.Integration.Tests.Harness;

namespace Expenses.Integration.Tests.Api;

/// <summary>
/// A fiscal QR payload captured over HTTP on its own, confirmed without an image, and the duplicate
/// warning on both capture endpoints (D37, D38, D39). Scenarios from api-surface: "A fiscal QR payload
/// can be captured over HTTP without an image", "Receipt capture is HTTP-only" ("Fiscal capture over
/// HTTP"), "A capture response names an invoice already recorded", "Fiscal identity is reported apart
/// from the receipt image", "Confirming a capture is available over both interfaces"; and from
/// receipt-ingestion, "A fiscal QR payload can be captured with no image" ("A payload no identifier
/// can be read from": the service is not asked).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FiscalCaptureTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTime s_occurred = new(2036, 4, 5, 10, 0, 0, DateTimeKind.Unspecified);

    private static int s_sequence;

    private FiscalPortalStub _portal = null!;
    private ExpensesApi _api = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await postgres.Migrate();
        _portal = await FiscalPortalStub.Answering();

        // The placeholder behind the photograph path, so nothing here reaches a paid engine (D33).
        _api = new ExpensesApi(
            postgres.ConnectionString,
            ("Extraction:Portal:BaseAddress", _portal.BaseAddress),
            ("Extraction:Vision:Engine", "placeholder"));
        _client = _api.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
        await _portal.DisposeAsync();
    }

    [Fact]
    public async Task Capturing_a_payload()
    {
        var response = await CaptureFiscal(Payload(UniqueIkof()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var captured = await ExpensesApi.Read<JsonElement>(response);

        // The same shape as an image capture, read from the invoice the service holds, with no key
        // because nothing was stored.
        Assert.Equal("Extracted", captured.GetProperty("state").GetString());
        Assert.Equal(14, captured.GetProperty("result").GetProperty("candidates").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, captured.GetProperty("tempKey").ValueKind);
        Assert.Equal("RetrievedFromService", captured.GetProperty("fiscalSource").GetString());

        // The vision engine was never shown anything: there was nothing to show it.
        Assert.DoesNotContain(
            "vision",
            captured.GetProperty("result").GetProperty("stepsRun").EnumerateArray().Select(step => step.GetString()));
    }

    [Fact]
    public async Task An_oversized_payload()
    {
        var response = await CaptureFiscal(new string('x', 4096));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("receipt_image.fiscal_payload_too_long", await Code(response));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_missing_payload(string? payload)
    {
        var response = await CaptureFiscal(payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("receipt_image.fiscal_payload_required", await Code(response));
    }

    [Fact]
    public async Task An_unparseable_payload()
    {
        int asked = _portal.Requests.Count;

        var response = await CaptureFiscal("https://example.com/menu");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var captured = await ExpensesApi.Read<JsonElement>(response);
        Assert.Equal("Failed", captured.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, captured.GetProperty("supplied").GetProperty("ikof").ValueKind);

        // receipt-ingestion, "A payload no identifier can be read from": nobody to ask about.
        Assert.Equal(asked, _portal.Requests.Count);
    }

    [Fact]
    public async Task A_purchase_with_a_fiscal_identity_and_no_image()
    {
        string ikof = UniqueIkof();
        var captured = await ExpensesApi.Read<JsonElement>(await CaptureFiscal(Payload(ikof)));

        var confirmed = await Record(new
        {
            occurredAt = Next(),
            amount = 59.65m,
            expenses = new[] { new { description = "Groceries", amount = 59.65m, unitCode = "PCS" } },
            capture = new
            {
                state = captured.GetProperty("state").GetString(),
                jikr = captured.GetProperty("extracted").GetProperty("jikr").GetString(),
                fiscalSource = captured.GetProperty("fiscalSource").GetString(),
                fiscalPayload = captured.GetProperty("fiscalPayload").GetString(),
            },
        });

        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        long id = (await ExpensesApi.Read<JsonElement>(confirmed)).GetProperty("id").GetInt64();

        var purchase = await _client.GetFromJsonAsync<JsonElement>($"/purchases/{id}", ExpensesApi.Json);
        Assert.False(purchase.GetProperty("hasReceiptImage").GetBoolean());
        Assert.Equal("Extracted", purchase.GetProperty("extractionState").GetString());

        var fiscal = purchase.GetProperty("fiscal");
        Assert.Equal(ikof, fiscal.GetProperty("ikofExtracted").GetString());
        Assert.Equal("d2857c6a-a363-4173-bf9c-dff37f77741a", fiscal.GetProperty("jikrExtracted").GetString());
        Assert.Equal(Payload(ikof), fiscal.GetProperty("payload").GetString());

        // api-surface, "Requesting the image of an image-less purchase".
        var image = await _client.GetAsync($"/purchases/{id}/receipt/content");
        Assert.Equal(HttpStatusCode.NotFound, image.StatusCode);

        // Its extraction is still readable, with no image beside it.
        var extraction = await _client.GetFromJsonAsync<JsonElement>($"/purchases/{id}/extraction", ExpensesApi.Json);
        Assert.Equal("Extracted", extraction.GetProperty("receipt").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, extraction.GetProperty("receipt").GetProperty("contentType").ValueKind);
    }

    [Fact]
    public async Task A_confirmation_naming_neither_a_key_nor_a_payload_is_rejected()
    {
        var response = await Record(new
        {
            occurredAt = Next(),
            amount = 1.00m,
            expenses = new[] { new { description = "Coffee", amount = 1.00m, unitCode = "PCS" } },
            capture = new { state = "Extracted" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("capture.identity_required", await Code(response));
    }

    [Fact]
    public async Task Reading_a_duplicate_warning_on_either_capture_endpoint()
    {
        string ikof = UniqueIkof();
        var first = await ExpensesApi.Read<JsonElement>(await CaptureFiscal(Payload(ikof)));
        Assert.Equal(JsonValueKind.Null, first.GetProperty("alreadyRecorded").ValueKind);

        var occurredAt = Next();
        var confirmed = await Record(new
        {
            occurredAt,
            amount = 59.65m,
            expenses = new[] { new { description = "Groceries", amount = 59.65m, unitCode = "PCS" } },
            capture = new
            {
                state = first.GetProperty("state").GetString(),
                fiscalSource = first.GetProperty("fiscalSource").GetString(),
                fiscalPayload = first.GetProperty("fiscalPayload").GetString(),
            },
        });
        long id = (await ExpensesApi.Read<JsonElement>(confirmed)).GetProperty("id").GetInt64();

        var again = await ExpensesApi.Read<JsonElement>(await CaptureFiscal(Payload(ikof)));
        Assert.Equal(id, again.GetProperty("alreadyRecorded").GetProperty("purchaseId").GetInt64());
        Assert.Equal(occurredAt, again.GetProperty("alreadyRecorded").GetProperty("occurredAt").GetDateTime());

        // The image endpoint names it too, from a payload that arrived beside a photograph.
        var photographed = await ExpensesApi.Read<JsonElement>(await CaptureImage(Payload(ikof)));
        Assert.Equal(id, photographed.GetProperty("alreadyRecorded").GetProperty("purchaseId").GetInt64());
    }

    private static string UniqueIkof() => $"FISCALCAPTURE{Interlocked.Increment(ref s_sequence):D19}";

    private static string Payload(string ikof)
        => $"https://mapr.tax.gov.me/ic/#/verify?iic={ikof}&tin=02365928&crtd=2026-08-29T14:59:22+02:00&prc=59.65";

    private static DateTime Next() => s_occurred.AddMinutes(Interlocked.Increment(ref s_sequence));

    private static async Task<string?> Code(HttpResponseMessage response)
        => (await ExpensesApi.Read<JsonElement>(response)).GetProperty("code").GetString();

    private Task<HttpResponseMessage> CaptureFiscal(string? payload)
        => _client.PostAsJsonAsync("/receipts/capture-fiscal", new { payload }, ExpensesApi.Json);

    private async Task<HttpResponseMessage> CaptureImage(string fiscalQr)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, (byte)s_sequence, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x07]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "receipt.jpg");
        form.Add(new StringContent(fiscalQr), "fiscalQr");

        return await _client.PostAsync("/receipts/capture", form);
    }

    private Task<HttpResponseMessage> Record(object command)
        => _client.PostAsJsonAsync("/purchases", command, ExpensesApi.Json);
}
