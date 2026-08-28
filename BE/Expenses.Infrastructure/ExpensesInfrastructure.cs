using Expenses.Application.Abstractions;
using Expenses.Application.Extraction;
using Expenses.Application.Merchants;
using Expenses.Application.Purchases;
using Expenses.Application.Receipts;
using Expenses.Application.ReferenceData;
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
        var connection = configuration.GetConnectionString(ConnectionName)
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
        var decoder = Bind<DecoderOptions>(configuration, "Extraction:Decoder");
        var queue = Bind<QueueOptions>(configuration, "Extraction:Queue");

        services.AddSingleton(extraction);
        services.AddSingleton(placeholder);
        services.AddSingleton(decoder);
        services.AddSingleton(queue);

        services.AddSingleton<IFiscalCodeDecoder>(provider => new FiscalCodeDecoder(
            provider.GetRequiredService<DecoderOptions>(),
            provider.GetRequiredService<ILogger<FiscalCodeDecoder>>()));

        services.AddSingleton<IExtractionStage>(provider => new FiscalDecodeStage(
            provider.GetRequiredService<IFiscalCodeDecoder>(),
            provider.GetRequiredService<ILogger<FiscalDecodeStage>>()));

        services.AddSingleton<IExtractionStage>(provider => new VisionStage(
            new PlaceholderReceiptExtractor(
                provider.GetRequiredService<PlaceholderOptions>(),
                VisionStage.CheapTier),
            VisionStage.CheapTier,
            ExtractionStageRole.Primary));

        services.AddSingleton<IExtractionStage>(provider => new VisionStage(
            new PlaceholderReceiptExtractor(
                provider.GetRequiredService<PlaceholderOptions>(),
                VisionStage.ExpensiveTier),
            VisionStage.ExpensiveTier,
            ExtractionStageRole.Fallback));

        services.AddSingleton(provider => new ExtractionCascade(
            provider.GetServices<IExtractionStage>(),
            provider.GetRequiredService<ExtractionOptions>()));

        services.AddSingleton<ExtractionQueue>();
        services.AddSingleton<IExtractionQueue>(provider => provider.GetRequiredService<ExtractionQueue>());

        // Registered but not necessarily hosted: the queue is always there to accept work, while
        // draining it is this process's job only when it is configured to be.
        services.AddSingleton<ExtractionService>();

        if (Bind<ExtractionHostingOptions>(configuration, "Extraction").DrainInBackground)
        {
            services.AddHostedService(provider => provider.GetRequiredService<ExtractionService>());
        }
    }

    /// <summary>
    /// The use cases both adapters call. They are registered here because the composition root is
    /// here: an adapter reaches this project for one method and nothing else (D1).
    /// </summary>
    private static void AddUseCases(IServiceCollection services)
    {
        services.AddScoped<RecordPurchase>();
        services.AddScoped<GetPurchase>();
        services.AddScoped<ListPurchases>();

        services.AddScoped<ListCategories>();
        services.AddScoped<CreateCategory>();
        services.AddScoped<RenameCategory>();
        services.AddScoped<DeactivateCategory>();
        services.AddScoped<DeleteCategory>();
        services.AddScoped<ListUnits>();

        services.AddScoped<ResolveMerchant>();
        services.AddScoped<ListMerchants>();
        services.AddScoped<SearchMerchants>();
        services.AddScoped<RenameMerchant>();
        services.AddScoped<SetMerchantParent>();
        services.AddScoped<DeactivateMerchant>();

        services.AddScoped<AttachReceiptImage>();
        services.AddScoped<DeleteReceipt>();
        services.AddScoped<GetExtractionCandidates>();
        services.AddScoped<ConfirmCandidates>();
        services.AddScoped<DiscardCandidates>();
        services.AddScoped<RequeueExtraction>();
        services.AddScoped<RunExtraction>();
    }

    private static TOptions Bind<TOptions>(IConfiguration configuration, string section)
        where TOptions : class, new()
    {
        var options = new TOptions();
        configuration.GetSection(section).Bind(options);

        return options;
    }
}
