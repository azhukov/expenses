# Build context is BE/, not this directory: the host is one project of several and restores from
# the same project references the solution does.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so a source-only change does not re-run restore.
COPY global.json Directory.Build.props .editorconfig ./
COPY Expenses.Domain/Expenses.Domain.csproj Expenses.Domain/
COPY Expenses.Application/Expenses.Application.csproj Expenses.Application/
COPY Expenses.Infrastructure/Expenses.Infrastructure.csproj Expenses.Infrastructure/
COPY Expenses.Api/Expenses.Api.csproj Expenses.Api/
RUN dotnet restore Expenses.Api/Expenses.Api.csproj

COPY Expenses.Domain/ Expenses.Domain/
COPY Expenses.Application/ Expenses.Application/
COPY Expenses.Infrastructure/ Expenses.Infrastructure/
COPY Expenses.Api/ Expenses.Api/
RUN dotnet publish Expenses.Api/Expenses.Api.csproj -c Release -o /app --no-restore

# The Debian runtime image, not Alpine: InvariantGlobalization is off (Directory.Build.props) and
# ICU root collation is the whole point of how the database is provisioned (D13).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# curl is here for the compose healthcheck and nothing else. It is what lets the MCP host wait for
# the API to have finished applying migrations rather than merely to have been started.
RUN apt-get update \
    && apt-get install --no-install-recommends -y curl \
    && rm -rf /var/lib/apt/lists/*

# The receipt store (D11). Created and owned here rather than in compose, because Docker seeds an
# empty named volume from the image directory it is mounted over — including its ownership. Without
# this the volume arrives root-owned and the host refuses to start, which is the check doing its job
# but for a reason nobody wants to debug twice.
#
# `receipts-temp` is where captures wait to be confirmed, and is deliberately a sibling rather than
# a child of the permanent store, so a naive walk over either one never mistakes an unconfirmed
# capture for a confirmed receipt. It gets no volume: what lands there is swept within a day.
RUN mkdir -p /var/lib/expenses/receipts /var/lib/expenses/receipts-temp

WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "Expenses.Api.dll"]
