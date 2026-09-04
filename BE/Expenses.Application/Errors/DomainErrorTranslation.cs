namespace Expenses.Application.Errors;

/// <summary>
/// Names the stable code for a rule the domain signalled with a framework exception (D22). There is
/// one method per entity because the same exception type and parameter name mean different things
/// in different entities, and the use case always knows which one it called.
/// </summary>
internal static class DomainErrorTranslation
{
    public static ExpensesException Expense(Exception exception) => exception switch
    {
        ArgumentException { ParamName: "description" } => Wrap(ApplicationErrors.ExpenseDescriptionRequired, exception),

        // The pair is set together or not at all (D19), which arrives as a plain ArgumentException
        // naming whichever half was missing.
        ArgumentException { ParamName: "listUnitPrice" or "discountAmount" } and not ArgumentOutOfRangeException =>
            Wrap(ApplicationErrors.ExpenseDiscountIncomplete, exception),

        ArgumentOutOfRangeException { ParamName: "discountAmount" } negative when IsNegative(negative) =>
            Wrap(ApplicationErrors.ExpenseDiscountNegative, exception),

        ArgumentOutOfRangeException { ParamName: "quantity" } quantity => Wrap(
            IsNegative(quantity) ? ApplicationErrors.QuantityNegative : ApplicationErrors.QuantityPrecision,
            exception),

        ArgumentOutOfRangeException amount => Wrap(
            IsNegative(amount) ? ApplicationErrors.AmountNegative : ApplicationErrors.AmountPrecision,
            exception),

        _ => throw exception,
    };

    public static ExpensesException Purchase(Exception exception) => exception switch
    {
        ArgumentOutOfRangeException amount => Wrap(
            IsNegative(amount) ? ApplicationErrors.AmountNegative : ApplicationErrors.AmountPrecision,
            exception),

        // Reconciliation and the empty-purchase rule are checked by the use case before the
        // aggregate is built, so that the error can carry both amounts; reaching here means the
        // aggregate caught something the use case did not, and it is still a purchase rule.
        InvalidOperationException => Wrap(ApplicationErrors.PurchaseReconciliationMismatch, exception),

        _ => throw exception,
    };

    public static ExpensesException Category(Exception exception) => exception switch
    {
        ArgumentException { ParamName: "code" } => Wrap(ApplicationErrors.CategoryCodeRequired, exception),
        ArgumentException { ParamName: "name" } => Wrap(ApplicationErrors.CategoryNameRequired, exception),
        InvalidOperationException => Wrap(ApplicationErrors.CategoryParentCycle, exception),
        _ => throw exception,
    };

    public static ExpensesException Merchant(Exception exception) => exception switch
    {
        ArgumentException { ParamName: "name" } => Wrap(ApplicationErrors.MerchantNameRequired, exception),
        InvalidOperationException => Wrap(ApplicationErrors.MerchantParentCycle, exception),
        _ => throw exception,
    };

    public static ExpensesException Receipt(Exception exception) => exception switch
    {
        ArgumentException { ParamName: "contentHash" } =>
            Wrap(ApplicationErrors.ReceiptImageContentHashInvalid, exception),
        ArgumentException { ParamName: "storageKey" } =>
            Wrap(ApplicationErrors.ReceiptStorageKeyInvalid, exception),
        ArgumentException { ParamName: "contentType" } =>
            Wrap(ApplicationErrors.ReceiptImageContentTypeRequired, exception),
        ArgumentOutOfRangeException { ParamName: "sizeInBytes" } =>
            Wrap(ApplicationErrors.ReceiptImageSizeInvalid, exception),
        InvalidOperationException => Wrap(ApplicationErrors.ExtractionInvalidTransition, exception),
        _ => throw exception,
    };

    private static bool IsNegative(ArgumentOutOfRangeException exception)
        => exception.ActualValue is decimal value && value < 0m;

    private static ExpensesException Wrap(string code, Exception exception)
        => new(ApplicationError.From(code, exception), exception);
}
