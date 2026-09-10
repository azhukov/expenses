using Expenses.Application.Dtos;

namespace Expenses.Api;

/// <summary>Reference data, listed by the shapes the Application already returns.</summary>
public sealed record ReferenceDataResponse(IReadOnlyList<CategoryView> Categories, IReadOnlyList<UnitView> Units);
