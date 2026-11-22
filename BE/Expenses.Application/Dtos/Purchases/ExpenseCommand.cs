namespace Expenses.Application.Dtos;

/// <summary>A line as submitted. Reference data is addressed by <c>code</c> (D8).</summary>
public sealed record ExpenseCommand(
    string Description,
    decimal Amount,
    decimal? Quantity = null,
    string? UnitCode = null,
    decimal? UnitPrice = null,
    string? CategoryCode = null,
    string? CategoryRaw = null,
    string? UnitRaw = null,
    decimal? ListUnitPrice = null,
    decimal? DiscountAmount = null)
{
    /// <summary>
    /// A reference already resolved to an identifier — how a confirmed extraction candidate
    /// carries the match a stage made, since a stage matches against the dictionary rather than
    /// producing a code. Ignored when the corresponding code is supplied.
    /// </summary>
    public long? MatchedCategoryId { get; init; }

    public long? MatchedUnitId { get; init; }

    /// <summary>
    /// The caller's statement that this line's unit is still the one guessed from its quantity,
    /// untouched by a person. It only stops the line teaching an alias: it never supplies a unit nor
    /// makes an unacceptable line acceptable, so it is safe to take on trust (default-unit-from-quantity, D3).
    /// </summary>
    public bool UnitInferred { get; init; }
}
