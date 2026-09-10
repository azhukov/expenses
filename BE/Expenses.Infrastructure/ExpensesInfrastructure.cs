using Expenses.Application.Dtos;
using Expenses.Application.Interfaces;
using Expenses.Application.Services;
using Expenses.Infrastructure.Extraction;
using Expenses.Infrastructure.Persistence;
using Expenses.Infrastructure.Persistence.Configurations;
using Expenses.Infrastructure.Receipts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Expenses.Infrastructure;

/// <summary>
/// The single entry point both hosts call (D1). `Expenses.Api` and `Expenses.Mcp` reference this
/// project for exactly this method and touch nothing else in it, which is what keeps configuration
/// and wiring from existing twice.
/// </summary>
public static class ExpensesInfrastructure
{
    public const string ConnectionName = "Expenses";

    public static IServiceCollection AddExpensesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connection = configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException(
                $"No connection string named '{ConnectionName}' is configured.");

        services.AddDbContext<ExpensesDbContext>(options => options
            .UseNpgsql(connection)

            // Categories are seeded and user-editable, so they are upserted by code rather than
            // declared as model-managed rows: a rename and a user's own categories both survive a
            // re-run (D15). Units go the other way, by HasData, because nobody edits them.
            .UseAsyncSeeding((context, _, cancellationToken) =>
                CategorySeed.Apply((ExpensesDbContext)context, cancellationToken))
            .UseSeeding((context, _) =>
                CategorySeed.Apply((ExpensesDbContext)context).GetAwaiter().GetResult()));

        services.AddScoped<IPurchaseRepository, PurchaseRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IUnitRepository, UnitRepository>();
        services.AddScoped<IMerchantRepository, MerchantRepository>();
        services.AddScoped<IUnitOfWork, ExpensesUnitOfWork>();

        // The receipt store is a directory, not a table: the bytes are files and the ledger holds
        // the reference (D11).
        var receipts = Bind<ReceiptStoreOptions>(configuration, "Receipts");
        services.AddSingleton(receipts);
        services.AddSingleton<IReceiptImageStore, ReceiptFileStore>();

        // A distinct root from the permanent store's, so a capture waiting to be confirmed is never
        // mistaken for a confirmed receipt by a naive directory walk over either one.
        var temporaryReceipts = Bind<TemporaryReceiptStoreOptions>(configuration, "TemporaryReceipts");
        services.AddSingleton(temporaryReceipts);
        services.AddSingleton<ITemporaryReceiptStore, TemporaryReceiptFileStore>();

        // A singleton because candidates are transient and shared across scopes: the background
        // drain produces them and a later request reads them, both within one process (D12).
        services.AddSingleton<IExtractionCandidateStore, InMemoryExtractionCandidateStore>();

        AddExtraction(services, configuration);
        AddUseCases(services);

        return services;
    }

    /// <summary>
    /// The cascade (D20), wired cheapest-first. The two vision tiers are two configured instances
    /// of the same placeholder in this change and two instances of a real engine later; nothing
    /// about the shape changes when they become real, which is why it is built now.
    /// </summary>
    private static void AddExtraction(IServiceCollection services, IConfiguration configuration)
    {
        var extraction = Bind<ExtractionOptions>(configuration, "Extraction");
        var placeholder = Bind<PlaceholderOptions>(configuration, "Extraction:Placeholder");

        services.AddSingleton(extraction);
        services.AddSingleton(placeholder);

        // No options of its own: the preprocessing ladder the budget bounded is gone, because the
        // measured hit needed none of it and no amount of it rescued a measured miss (D21).
        services.AddSingleton<IFiscalCodeDecoder>(new FiscalCodeDecoder());

        // A singleton, so that an invoice already retrieved stays retrieved: the answer is cached on
        // the client, and a request that leaves the building is worth more care than a local one.
        services.AddSingleton(Bind<PortalOptions>(configuration, "Extraction:Portal"));
        services.AddHttpClient(FiscalPortalClient.ClientName);
        services.AddSingleton<IFiscalInvoiceRetrieval>(provider => new FiscalPortalClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<PortalOptions>(),
            provider.GetRequiredService<ILogger<FiscalPortalClient>>()));

        services.AddSingleton<IExtractionStage>(provider => new FiscalDecodeStage(
            provider.GetRequiredService<IFiscalCodeDecoder>(),
            provider.GetRequiredService<ILogger<FiscalDecodeStage>>()));

        services.AddSingleton<IExtractionStage>(provider => new FiscalInvoiceStage(
            provider.GetRequiredService<IFiscalInvoiceRetrieval>(),
            provider.GetRequiredService<ILogger<FiscalInvoiceStage>>()));

        // Both vision tiers are the fallback now: they answer for the receipts the deterministic
        // path cannot serve, in registration order, and neither runs behind an invoice the tax
        // authority stated and the arithmetic confirmed (D22).
        services.AddSingleton<IExtractionStage>(provider => new VisionStage(
            new PlaceholderReceiptExtractor(
                provider.GetRequiredService<PlaceholderOptions>(),
                VisionStage.CheapTier),
            VisionStage.CheapTier,
            ExtractionStageRole.Fallback));

        services.AddSingleton<IExtractionStage>(provider => new VisionStage(
            new PlaceholderReceiptExtractor(
                provider.GetRequiredService<PlaceholderOptions>(),
                VisionStage.ExpensiveTier),
            VisionStage.ExpensiveTier,
            ExtractionStageRole.Fallback));

        services.AddSingleton(provider => new ExtractionCascade(
            provider.GetServices<IExtractionStage>(),
            provider.GetRequiredService<ExtractionOptions>()));

        // The one piece of background processing this change keeps, unrelated to extraction:
        // extraction itself always runs synchronously in the request from here on.
        services.AddSingleton(Bind<OrphanCaptureSweepOptions>(configuration, "TemporaryReceipts:Sweep"));
        services.AddHostedService<OrphanCaptureSweep>();
    }

    /// <summary>
    /// The application services both adapters call. They are registered here because the
    /// composition root is here: an adapter reaches this project for one method and nothing else
    /// (D1).
    /// </summary>
    private static void AddUseCases(IServiceCollection services)
    {
        services.AddScoped<MerchantService>();
        services.AddScoped<PurchaseService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<UnitService>();
        services.AddScoped<ReceiptService>();
    }

    private static TOptions Bind<TOptions>(IConfiguration configuration, string section)
        where TOptions : class, new()
    {
        var options = new TOptions();
        configuration.GetSection(section).Bind(options);

        return options;
    }
}
