# Build context is BE/, not this directory: the host is one project of several and restores from
# the same project references the solution does.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so a source-only change does not re-run restore.
COPY global.json Directory.Build.props ./
COPY Expenses.Domain/Expenses.Domain.csproj Expenses.Domain/
COPY Expenses.Application/Expenses.Application.csproj Expenses.Application/
COPY Expenses.Infrastructure/Expenses.Infrastructure.csproj Expenses.Infrastructure/
COPY Expenses.Mcp/Expenses.Mcp.csproj Expenses.Mcp/
RUN dotnet restore Expenses.Mcp/Expenses.Mcp.csproj

COPY Expenses.Domain/ Expenses.Domain/
COPY Expenses.Application/ Expenses.Application/
COPY Expenses.Infrastructure/ Expenses.Infrastructure/
COPY Expenses.Mcp/ Expenses.Mcp/
RUN dotnet publish Expenses.Mcp/Expenses.Mcp.csproj -c Release -o /app --no-restore

# The Debian runtime image, not Alpine: InvariantGlobalization is off (Directory.Build.props) and
# ICU root collation is the whole point of how the database is provisioned (D13).
#
# Containerised, this host serves MCP over HTTP (EXPENSES_MCP_TRANSPORT=http, set in compose).
# stdio — the default, and the natural local-assistant experience — is a `dotnet run` on the host
# machine instead, because a container cannot hand its stdin to an MCP client.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# The receipt store (D11). Created and owned here rather than in compose, because Docker seeds an
# empty named volume from the image directory it is mounted over — including its ownership. Without
# this the volume arrives root-owned and the host refuses to start, which is the check doing its job
# but for a reason nobody wants to debug twice.
RUN mkdir -p /var/lib/expenses/receipts && chown -R $APP_UID:$APP_UID /var/lib/expenses

WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Expenses.Mcp.dll"]
