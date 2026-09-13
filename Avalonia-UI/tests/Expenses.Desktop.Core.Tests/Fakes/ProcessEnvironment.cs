namespace Expenses.Desktop.Core.Tests.Fakes;

/// <summary>
/// Tests that set process environment variables run one at a time: the environment is shared by
/// every test in the process, and xunit runs classes in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironment
{
    public const string Name = "Process environment";
}
