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
    private static readonly DateTime Occurred = new(2034, 9, 10, 15, 45, 0, DateTimeKind.Unspecified);

    private static int _sequence;

    private ExpensesApi _api = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await postgres.Migrate();
        _api = new ExpensesApi(postgres.ConnectionString);
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
        var command = NewPurchase(9.90m, [Line("Coffee", 9.90m)]);

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
        var body = await response.Content.ReadAsStringAsync();

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
    public async Task Multipart_upload_and_image_download()
    {
        var purchaseId = await RecordedId(5.55m);

        var uploaded = await Upload(purchaseId, Jpeg(0x91));
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var image = await ExpensesApi.Read<JsonElement>(uploaded);
        Assert.Equal("Pending", image.GetProperty("state").GetString());

        var download = await _client.GetAsync($"/purchases/{purchaseId}/receipt/content");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Jpeg(0x91), await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Fiscal_identifiers_are_readable_before_extraction_has_run()
    {
        var purchaseId = await RecordedId(6.66m);

        var uploaded = await Upload(purchaseId, Jpeg(0x92), ikof: "A1B2C3", jikr: "9F8E7D");
        

        var response = await _client.GetAsync($"/purchases/{purchaseId}/extraction");
        var extraction = await ExpensesApi.Read<JsonElement>(response);
        var storedImage = extraction.GetProperty("receipt");

        Assert.Equal("A1B2C3", storedImage.GetProperty("fiscalIkofSupplied").GetString());
        Assert.Equal("9F8E7D", storedImage.GetProperty("fiscalJikrSupplied").GetString());
        Assert.Equal("Unverified", storedImage.GetProperty("corroboration").GetString());
        Assert.Equal("Pending", storedImage.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Per_stage_provenance_and_each_arithmetic_check_are_reported()
    {
        var purchaseId = await RecordedId(7.77m);
        var uploaded = await Upload(purchaseId, Jpeg(0x93));
        

        var reran = await _client.PostAsync($"/purchases/{purchaseId}/extraction/rerun", null);
        Assert.Equal(HttpStatusCode.Accepted, reran.StatusCode);

        await RunExtraction(purchaseId);

        var extraction = await ExpensesApi.Read<JsonElement>(
            await _client.GetAsync($"/purchases/{purchaseId}/extraction"));

        var result = extraction.GetProperty("result");
        Assert.Equal("placeholder", result.GetProperty("engineName").GetString());
        Assert.Contains(
            "vision-cheap",
            result.GetProperty("stagesRun").EnumerateArray().Select(stage => stage.GetString()));
        Assert.Equal("vision-cheap", result.GetProperty("provenance").GetProperty("total").GetString());

        var checks = extraction.GetProperty("validation").GetProperty("checks").EnumerateArray().ToList();
        Assert.Contains(checks, check => check.GetProperty("name").GetString() == "line_sum");
        Assert.All(checks, check => Assert.False(string.IsNullOrWhiteSpace(
            check.GetProperty("outcome").GetString())));
    }

    [Fact]
    public async Task Reference_data_is_listed_and_a_category_is_managed()
    {
        var units = await ExpensesApi.Read<JsonElement>(await _client.GetAsync("/units"));
        Assert.Contains(units.EnumerateArray(), unit => unit.GetProperty("code").GetString() == "KG");

        var code = $"HTTP_TEST_{Interlocked.Increment(ref _sequence)}";
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
        var purchaseId = await RecordedId(10.00m);
        var uploaded = await Upload(purchaseId, Jpeg(0x94));
        
        await RunExtraction(purchaseId);

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
        var purchaseId = await RecordedId(11.11m);
        await Upload(purchaseId, Jpeg(0x95));

        var deleted = await _client.DeleteAsync($"/purchases/{purchaseId}/receipt");

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var purchase = await ExpensesApi.Read<JsonElement>(deleted);
        Assert.False(purchase.GetProperty("hasReceipt").GetBoolean());

        // The bytes are gone with the reference; the purchase and its expenses are not (D11).
        var download = await _client.GetAsync($"/purchases/{purchaseId}/receipt/content");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Single(purchase.GetProperty("expenses").EnumerateArray());
    }

    /// <summary>Runs the cascade the way the drain would, since the suite disables the drain.</summary>
    private async Task RunExtraction(long purchaseId) =>
        await _api.Resolve<Application.Receipts.RunExtraction>().Execute(purchaseId);

    private async Task<HttpResponseMessage> Record(object command) =>
        await _client.PostAsJsonAsync("/purchases", command, ExpensesApi.Json);

    private async Task<long> RecordedId(decimal amount)
    {
        var response = await Record(NewPurchase(amount, [Line("Line", amount)]));

        return (await ExpensesApi.Read<JsonElement>(response)).GetProperty("id").GetInt64();
    }

    private async Task<HttpResponseMessage> Upload(long purchaseId, byte[] content, string? ikof = null, string? jikr = null)
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

        return await _client.PostAsync($"/purchases/{purchaseId}/receipt", form);
    }

    private static object NewPurchase(decimal amount, object[] expenses, DateTime? occurredAt = null) =>
        new { occurredAt = occurredAt ?? Next(), amount, expenses };

    private static object Line(string description, decimal amount) => new { description, amount };

    private static DateTime Next() => Occurred.AddMinutes(Interlocked.Increment(ref _sequence));

    private static byte[] Jpeg(byte seed) =>
        [0xFF, 0xD8, 0xFF, 0xE0, seed, 0x4A, 0x46, 0x49, 0x46, 0x00, seed, 0x03];
}
