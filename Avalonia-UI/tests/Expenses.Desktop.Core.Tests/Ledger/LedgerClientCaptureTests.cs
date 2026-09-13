using System.Net;
using System.Text;
using Expenses.Desktop.Core.Ledger;
using Expenses.Desktop.Core.Tests.Fakes;

namespace Expenses.Desktop.Core.Tests.Ledger;

/// <summary>
/// Scenarios from desktop-client: "Original bytes are preserved", "An unusual format is not
/// pre-judged", "The ledger's judgement of a file is shown".
/// </summary>
public sealed class LedgerClientCaptureTests
{
    private const string Extracted = """
        {
          "tempKey": "tmp-1",
          "state": "NeedsReview",
          "failureReason": null,
          "result": {
            "engineName": "cascade", "engineVersion": "1", "stepsRun": ["vision"],
            "candidates": [{
              "lineNumber": 1, "description": "Milk", "amount": 2.40, "quantity": 2, "unitPrice": 1.20,
              "listUnitPrice": null, "discountAmount": null, "taxRatePercent": 21, "categoryRaw": "DAIRY",
              "unitRaw": null, "categoryId": 3, "unitId": null,
              "provenance": { "description": "vision" }, "reportedConfidence": { "description": 0.42 }
            }],
            "total": 2.40, "taxRatePercent": null, "taxAmount": null, "merchantName": "Idea",
            "merchantTaxId": null, "provenance": {}, "reportedConfidence": {}
          },
          "validation": { "checks": [{ "name": "LinesSumToTotal", "outcome": "Failed", "description": "Lines do not add up.", "values": { "total": 2.40, "sum": null } }] },
          "supplied": { "ikof": null, "jikr": null, "issuerTaxNumber": null, "createdAt": null, "total": null },
          "extracted": { "ikof": "ABC", "jikr": null, "issuerTaxNumber": "02005328", "createdAt": "2026-09-12T18:05:11", "total": 2.40 },
          "fiscalSource": "DecodedFromCode",
          "fiscalPayload": "https://efi.example/verify#/verify?iic=ABC"
        }
        """;

    private readonly FakeHttpMessageHandler _api = new();

    [Fact]
    public async Task Original_bytes_are_preserved()
    {
        // Every byte value, including ones no text encoding would carry through unchanged.
        var photograph = Enumerable.Range(0, 256).Select(each => (byte)each).Concat(new byte[] { 0xFF, 0xD8, 0x00 }).ToArray();
        var parts = new List<(string? Name, string? FileName, byte[] Bytes)>();

        // Read inside the responder: the client disposes the request once it has been answered, and
        // disposing multipart content empties it.
        _api.Respond("POST", "/receipts/capture", async request =>
        {
            foreach (var part in Assert.IsType<MultipartFormDataContent>(request.Content))
            {
                var disposition = part.Headers.ContentDisposition;
                parts.Add((disposition?.Name?.Trim('"'), disposition?.FileName?.Trim('"'), await part.ReadAsByteArrayAsync()));
            }

            return Json(Extracted);
        });

        await Subject().CaptureReceipt("receipt.jpg", photograph, Token);

        var sent = Assert.Single(parts);
        Assert.Equal("file", sent.Name);
        Assert.Equal("receipt.jpg", sent.FileName);
        Assert.Equal(photograph, sent.Bytes);
    }

    [Fact]
    public async Task An_unusual_format_is_not_pre_judged()
    {
        var notAnImage = Encoding.UTF8.GetBytes("%PDF-1.7 not something the client can display");
        _api.Respond("POST", "/receipts/capture", HttpStatusCode.OK, Extracted);

        var result = await Subject().CaptureReceipt("receipt.heic", notAnImage, Token);

        Assert.Single(_api.Requests);
        Assert.Equal("tmp-1", result.TempKey);
    }

    [Fact]
    public async Task The_capture_result_is_read_in_full()
    {
        _api.Respond("POST", "/receipts/capture", HttpStatusCode.OK, Extracted);

        var result = await Subject().CaptureReceipt("receipt.jpg", [1, 2, 3], Token);

        Assert.Equal(ExtractionState.NeedsReview, result.State);
        var candidate = Assert.Single(result.Result!.Candidates);
        Assert.Equal(0.42m, candidate.ReportedConfidence!["description"]);
        Assert.Equal(CheckOutcome.Failed, Assert.Single(result.Validation!.Checks).Outcome);
        Assert.Equal(new DateTime(2026, 9, 12, 18, 5, 11), result.Extracted!.CreatedAt);
        Assert.Equal(FiscalSource.DecodedFromCode, result.FiscalSource);
        Assert.Equal("https://efi.example/verify#/verify?iic=ABC", result.FiscalPayload);
    }

    [Fact]
    public async Task The_ledgers_judgement_of_a_file_is_shown()
    {
        _api.Respond("POST", "/receipts/capture", HttpStatusCode.RequestEntityTooLarge, """
            { "code": "receipt.image.too_large", "message": "A receipt image may be at most 15 MB.", "fields": {}, "correlationId": null }
            """);

        var failure = await Assert.ThrowsAsync<LedgerException>(
            () => Subject().CaptureReceipt("huge.jpg", [1], Token));

        Assert.Equal("A receipt image may be at most 15 MB.", failure.Message);
        Assert.Equal("receipt.image.too_large", failure.Code);
    }

    [Fact]
    public async Task An_upload_that_cannot_reach_the_ledger()
    {
        _api.Unreachable("POST", "/receipts/capture");

        var failure = await Assert.ThrowsAsync<LedgerException>(
            () => Subject().CaptureReceipt("receipt.jpg", [1], Token));

        Assert.Equal("The ledger could not be reached.", failure.Message);
        Assert.Null(failure.Status);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private LedgerClient Subject()
    {
        return new LedgerClient(FakeHttpMessageHandler.ClientFor(_api));
    }
}
