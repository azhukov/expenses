using Expenses.Application.Dtos;
using Expenses.Application.Errors;
using Expenses.Application.Interfaces;
using Expenses.Domain.Entities;

namespace Expenses.Application.Services;

/// <summary>
/// Units are fixed reference data (D15): there is no create, rename or deactivate use case here.
/// What is editable is a unit's aliases — the spellings a receipt may print for it — which are
/// listed with each unit and added and removed by the user, or learned when a line is saved.
/// </summary>
public sealed class UnitService(IUnitRepository units, IUnitAliasRepository aliases, IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<UnitView>> List(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var listed = await units.List(includeInactive, cancellationToken);
        var allAliases = await aliases.List(cancellationToken);

        return [.. listed
            .OrderBy(unit => unit.Code, StringComparer.Ordinal)
            .Select(unit => UnitView.Of(unit, allAliases))];
    }

    /// <summary>
    /// Adds a spelling to a unit. Adding one the unit already has succeeds and changes nothing, so
    /// repeating the call is safe; one another unit has, or one that spells a unit's code, is
    /// refused rather than re-pointed.
    /// </summary>
    public async Task<UnitView> AddAlias(
        string unitCode,
        string alias,
        CancellationToken cancellationToken = default)
    {
        string normalised = UnitText.Normalise(alias)
            ?? throw ExpensesException.For(
                ApplicationErrors.UnitAliasEmpty,
                "A unit alias needs some text beyond spaces, periods and slashes.",
                ("alias", alias));

        var unit = await RequireUnit(unitCode, cancellationToken);

        if (!unit.IsActive)
        {
            throw ExpensesException.For(
                ApplicationErrors.UnitInactive,
                $"The unit '{unit.Code}' has been deactivated and cannot be given a new alias.",
                ("unitCode", unit.Code));
        }

        // A code already matches on its own, and matching tries codes first, so an alias spelling
        // one could never be reached — and would say something false about the other unit.
        var allUnits = await units.List(includeInactive: true, cancellationToken);

        if (allUnits.FirstOrDefault(other => UnitText.Normalise(other.Code) == normalised) is { } coded)
        {
            throw ExpensesException.For(
                ApplicationErrors.UnitAliasIsUnitCode,
                $"'{normalised}' is the code of the unit {coded.Code}, and matches it without an alias.",
                ("alias", normalised),
                ("unitCode", coded.Code));
        }

        if (await aliases.FindByAlias(normalised, cancellationToken) is { } existing)
        {
            if (existing.UnitId == unit.Id)
            {
                return UnitView.Of(unit, await aliases.List(cancellationToken));
            }

            // Named for a person as well as coded for a program: the client shows this message as is.
            var owner = allUnits.First(other => other.Id == existing.UnitId);

            throw ExpensesException.For(
                ApplicationErrors.UnitAliasConflict,
                $"The alias '{normalised}' already belongs to {owner.Name} ({owner.Code}); remove it there first.",
                ("alias", normalised),
                ("unitCode", owner.Code));
        }

        await aliases.Add(UnitAlias.Create(unit.Id, normalised), cancellationToken);
        await unitOfWork.SaveChanges(cancellationToken);

        return UnitView.Of(unit, await aliases.List(cancellationToken));
    }

    /// <summary>
    /// Removes a spelling from a unit. Expenses already matched through it keep their unit and their
    /// printed text; only later matching changes.
    /// </summary>
    public async Task RemoveAlias(
        string unitCode,
        string alias,
        CancellationToken cancellationToken = default)
    {
        var unit = await RequireUnit(unitCode, cancellationToken);
        string? normalised = UnitText.Normalise(alias);

        // One that belongs to another unit is reported as missing from this one, not removed: the
        // caller named a pairing, and that pairing does not exist.
        var existing = normalised is null ? null : await aliases.FindByAlias(normalised, cancellationToken);

        if (existing is null || existing.UnitId != unit.Id)
        {
            throw ExpensesException.For(
                ApplicationErrors.UnitAliasNotFound,
                $"The unit {unit.Code} has no alias '{normalised ?? alias}'.",
                ("alias", normalised ?? alias),
                ("unitCode", unit.Code));
        }

        await aliases.Remove(existing, cancellationToken);
        await unitOfWork.SaveChanges(cancellationToken);
    }

    private async Task<Unit> RequireUnit(string unitCode, CancellationToken cancellationToken)
        => await units.FindByCode(unitCode, cancellationToken)
            ?? throw ExpensesException.For(
                ApplicationErrors.UnitNotFound,
                $"There is no unit with code '{unitCode}'.",
                ("unitCode", unitCode));
}
