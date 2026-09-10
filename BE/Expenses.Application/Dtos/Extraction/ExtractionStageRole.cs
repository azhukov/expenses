namespace Expenses.Application.Dtos;

/// <summary>
/// Where a stage sits in the cascade (D20). The order is free stages first, the cheap vision tier
/// next, arithmetic validation, and the expensive tier only when validation failed.
/// </summary>
public enum ExtractionStageRole
{
    /// <summary>Free and deterministic; may produce nothing, and must cost nothing when it does.</summary>
    Opportunistic = 0,

    /// <summary>The cheap vision tier — the stage expected to produce the lines.</summary>
    Primary = 1,

    /// <summary>The expensive tier. Runs only after a failed validation.</summary>
    Fallback = 2,
}
