namespace Expenses.Application.Errors;

/// <summary>
/// The single error model both adapters map from (D1 — no rule may live in an adapter).
/// A stable <see cref="Code"/>, a human-readable <see cref="Message"/>, and the fields that
/// gave offence, so a client can point at the right input without parsing prose.
/// </summary>
public sealed record ApplicationError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?> Fields)
{
    private static readonly IReadOnlyDictionary<string, object?> s_noFields = new Dictionary<string, object?>();

    public static ApplicationError From(string code, string message, params (string Name, object? Value)[] fields)
        => new(code, message, fields.Length == 0
            ? s_noFields
            : fields.ToDictionary(field => field.Name, field => field.Value));

    /// <summary>
    /// A domain rule violation is an application error. The domain throws the framework exceptions
    /// — it holds nothing but entities and so carries no error type and no codes of its own — so the
    /// use case that called the entity names the code, which is why this takes one.
    /// </summary>
    public static ApplicationError From(string code, Exception exception) => exception switch
    {
        // The offending value travels with the error, which is the whole point of the field map:
        // a client can point at the input rather than re-parsing the message.
        ArgumentOutOfRangeException outOfRange => From(
            code,
            outOfRange.Message,
            (outOfRange.ParamName ?? "value", outOfRange.ActualValue)),
        ArgumentException argument => From(code, argument.Message, (argument.ParamName ?? "value", null)),
        _ => From(code, exception.Message),
    };
}
