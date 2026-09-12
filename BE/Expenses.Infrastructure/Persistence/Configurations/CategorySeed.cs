using Expenses.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Infrastructure.Persistence.Configurations;

/// <summary>
/// The initial category taxonomy, upserted by <c>code</c> rather than declared with
/// <c>HasData</c> (D15). Categories are seeded *and* user-editable: `HasData` treats the model as
/// the source of truth, so removing an entry would emit a DELETE and a user's rename would be
/// reverted on the next migration. Upserting by code means a rename survives, a user's own
/// categories survive, and a repeated run changes nothing.
///
/// Content, not structure: adding or renaming an entry here is editing seed data, and only affects
/// databases that have not seen that code before.
/// </summary>
internal static class CategorySeed
{
    private static readonly (string Code, string Name, string? ParentCode)[] s_rows =
    [
        ("GROCERIES", "Groceries", null),
        ("PRODUCE", "Fruit and vegetables", "GROCERIES"),
        ("BAKERY", "Bakery", "GROCERIES"),
        ("MEAT_FISH", "Meat and fish", "GROCERIES"),
        ("DAIRY", "Dairy and eggs", "GROCERIES"),
        ("BEVERAGES", "Beverages", "GROCERIES"),
        ("ALCOHOL", "Alcohol", "GROCERIES"),
        ("SNACKS", "Snacks and confectionery", "GROCERIES"),

        ("DINING", "Eating out", null),
        ("RESTAURANT", "Restaurant", "DINING"),
        ("CAFE", "Café", "DINING"),
        ("TAKEAWAY", "Takeaway and delivery", "DINING"),

        ("HOUSEHOLD", "Household", null),
        ("CLEANING", "Cleaning and laundry", "HOUSEHOLD"),
        ("KITCHEN", "Kitchen and tableware", "HOUSEHOLD"),
        ("FURNITURE", "Furniture and furnishings", "HOUSEHOLD"),

        ("HOME", "Home", null),
        ("RENT", "Rent", "HOME"),
        ("MAINTENANCE", "Repairs and maintenance", "HOME"),

        ("UTILITIES", "Utilities", null),
        ("ELECTRICITY", "Electricity and heating", "UTILITIES"),
        ("WATER", "Water and waste", "UTILITIES"),
        ("INTERNET", "Internet", "UTILITIES"),
        ("MOBILE", "Mobile and telephone", "UTILITIES"),

        ("TRANSPORT", "Transport", null),
        ("FUEL", "Fuel", "TRANSPORT"),
        ("PUBLIC_TRANSPORT", "Public transport", "TRANSPORT"),
        ("TAXI", "Taxi and ride hailing", "TRANSPORT"),
        ("VEHICLE", "Vehicle upkeep", "TRANSPORT"),

        ("HEALTH", "Health", null),
        ("PHARMACY", "Pharmacy", "HEALTH"),
        ("MEDICAL", "Medical and dental", "HEALTH"),

        ("PERSONAL", "Personal care", null),
        ("CLOTHING", "Clothing and footwear", null),

        ("LEISURE", "Leisure", null),
        ("BOOKS", "Books and media", "LEISURE"),
        ("SPORTS", "Sport and fitness", "LEISURE"),
        ("TRAVEL", "Travel and accommodation", "LEISURE"),
        ("SUBSCRIPTIONS", "Subscriptions", "LEISURE"),

        ("PETS", "Pets", null),
        ("GIFTS", "Gifts and donations", null),
        ("FEES", "Fees and charges", null),
        ("OTHER", "Other", null),
    ];

    public static async Task Apply(ExpensesDbContext context, CancellationToken cancellationToken = default)
    {
        var existing = await context.Categories.ToDictionaryAsync(
            category => category.Code,
            StringComparer.OrdinalIgnoreCase,
            cancellationToken);

        // Parents first and saved before their children, because a child is attached to the parent
        // entity and the parent needs an identifier to be attached to.
        await Insert(context, existing, parented: false, cancellationToken);
        await Insert(context, existing, parented: true, cancellationToken);
    }

    private static async Task Insert(
        ExpensesDbContext context,
        Dictionary<string, Category> existing,
        bool parented,
        CancellationToken cancellationToken)
    {
        bool added = false;

        foreach (var (code, name, parentCode) in s_rows.Where(row => (row.ParentCode is not null) == parented))
        {
            // A code that is already there is left exactly as it is: its name may be the user's,
            // and this run has nothing to say about it.
            if (existing.ContainsKey(code))
            {
                continue;
            }

            var parent = parentCode is null ? null : existing.GetValueOrDefault(parentCode);
            var category = Category.Create(code, name, parent, isSystem: true);

            context.Categories.Add(category);
            existing[code] = category;
            added = true;
        }

        if (added)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
