using Expenses.Application.Errors;
using ModelContextProtocol;

namespace Expenses.Mcp;

/// <summary>
/// Turns a use-case failure into a tool error carrying the stable code and the message as written
/// (D22, D1). No MCP-specific wording is invented here: the same failure over HTTP says the same
/// thing, which is the point of both adapters mapping one error model.
/// </summary>
internal static class ToolFailure
{
    public static McpException From(ExpensesException exception)
        => new($"{exception.Error.Message} [{exception.Error.Code}]");
}
