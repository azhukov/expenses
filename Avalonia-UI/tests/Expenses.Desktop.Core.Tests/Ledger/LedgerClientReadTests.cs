using System.Net;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Tests.Fakes;

namespace Expenses.Desktop.Core.Tests.Ledger;

/// <summary>
/// Scenarios from desktop-client: "A configured address is used", "The ledger reports a specific
/// error", "The ledger cannot be reached". The JSON is written out as the API sends it, not
/// serialised from the client's own records, so a mismatch in naming or enum handling fails here.
/// </summary>
public sealed class LedgerClientReadTests
{
    private readonly FakeHttpMessageHandler _api = new();

    [Fact]
    public async Task Purchases_are_read_for_a_date_range_from_the_API_shape()
    {
        _api.Respond("GET", "/purchases", HttpStatusCode.OK, """
            [{
              "id": 7,
              "occurredAt": "2026-09-12T21:30:00",
              "amount": 12.40,
              "merchantId": null,
              "merchantRaw": "IDEA 042",
              "hasReceipt": true,
              "expenses": [{
                "id": 70, "description": "Milk", "quantity": 2, "amount": 2.40, "unitId": null,
                "unitPrice": 1.20, "categoryId": 3, "categoryRaw": null, "unitRaw": null,
                "listUnitPrice": null, "discountAmount": null, "discountPercentage": null
              }],
              "totalSaving": 0,
              "savingPercentage": null,
              "extractionState": "NeedsReview"
            }]
            """);

        var purchases = await Subject().ListPurchases(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), Token);

        var request = Assert.Single(_api.Requests);
        Assert.Equal("?from=2026-09-01&to=2026-09-30", request.Uri.Query);

        var purchase = Assert.Single(purchases);
        Assert.Equal(new DateTime(2026, 9, 12, 21, 30, 0), purchase.OccurredAt);
        Assert.Equal(DateTimeKind.Unspecified, purchase.OccurredAt.Kind);
        Assert.Equal(12.40m, purchase.Amount);
        Assert.Equal("IDEA 042", purchase.MerchantRaw);
        Assert.Equal(ExtractionState.NeedsReview, purchase.ExtractionState);
        Assert.Equal("Milk", Assert.Single(purchase.Expenses).Description);
    }

    [Fact]
    public async Task Merchants_categories_and_units_are_read_from_their_dictionaries()
    {
        _api.Respond("GET", "/merchants", HttpStatusCode.OK, """
            [{ "id": 1, "name": "Idea", "taxId": "02005328", "parentId": null, "parentName": null, "isActive": true }]
            """)
            .Respond("GET", "/categories", HttpStatusCode.OK, """
            [{ "id": 3, "code": "food", "name": "Food", "parentId": null, "parentCode": null, "isSystem": true, "isActive": true }]
            """)
            .Respond("GET", "/units", HttpStatusCode.OK, """
            [{ "id": 2, "code": "kg", "name": "Kilogram", "symbol": "kg", "kind": "Mass", "isActive": true }]
            """);

        var subject = Subject();

        Assert.Equal("Idea", Assert.Single(await subject.ListMerchants(Token)).Name);
        Assert.Equal("food", Assert.Single(await subject.ListCategories(Token)).Code);
        Assert.Equal(UnitKind.Mass, Assert.Single(await subject.ListUnits(Token)).Kind);
    }

    [Fact]
    public async Task A_configured_address_is_used()
    {
        _api.Respond("GET", "/api/merchants", HttpStatusCode.OK, "[]");

        await new LedgerClient(FakeHttpMessageHandler.ClientFor(_api, "http://elsewhere.test:9000/api/")).ListMerchants(Token);

        var request = Assert.Single(_api.Requests);
        Assert.Equal("http://elsewhere.test:9000/api/merchants", request.Uri.ToString());
    }

    [Fact]
    public async Task The_ledger_reports_a_specific_error()
    {
        _api.Respond("GET", "/purchases", HttpStatusCode.BadRequest, """
            { "code": "range.invalid", "message": "The end of the range is before its start.", "fields": {}, "correlationId": "abc" }
            """);

        var failure = await Assert.ThrowsAsync<LedgerException>(
            () => Subject().ListPurchases(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1), Token));

        Assert.Equal("The end of the range is before its start.", failure.Message);
        Assert.Equal("range.invalid", failure.Code);
        Assert.Equal(400, failure.Status);
        Assert.Equal("abc", failure.CorrelationId);
    }

    [Fact]
    public async Task A_failure_without_the_error_shape_is_still_a_ledger_failure()
    {
        _api.Respond("GET", "/merchants", HttpStatusCode.BadGateway, "<html>bad gateway</html>");

        var failure = await Assert.ThrowsAsync<LedgerException>(() => Subject().ListMerchants(Token));

        Assert.Equal("The ledger could not be read.", failure.Message);
        Assert.Null(failure.Code);
        Assert.Equal(502, failure.Status);
    }

    [Fact]
    public async Task The_ledger_cannot_be_reached()
    {
        _api.Unreachable("GET", "/categories");

        var failure = await Assert.ThrowsAsync<LedgerException>(() => Subject().ListCategories(Token));

        Assert.Equal("The ledger could not be reached.", failure.Message);
        Assert.Null(failure.Code);
        Assert.Null(failure.Status);
    }

    [Fact]
    public async Task Cancelling_a_read_is_not_reported_as_a_failure()
    {
        // The fake would answer normally: an OperationCanceledException can only come from the
        // client honouring the token, not from the transport failing.
        _api.Respond("GET", "/units", HttpStatusCode.OK, "[]");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Subject().ListUnits(cancellation.Token));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private LedgerClient Subject()
    {
        return new LedgerClient(FakeHttpMessageHandler.ClientFor(_api));
    }
}
