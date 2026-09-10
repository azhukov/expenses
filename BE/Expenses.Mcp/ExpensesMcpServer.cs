using Expenses.Application.Errors;
using Expenses.Mcp.Tools;

namespace Expenses.Mcp;

/// <summary>
/// Registers the tool surface. It is a method rather than inline host setup so that the same
/// registration is used by the stdio host, a hosted HTTP deployment, and the tests — a tool that
/// exists only in one of the three would be a tool nobody has really exercised.
/// </summary>
public static class ExpensesMcpServer
{
    public static IMcpServerBuilder AddExpensesMcpServer(this IServiceCollection services)
        => services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
                {
                    Name = "expenses",
                    Version = "1.0",
                    Title = "Personal expense ledger",
                };

                options.ServerInstructions = """
                    Records and answers questions about a personal expense ledger.

                    A purchase is a container of one or more expense lines whose amounts must sum
                    exactly to the purchase total. Categories and units are referenced by code, never
                    by display name — list them if you are unsure. Recording is safe to repeat: a
                    purchase with the same date, time and amount is returned rather than duplicated.

                    Receipt images are uploaded over the HTTP interface, not here; the extraction
                    tools read and re-run extraction for images that are already stored.
                    """;
            })
            .WithTools<PurchaseTools>()
            .WithTools<ReferenceDataTools>()
            .WithTools<MerchantTools>()
            .WithTools<ExtractionTools>()

            // One place translates a use-case failure into a tool error, so no tool handler
            // carries error wording of its own and both adapters report the same failure (D1).
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (request, cancellationToken) =>
            {
                try
                {
                    return await next(request, cancellationToken);
                }
                catch (ExpensesException failure)
                {
                    throw ToolFailure.From(failure);
                }
            }));
}
