using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Expenses.Application.Abstractions;
using Expenses.Application.Merchants;
using Expenses.Application.Purchases;
using Expenses.Application.Receipts;
using Expenses.Application.ReferenceData;
using Expenses.Domain;
using Expenses.Integration.Tests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Integration.Tests.Ledger;

/// <summary>
/// Scenarios the suite audit (10.15) found without a test of their own: "Extraction state is
/// readable", "Non-Latin receipt text", "Manual purchase has no image", "Parent is assignable",
/// "Purchase references a chain directly", "User cannot create units", and "Declared type disagrees
/// with content".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CoverageGapTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTime Occurred = new(2040, 1, 2, 7, 45, 0, DateTimeKind.Unspecified);

    private static int _sequence;

    private ServiceProvider _services = null!;

    public Task InitializeAsync()
    {
        _services = postgres.Services();
        return postgres.Migrate();
    }

    public Task DisposeAsync() => _services.DisposeAsync().AsTask();

    [Fact]
    public async Task Extraction_state_is_readable_from_the_purchase()
    {
        using var scope = _services.CreateScope();
        var captured = await scope.ServiceProvider.GetRequiredService<CaptureReceipt>().Execute(Jpeg());

        var recorded = await scope.ServiceProvider.GetRequiredService<RecordPurchase>().Execute(
            new RecordPurchaseCommand(
                Next(),
                6.00m,
                [new ExpenseCommand("Line", 6.00m)],
                Capture: new CapturedReceiptCommand(captured.TempKey, captured.State, captured.FailureReason)));

        var purchase = await scope.ServiceProvider.GetRequiredService<GetPurchase>()
            .Execute(recorded.Purchase.Id);

        // Retrieving a purchase says where its receipt has got to, in the same read: the receipt is
        // columns on the purchase rather than a row to join (D11) — and already terminal, since
        // extraction ran synchronously at capture.
        Assert.True(purchase.HasReceipt);
        Assert.Equal(captured.State, purchase.ExtractionState);
    }

    [Fact]
    public async Task Manual_purchase_has_no_image()
    {
        using var scope = _services.CreateScope();
        var recorded = await Record(scope.ServiceProvider, 7.00m);

        var purchase = await scope.ServiceProvider.GetRequiredService<GetPurchase>()
            .Execute(recorded.Purchase.Id);

        Assert.False(purchase.HasReceipt);
        Assert.Null(purchase.ExtractionState);
    }

    [Fact]
    public async Task Non_latin_receipt_text_is_retained_character_for_character()
    {
        const string Cyrillic = "Хлеб пшеничный 500г";
        const string Greek = "Ελληνικός καφές";

        using var scope = _services.CreateScope();
        var recorded = await scope.ServiceProvider.GetRequiredService<RecordPurchase>().Execute(
            new RecordPurchaseCommand(
                Next(),
                3.00m,
                [
                    new ExpenseCommand(Cyrillic, 2.00m, UnitRaw: "шт", CategoryRaw: "продукты"),
                    new ExpenseCommand(Greek, 1.00m),
                ]));

        var purchase = await scope.ServiceProvider.GetRequiredService<GetPurchase>()
            .Execute(recorded.Purchase.Id);

        // Multilingual support is storage and collation, not translation: the text comes back
        // exactly as it went in (D8, D13).
        Assert.Equal(Cyrillic, purchase.Expenses[0].Description);
        Assert.Equal("шт", purchase.Expenses[0].UnitRaw);
        Assert.Equal("продукты", purchase.Expenses[0].CategoryRaw);
        Assert.Equal(Greek, purchase.Expenses[1].Description);
    }

    [Fact]
    public async Task A_category_with_children_is_assignable_in_its_own_right()
    {
        var parentCode = $"GAP_PARENT_{Interlocked.Increment(ref _sequence)}";
        var childCode = $"GAP_CHILD_{_sequence}";

        using var scope = _services.CreateScope();
        var create = scope.ServiceProvider.GetRequiredService<CreateCategory>();
        await create.Execute(new CreateCategoryCommand(parentCode, "Parent"));
        await create.Execute(new CreateCategoryCommand(childCode, "Child", parentCode));

        var recorded = await scope.ServiceProvider.GetRequiredService<RecordPurchase>().Execute(
            new RecordPurchaseCommand(
                Next(),
                2.00m,
                [new ExpenseCommand("Assigned to a parent", 2.00m, CategoryCode: parentCode)]));

        Assert.NotNull(recorded.Purchase.Expenses[0].CategoryId);
    }

    [Fact]
    public async Task A_purchase_references_a_chain_directly()
    {
        using var scope = _services.CreateScope();
        var chain = await scope.ServiceProvider.GetRequiredService<ResolveMerchant>()
            .Execute("GAP CHAIN", "09950001");
        var branch = await scope.ServiceProvider.GetRequiredService<ResolveMerchant>()
            .Execute("GAP CHAIN 034", "09950002");

        await scope.ServiceProvider.GetRequiredService<SetMerchantParent>()
            .Execute(branch.Merchant.Id, chain.Merchant.Id);

        var recorded = await scope.ServiceProvider.GetRequiredService<RecordPurchase>().Execute(
            new RecordPurchaseCommand(
                Next(),
                5.00m,
                [new ExpenseCommand("At the chain itself", 5.00m)],
                new MerchantCommand("GAP CHAIN", "09950001")));

        // A merchant with children is assignable in its own right, exactly like a category (D18).
        Assert.Equal(chain.Merchant.Id, recorded.Purchase.MerchantId);
        Assert.False(recorded.MerchantNewlyAdded);
    }

    [Fact]
    public async Task A_user_cannot_create_a_unit()
    {
        await using var api = new ExpensesApi(postgres.ConnectionString);
        using var client = api.CreateClient();

        var attempted = await client.PostAsJsonAsync("/units", new { code = "FURLONG", name = "Furlong" });

        // Units are fixed reference data: there is no way in over either interface, rather than a
        // way in that refuses (D15). The route exists for reading only, so posting to it is not
        // allowed rather than not found — either answer is the absence of a creation path.
        Assert.False(attempted.IsSuccessStatusCode);
        Assert.Contains(
            attempted.StatusCode,
            new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });

        await using var mcp = await ExpensesMcp.Start(postgres.ConnectionString);
        var tools = await mcp.Client.ListToolsAsync();

        Assert.DoesNotContain(tools, tool => tool.Name.Contains("create_unit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Declared_type_disagrees_with_content()
    {
        await using var api = new ExpensesApi(postgres.ConnectionString);
        using var client = api.CreateClient();

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("this is text pretending to be a photograph"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "receipt.jpg");

        var uploaded = await client.PostAsync("/receipts/capture", form);

        // The declared type and the file name are claims; the bytes are not. Nothing is stored, even
        // temporarily.
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, uploaded.StatusCode);
        Assert.Equal(
            "receipt_image.unsupported_format",
            (await ExpensesApi.Read<JsonElement>(uploaded)).GetProperty("code").GetString());
    }

    private static async Task<RecordPurchaseResult> Record(IServiceProvider services, decimal amount) =>
        await services.GetRequiredService<RecordPurchase>().Execute(
            new RecordPurchaseCommand(Next(), amount, [new ExpenseCommand("Line", amount)]));

    private static byte[] Jpeg() =>
        [0xFF, 0xD8, 0xFF, 0xE0, (byte)_sequence, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x06, (byte)(_sequence >> 8)];

    private static DateTime Next() => Occurred.AddMinutes(Interlocked.Increment(ref _sequence));
}
