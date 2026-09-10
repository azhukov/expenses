namespace Expenses.Application.Interfaces;

/// <summary>
/// Wall-clock time, never converted (D5). Every value it yields has
/// <see cref="DateTimeKind.Unspecified"/>, because an offset here would be invented.
/// </summary>
public interface IClock
{
    DateTime Now { get; }
}
