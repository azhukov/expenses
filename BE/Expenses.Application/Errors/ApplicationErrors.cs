namespace Expenses.Application.Errors;

/// <summary>
/// Every stable error code in the system, including the ones raised by a domain rule. Part of the
/// contract for both adapters, so they are not reworded casually.
///
/// The domain contains entities and nothing else, so it declares no codes: an entity signals a
/// violation with the framework exception that fits it, and the use case that called the entity
/// translates that into the code below which names the rule.
/// </summary>
public static class ApplicationErrors
{
    // Raised by a domain rule and translated by the use case that called the entity.
    public const string AmountNegative = "amount.negative";
    public const string AmountPrecision = "amount.precision_exceeded";

    public const string QuantityNegative = "quantity.negative";
    public const string QuantityPrecision = "quantity.precision_exceeded";

    public const string ExpenseDescriptionRequired = "expense.description_required";
    public const string ExpenseDiscountIncomplete = "expense.discount_incomplete";
    public const string ExpenseDiscountNegative = "expense.discount_negative";
    public const string ExpenseDiscountPercentageNotAccepted = "expense.discount_percentage_not_accepted";

    public const string PurchaseNoExpenses = "purchase.no_expenses";
    public const string PurchaseReconciliationMismatch = "purchase.reconciliation_mismatch";
    public const string PurchaseOccurrenceRequired = "purchase.occurrence_required";

    public const string CategoryCodeRequired = "category.code_required";
    public const string CategoryNameRequired = "category.name_required";
    public const string CategoryParentCycle = "category.parent_cycle";

    public const string UnitCodeRequired = "unit.code_required";
    public const string UnitNameRequired = "unit.name_required";

    public const string MerchantNameRequired = "merchant.name_required";
    public const string MerchantParentCycle = "merchant.parent_cycle";

    public const string ReceiptImageContentHashInvalid = "receipt_image.content_hash_invalid";
    public const string ReceiptStorageKeyInvalid = "receipt_image.storage_key_invalid";
    public const string ReceiptImageContentTypeRequired = "receipt_image.content_type_required";
    public const string ReceiptImageSizeInvalid = "receipt_image.size_invalid";
    public const string ExtractionInvalidTransition = "extraction.invalid_transition";
    public const string ExtractionCandidateDescriptionRequired = "extraction_candidate.description_required";

    // Raised above the domain.
    public const string PurchaseNotFound = "purchase.not_found";

    public const string CategoryNotFound = "category.not_found";
    public const string CategoryDuplicateCode = "category.duplicate_code";
    public const string CategoryCodeImmutable = "category.code_immutable";
    public const string CategorySystemUndeletable = "category.system_undeletable";
    public const string CategoryHasActiveChildren = "category.has_active_children";
    public const string CategoryInactive = "category.inactive";

    public const string UnitNotFound = "unit.not_found";
    public const string UnitInactive = "unit.inactive";
    public const string UnitCreationNotSupported = "unit.creation_not_supported";

    public const string MerchantNotFound = "merchant.not_found";
    public const string MerchantSearchTermRequired = "merchant.search_term_required";
    public const string MerchantDuplicateTaxId = "merchant.duplicate_tax_id";
    public const string MerchantHasActiveChildren = "merchant.has_active_children";

    public const string ReceiptImageNotFound = "receipt_image.not_found";
    public const string ReceiptImageUnsupportedFormat = "receipt_image.unsupported_format";
    public const string ReceiptImageTooLarge = "receipt_image.too_large";
    public const string CaptureNotFound = "capture.not_found";

    public const string ListingPageSizeInvalid = "listing.page_size_invalid";
    public const string ListingRangeInvalid = "listing.range_invalid";

    public const string ExtractionCandidatesNotFound = "extraction.candidates_not_found";
    public const string ExtractionNotAttempted = "extraction.not_attempted";
}
