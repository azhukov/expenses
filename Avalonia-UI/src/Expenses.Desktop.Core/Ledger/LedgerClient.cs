using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Expenses.Desktop.Core.Ledger;

/// <summary>
/// The calls the screens make, over a typed <see cref="HttpClient"/> whose base address comes from
/// configuration (D4). Every failure surfaces as a <see cref="LedgerException"/>, with the ledger's
/// own message where it gave one, mirroring the browser client so that "no second error vocabulary"
/// means the same thing in both.
/// </summary>
public sealed class LedgerClient(HttpClient http)
{
    private const string Unreachable = "The ledger could not be reached.";
    private const string Unreadable = "The ledger could not be read.";

    /// <summary>camelCase and enums as names, which is what the API sends and expects.</summary>
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// As <see cref="s_json"/>, but a null member is left off the wire rather than sent as null, the
    /// way the browser client leaves it undefined: an unset date must reach the API as absent so that
    /// it takes the receipt's.
    /// </summary>
    private static readonly JsonSerializerOptions s_writeJson = new(s_json)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Purchases occurring in an inclusive range of dates.</summary>
    public Task<IReadOnlyList<PurchaseView>> ListPurchases(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var query = string.Create(CultureInfo.InvariantCulture, $"purchases?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

        return Read<IReadOnlyList<PurchaseView>>(query, cancellationToken);
    }

    /// <summary>The merchant dictionary.</summary>
    public Task<IReadOnlyList<MerchantView>> ListMerchants(CancellationToken cancellationToken = default)
    {
        return Read<IReadOnlyList<MerchantView>>("merchants", cancellationToken);
    }

    /// <summary>The category dictionary.</summary>
    public Task<IReadOnlyList<CategoryView>> ListCategories(CancellationToken cancellationToken = default)
    {
        return Read<IReadOnlyList<CategoryView>>("categories", cancellationToken);
    }

    /// <summary>The unit dictionary.</summary>
    public Task<IReadOnlyList<UnitView>> ListUnits(CancellationToken cancellationToken = default)
    {
        return Read<IReadOnlyList<UnitView>>("units", cancellationToken);
    }

    /// <summary>
    /// Uploads one image and waits for extraction, which the endpoint runs before it answers. The bytes
    /// are sent exactly as given, as the one <c>file</c> part, and never decoded: a client that never
    /// holds a bitmap cannot re-encode one (D4).
    /// </summary>
    public Task<CaptureResult> CaptureReceipt(string fileName, IReadOnlyList<byte> image, CancellationToken cancellationToken = default)
    {
        var bytes = image as byte[] ?? [.. image];

        return Send<CaptureResult>(
            () =>
            {
                // No content type is named for the part: the server judges the format from the bytes,
                // and a type guessed here from the file name would be a judgement the client has no
                // business making.
                var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", fileName } };

                return new HttpRequestMessage(HttpMethod.Post, "receipts/capture") { Content = form };
            },
            cancellationToken);
    }

    /// <summary>Records a purchase, with its captured receipt attached where one is carried.</summary>
    public Task<RecordPurchaseResponse> RecordPurchase(RecordPurchaseRequest request, CancellationToken cancellationToken = default)
    {
        return Send<RecordPurchaseResponse>(
            () => new HttpRequestMessage(HttpMethod.Post, "purchases") { Content = JsonContent.Create(request, options: s_writeJson) },
            cancellationToken);
    }

    /// <summary>
    /// The failure side of a response. A body in the error shape is the ledger's own words; anything
    /// else - a proxy's HTML page, an empty body - is still a ledger failure, just an unnamed one.
    /// </summary>
    private static async Task<LedgerException> FailureOf(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ErrorResponse? body = null;

        try
        {
            body = await response.Content.ReadFromJsonAsync<ErrorResponse>(s_json, cancellationToken);
        }
        catch (JsonException)
        {
        }

        var status = (int)response.StatusCode;

        return body is { Message: not null, Code: not null }
            ? new LedgerException(body.Message, body.Code, status, body.CorrelationId)
            : new LedgerException(Unreadable, status: status);
    }

    // Paths are relative, without a leading slash, so that a base address with a path of its own
    // ("http://host/api/") keeps it rather than being reset to the host's root.
    private Task<T> Read<T>(string path, CancellationToken cancellationToken)
    {
        return Send<T>(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);
    }

    private async Task<T> Send<T>(Func<HttpRequestMessage> request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            using var message = request();
            response = await http.SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // A timeout surfaces as a cancellation nobody asked for, and is a failure to reach the
            // ledger like any other.
            throw new LedgerException(Unreachable);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await FailureOf(response, cancellationToken);
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(s_json, cancellationToken)
                    ?? throw new LedgerException(Unreadable, status: (int)response.StatusCode);
            }
            catch (JsonException)
            {
                throw new LedgerException(Unreadable, status: (int)response.StatusCode);
            }
        }
    }
}
