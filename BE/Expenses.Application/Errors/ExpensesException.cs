namespace Expenses.Application.Errors;

/// <summary>Carries an <see cref="ApplicationError"/> out of a use case.</summary>
public sealed class ExpensesException : Exception
{
    public ExpensesException(ApplicationError error)
        : base(error.Message) => Error = error;

    public ExpensesException(ApplicationError error, Exception innerException)
        : base(error.Message, innerException) => Error = error;

    public ApplicationError Error { get; }

    public static ExpensesException For(string code, string message, params (string Name, object? Value)[] fields)
        => new(ApplicationError.From(code, message, fields));
}
