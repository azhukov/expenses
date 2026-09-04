using System.Reflection;

namespace Expenses.Application.Tests.Fakes;

/// <summary>
/// Entity identifiers are database-owned (D10), so nothing in the domain assigns them. The fakes
/// stand in for the database and therefore have to, which is what this is for. It exists in the
/// test project only.
/// </summary>
internal static class Identity
{
    public static T WithId<T>(this T entity, long id)
    {
        typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(entity, [id]);

        return entity;
    }

    public static long IdOf(object entity) =>
        (long)typeof(object).GetType().GetProperty("Id")!.GetValue(entity)!;
}
