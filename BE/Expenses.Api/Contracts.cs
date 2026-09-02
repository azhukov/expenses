using Expenses.Application.Extraction;
using Expenses.Application.Merchants;
using Expenses.Application.Purchases;
using Expenses.Application.Receipts;
using Expenses.Application.ReferenceData;
using Expenses.Domain;

namespace Expenses.Api;

/// <summary>
/// The request shapes the browser client posts. Binding is all these do: no rule may live in an
/// adapter (D1), so anything a request could be wrong about is decided by a use case.
/// </summary>
/// <param name="DiscountPercentage">
/// Accepted only so that it can be refused: a percentage is a rounded rendering of two numbers
/// already stored, and keeping it would be a second, lossy source of truth (D19).
/// </param>
public sealed record ExpenseRequest(
    string Description,
    decimal Amount,
    decimal? Quantity = null,
    string? UnitCode = null,
    decimal? UnitPrice = null,
    string? CategoryCode = null,
    string? CategoryRaw = null,
    string? UnitRaw = null,
    decimal? ListUnitPrice = null,
    decimal? DiscountAmount = null,

    decimal? DiscountPercentage = null)
{
    public ExpenseCommand ToCommand() => new(
        Description,
        Amount,
        Quantity,
        UnitCode,
        UnitPrice,
        CategoryCode,
        CategoryRaw,
        UnitRaw,
        ListUnitPrice,
        DiscountAmount);
}

public sealed record MerchantRequest(string Text, string? TaxId = null)
{
    public MerchantCommand ToCommand() => new(Text, TaxId);
}

/// <summary>
/// What a client resubmits from a capture response in order to confirm it — the temporary key,
/// and the extraction outcome exactly as capture reported it (D12: nothing about a capture is held
/// server-side, so this is the only way the server learns it again).
/// </summary>
public sealed record CapturedReceiptRequest(
    Guid TempKey,
    Receipt.ExtractionState State,
    string? FailureReason = null,
    string? SuppliedIkof = null,
    string? SuppliedJikr = null,
    string? ExtractedIkof = null,
    string? ExtractedJikr = null,
    Receipt.FiscalSource FiscalExtractedSource = Receipt.FiscalSource.None,
    string? FiscalCreatedAt = null)
{
    public CapturedReceiptCommand ToCommand() => new(
        TempKey,
        State,
        FailureReason,
        SuppliedIkof,
        SuppliedJikr,
        ExtractedIkof,
        ExtractedJikr,
        FiscalExtractedSource,
        FiscalCreatedAt);
}

/// <summary>
/// <see cref="OccurredAt"/> may be omitted only when <see cref="Capture"/> is supplied and its
/// fiscal QR decoded an invoice creation timestamp (D5).
/// </summary>
public sealed record RecordPurchaseRequest(
    decimal Amount,
    IReadOnlyList<ExpenseRequest> Expenses,
    MerchantRequest? Merchant = null,
    DateTime? OccurredAt = null,
    CapturedReceiptRequest? Capture = null);

public sealed record ConfirmCandidatesRequest(IReadOnlyList<ExpenseRequest>? Expenses = null);

public sealed record CreateCategoryRequest(string Code, string Name, string? ParentCode = null);

/// <summary>
/// <see cref="Code"/> is carried so that an attempt to change it can be refused rather than
/// silently ignored — the code is the identity stored references point at (D8).
/// </summary>
public sealed record RenameCategoryRequest(string Name, string? Code = null);

public sealed record RenameMerchantRequest(string Name);

public sealed record SetMerchantParentRequest(long? ParentId);

/// <summary>
/// A recorded purchase, with <see cref="AlreadyRecorded"/> beside the status code so a client that
/// only reads the body can still tell the two outcomes apart (D3).
/// </summary>
public sealed record RecordPurchaseResponse(
    long Id,
    DateTime OccurredAt,
    decimal Amount,
    long? MerchantId,
    string? MerchantRaw,
    bool HasReceipt,
    IReadOnlyList<ExpenseView> Expenses,
    decimal TotalSaving,
    decimal? SavingPercentage,
    bool AlreadyRecorded,
    MerchantView? Merchant,
    bool MerchantNewlyAdded)
{
    public static RecordPurchaseResponse Of(RecordPurchaseResult result) => new(
        result.Purchase.Id,
        result.Purchase.OccurredAt,
        result.Purchase.Amount,
        result.Purchase.MerchantId,
        result.Purchase.MerchantRaw,
        result.Purchase.HasReceipt,
        result.Purchase.Expenses,
        result.Purchase.TotalSaving,
        result.Purchase.SavingPercentage,
        result.AlreadyRecorded,
        result.Merchant,
        result.MerchantNewlyAdded);
}

/// <summary>
/// One extraction, as a reviewer needs to read it: the stages that ran, the stage that produced
/// each value, every arithmetic check with its outcome, and the fiscal corroboration state (D20).
/// </summary>
public sealed record ExtractionResponse(
    ReceiptView Receipt,
    ExtractionResultView? Result,
    ArithmeticValidationReport? Validation,
    bool CandidatesHeld)
{
    public static ExtractionResponse Of(ExtractionView view) =>
        new(view.Receipt, view.Result, view.Validation, view.CandidatesHeld);
}

/// <summary>
/// The single error shape, for every failure the interface can produce. A generic failure carries
/// a correlation identifier and nothing else, so no internal detail leaks.
/// </summary>
public sealed record ErrorResponse(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?> Fields,
    string? CorrelationId = null);

/// <summary>Reference data, listed by the shapes the Application already returns.</summary>
public sealed record ReferenceDataResponse(IReadOnlyList<CategoryView> Categories, IReadOnlyList<UnitView> Units);
