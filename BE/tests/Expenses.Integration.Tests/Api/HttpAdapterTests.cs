using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Expenses.Integration.Tests.Harness;

namespace Expenses.Integration.Tests.Api;

/// <summary>
/// Scenarios from api-surface: "HTTP interface for the browser client", "Receipt upload is
/// HTTP-only", "Errors carry enough detail to be acted on", "Merchant and discount are carried by
/// both interfaces", "Extraction results are legible over both interfaces", "Fiscal identifiers may
/// accompany an upload".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HttpAdapterTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTime s_occurred = new(2034, 9, 10, 15, 45, 0, DateTimeKind.Unspecified);

    private static readonly string[] s_terminalStates = ["Extracted", "NeedsReview", "Failed"];

    private static int s_sequence;

    private ExpensesApi _api = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await postgres.Migrate();

        // The placeholder path being reached is what these scenarios are about, not the real vision
        // engine's own behaviour (D33).
        _api = new ExpensesApi(postgres.ConnectionString, ("Extraction:Vision:Engine", "placeholder"));
        _client = _api.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Successful_creation()
    {
        var response = await Record(NewPurchase(12.40m, [Line("Lunch", 12.40m)]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var purchase = await ExpensesApi.Read<JsonElement>(response);
        Assert.True(purchase.GetProperty("id").GetInt64() > 0);
        Assert.Equal(12.40m, purchase.GetProperty("amount").GetDecimal());
        Assert.False(purchase.GetProperty("alreadyRecorded").GetBoolean());
    }

    [Fact]
    public async Task Duplicate_submission()
    {
        object command = NewPurchase(9.90m, [Line("Coffee", 9.90m)]);

        var first = await Record(command);
        var second = await Record(command);

        // Success either way, told apart by the status code rather than by an error (D3).
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var created = await ExpensesApi.Read<JsonElement>(first);
        var existing = await ExpensesApi.Read<JsonElement>(second);
        Assert.Equal(created.GetProperty("id").GetInt64(), existing.GetProperty("id").GetInt64());
        Assert.True(existing.GetProperty("alreadyRecorded").GetBoolean());
    }

    [Fact]
    public async Task Validation_failure_shape()
    {
        var response = await Record(NewPurchase(80.00m, [Line("Shoes", 60.00m), Line("Socks", 18.50m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ExpensesApi.Read<JsonElement>(response);

        Assert.Equal("purchase.reconciliation_mismatch", error.GetProperty("code").GetString());
        Assert.Contains("78.50", error.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(80.00m, error.GetProperty("fields").GetProperty("amount").GetDecimal());
        Assert.Equal(78.50m, error.GetProperty("fields").GetProperty("expensesTotal").GetDecimal());
    }

    [Fact]
    public async Task Unknown_resource()
    {
        var response = await _client.GetAsync("/purchases/987654321");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await ExpensesApi.Read<JsonElement>(response);

        // The same shape as every other failure across the interface.
        Assert.Equal("purchase.not_found", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Internal_detail_is_not_leaked()
    {
        var response = await _client.GetAsync("/diagnostics/failure");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var error = await ExpensesApi.Read<JsonElement>(response);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal("internal_error", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("correlationId").GetString()));
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_by_date_range()
    {
        // A pair of days of its own: every other test in the class writes to the base day, and the
        // range is what is under test here.
        var occurred = Next().AddDays(40);
        await Record(NewPurchase(3.00m, [Line("One", 3.00m)], occurred));
        await Record(NewPurchase(4.00m, [Line("Two", 4.00m)], occurred.AddDays(1)));

        var day = DateOnly.FromDateTime(occurred);
        var response = await _client.GetAsync($"/purchases?from={day:O}&to={day.AddDays(1):O}&take=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = await ExpensesApi.Read<JsonElement>(response);
        var amounts = listed.EnumerateArray().Select(purchase => purchase.GetProperty("amount").GetDecimal());
        Assert.Equal([4.00m, 3.00m], amounts);
    }

    [Fact]
    public async Task Merchant_and_discount_round_trip_with_the_derived_saving()
    {
        var created = await ExpensesApi.Read<JsonElement>(await Record(new
        {
            occurredAt = Next(),
            amount = 8.48m,
            merchant = new { text = "AROMA", taxId = "08800011" },
            expenses = new[]
            {
                new { description = "Sladoled", amount = 4.49m, listUnitPrice = 8.50m, discountAmount = 4.01m },
                new { description = "Cokolada", amount = 3.99m, listUnitPrice = 7.50m, discountAmount = 3.51m },
            },
        }));

        var response = await _client.GetAsync($"/purchases/{created.GetProperty("id").GetInt64()}");
        var purchase = await ExpensesApi.Read<JsonElement>(response);

        Assert.Equal("AROMA", purchase.GetProperty("merchantRaw").GetString());
        Assert.True(purchase.GetProperty("merchantId").GetInt64() > 0);
        Assert.Equal(7.52m, purchase.GetProperty("totalSaving").GetDecimal());

        var line = purchase.GetProperty("expenses")[0];
        Assert.Equal(8.50m, line.GetProperty("listUnitPrice").GetDecimal());
        Assert.Equal(4.01m, line.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(47.18m, line.GetProperty("discountPercentage").GetDecimal());
    }

    [Fact]
    public async Task A_submitted_discount_percentage_is_rejected()
    {
        var response = await Record(new
        {
            occurredAt = Next(),
            amount = 4.49m,
            expenses = new[]
            {
                new { description = "Sladoled", amount = 4.49m, listUnitPrice = 8.50m, discountPercentage = 47.18m },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ExpensesApi.Read<JsonElement>(response);

        // A percentage is a rounded rendering of two numbers already stored; accepting one would
        // create a second, lossy source of truth for the same fact (D19).
        Assert.Equal("expense.discount_percentage_not_accepted", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Capture_over_http()
    {
        var captured = await Capture(Jpeg(0x91));

        Assert.False(string.IsNullOrWhiteSpace(captured.GetProperty("tempKey").GetString()));
        Assert.Contains(
            captured.GetProperty("state").GetString(),
            s_terminalStates);
    }

    [Fact]
    public async Task Capture_confirm_and_image_download()
    {
        long purchaseId = await RecordedIdWithReceipt(5.55m, Jpeg(0x91));

        var download = await _client.GetAsync($"/purchases/{purchaseId}/receipt/content");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Jpeg(0x91), await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Fiscal_identifiers_are_readable_in_the_capture_response_before_confirmation()
    {
        var response = await CaptureRaw(
            Jpeg(0x92),
            fiscalQr: "https://mapr.tax.gov.me/ic/#/verify?iic=A1B2C3&tin=02365928");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var captured = await ExpensesApi.Read<JsonElement>(response);

        Assert.Equal("A1B2C3", captured.GetProperty("supplied").GetProperty("ikof").GetString());
        Assert.Equal("02365928", captured.GetProperty("supplied").GetProperty("issuerTaxNumber").GetString());
    }

    /// <summary>Scenario from api-surface: "Capture carrying a fiscal QR payload".</summary>
    [Fact]
    public async Task Capture_carrying_a_fiscal_qr_payload()
    {
        var response = await CaptureRaw(
            Jpeg(0x94),
            fiscalQr: "https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636"
                + "&tin=02365928&crtd=2026-08-29T14:59:22+02:00&prc=59.65");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var captured = await ExpensesApi.Read<JsonElement>(response);

        // Parsed server-side, so the client hands over what it read rather than what it understood
        // of it (D30). The tax number and total come free with the payload and could not be sent
        // at all under the field-by-field contract this replaces.
        var supplied = captured.GetProperty("supplied");
        Assert.Equal("32AA324CFF5030271E16D59F7F8EF636", supplied.GetProperty("ikof").GetString());
        Assert.Equal("02365928", supplied.GetProperty("issuerTaxNumber").GetString());
        Assert.Equal("2026-08-29T14:59:22+02:00", supplied.GetProperty("createdAt").GetString());
        Assert.Equal(59.65m, supplied.GetProperty("total").GetDecimal());
    }

    /// <summary>Scenario from api-surface: "Capture carrying an unparseable payload".</summary>
    [Fact]
    public async Task Capture_carrying_an_unparseable_payload()
    {
        var response = await CaptureRaw(Jpeg(0x95), fiscalQr: "*not a verification address*");

        // An ordinary capture, not a bad request: the payload is untrusted input and a shape this
        // parser does not recognise says nothing about whether the image is a receipt (D30).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Scenario from api-surface: "Capture carrying an oversized payload".</summary>
    [Fact]
    public async Task Capture_carrying_an_oversized_payload()
    {
        var response = await CaptureRaw(Jpeg(0x96), fiscalQr: new string('x', 4096));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Per_stage_provenance_and_each_arithmetic_check_are_reported()
    {
        var captured = await Capture(Jpeg(0x93));

        var result = captured.GetProperty("result");
        Assert.Equal("placeholder", result.GetProperty("engineName").GetString());
        Assert.Contains(
            "vision",
            result.GetProperty("stepsRun").EnumerateArray().Select(stage => stage.GetString()));
        Assert.Equal("vision", result.GetProperty("provenance").GetProperty("total").GetString());

        var checks = captured.GetProperty("validation").GetProperty("checks").EnumerateArray().ToList();
        Assert.Contains(checks, check => check.GetProperty("name").GetString() == "line_sum");
        Assert.All(checks, check => Assert.False(string.IsNullOrWhiteSpace(
            check.GetProperty("outcome").GetString())));
    }

    [Fact]
    public async Task Rerun_extraction_returns_the_full_result_synchronously()
    {
        long purchaseId = await RecordedIdWithReceipt(7.77m, Jpeg(0x96));

        var reran = await ExpensesApi.Read<JsonElement>(
            await _client.PostAsync($"/purchases/{purchaseId}/extraction/rerun", null));

        Assert.Contains(
            reran.GetProperty("receipt").GetProperty("state").GetString(),
            s_terminalStates);
        Assert.NotEmpty(reran.GetProperty("result").GetProperty("candidates").EnumerateArray());
    }

    [Fact]
    public async Task Reference_data_is_listed_and_a_category_is_managed()
    {
        var units = await ExpensesApi.Read<JsonElement>(await _client.GetAsync("/units"));
        Assert.Contains(units.EnumerateArray(), unit => unit.GetProperty("code").GetString() == "KG");

        string code = $"HTTP_TEST_{Interlocked.Increment(ref s_sequence)}";
        var created = await _client.PostAsJsonAsync("/categories", new { code, name = "Created over HTTP" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var renamed = await _client.PutAsJsonAsync($"/categories/{code}", new { name = "Renamed over HTTP" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(
            "Renamed over HTTP",
            (await ExpensesApi.Read<JsonElement>(renamed)).GetProperty("name").GetString());

        var codeChange = await _client.PutAsJsonAsync($"/categories/{code}", new { code = "SOMETHING_ELSE", name = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, codeChange.StatusCode);
        Assert.Equal(
            "category.code_immutable",
            (await ExpensesApi.Read<JsonElement>(codeChange)).GetProperty("code").GetString());

        var deactivated = await _client.PostAsync($"/categories/{code}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.False((await ExpensesApi.Read<JsonElement>(deactivated)).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Merchants_are_listed_and_searched()
    {
        await Record(new
        {
            occurredAt = Next(),
            amount = 2.50m,
            merchant = new { text = "Kafiterija Šćepanović", taxId = "08800022" },
            expenses = new[] { new { description = "Kafa", amount = 2.50m } },
        });

        var listed = await ExpensesApi.Read<JsonElement>(await _client.GetAsync("/merchants"));
        Assert.Contains(listed.EnumerateArray(), merchant => merchant.GetProperty("taxId").GetString() == "08800022");

        var found = await ExpensesApi.Read<JsonElement>(await _client.GetAsync("/merchants/search?term=Scepanovic"));
        Assert.Contains(
            found.EnumerateArray(),
            match => match.GetProperty("matchedText").GetString()!.Contains("Kafiterija", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Candidates_are_confirmed_over_http()
    {
        long purchaseId = await RecordedIdWithReceipt(10.00m, Jpeg(0x94));

        // Re-running produces fresh candidates for the receipt already attached to the purchase.
        await _client.PostAsync($"/purchases/{purchaseId}/extraction/rerun", null);

        // The placeholder's lines are its own; confirming the purchase's real amount is what a user
        // does after correcting them.
        var confirmed = await _client.PostAsJsonAsync(
            $"/purchases/{purchaseId}/extraction/confirm",
            new { expenses = new[] { new { description = "Corrected line", amount = 10.00m } } });

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var purchase = await ExpensesApi.Read<JsonElement>(confirmed);
        Assert.Equal("Corrected line", purchase.GetProperty("expenses")[0].GetProperty("description").GetString());

        var discarded = await _client.PostAsync($"/purchases/{purchaseId}/extraction/discard", null);
        Assert.Equal(HttpStatusCode.NoContent, discarded.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_receipt_over_http_leaves_the_purchase()
    {
        long purchaseId = await RecordedIdWithReceipt(11.11m, Jpeg(0x95));

        var deleted = await _client.DeleteAsync($"/purchases/{purchaseId}/receipt");

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var purchase = await ExpensesApi.Read<JsonElement>(deleted);
        Assert.False(purchase.GetProperty("hasReceipt").GetBoolean());

        // The bytes are gone with the reference; the purchase and its expenses are not (D11).
        var download = await _client.GetAsync($"/purchases/{purchaseId}/receipt/content");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Single(purchase.GetProperty("expenses").EnumerateArray());
    }

    private async Task<HttpResponseMessage> Record(object command)
        => await _client.PostAsJsonAsync("/purchases", command, ExpensesApi.Json);

    /// <summary>Captures an image, then confirms it into a new purchase of the given amount.</summary>
    private async Task<long> RecordedIdWithReceipt(decimal amount, byte[] content)
    {
        var captured = await Capture(content);
        string? tempKey = captured.GetProperty("tempKey").GetString();
        string? state = captured.GetProperty("state").GetString();

        var response = await Record(new
        {
            occurredAt = Next(),
            amount,
            expenses = new[] { Line("Line", amount) },
            capture = new { tempKey, state },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await ExpensesApi.Read<JsonElement>(response)).GetProperty("id").GetInt64();
    }

    private async Task<JsonElement> Capture(byte[] content, string? ikof = null, string? jikr = null)
    {
        var response = await CaptureRaw(content, ikof, jikr);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ExpensesApi.Read<JsonElement>(response);
    }

    private async Task<HttpResponseMessage> CaptureRaw(
        byte[] content,
        string? ikof = null,
        string? jikr = null,
        string? fiscalQr = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "receipt.jpg");

        if (ikof is not null)
        {
            form.Add(new StringContent(ikof), "fiscalIkof");
        }

        if (jikr is not null)
        {
            form.Add(new StringContent(jikr), "fiscalJikr");
        }

        if (fiscalQr is not null)
        {
            form.Add(new StringContent(fiscalQr), "fiscalQr");
        }

        return await _client.PostAsync("/receipts/capture", form);
    }

    private static object NewPurchase(decimal amount, object[] expenses, DateTime? occurredAt = null)
        => new { occurredAt = occurredAt ?? Next(), amount, expenses };

    private static object Line(string description, decimal amount) => new { description, amount };

    private static DateTime Next() => s_occurred.AddMinutes(Interlocked.Increment(ref s_sequence));

    private static byte[] Jpeg(byte seed)
        => [0xFF, 0xD8, 0xFF, 0xE0, seed, 0x4A, 0x46, 0x49, 0x46, 0x00, seed, 0x03];
}
