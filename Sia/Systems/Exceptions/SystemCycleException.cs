namespace Sia;

using System.Collections.Immutable;

[Serializable]
public class SystemCycleException : InvalidSystemDependencyException
{
    public ImmutableArray<Type> Cycle { get; }

    public SystemCycleException() { }
    public SystemCycleException(string message) : base(message) { }
    public SystemCycleException(string message, Exception inner) : base(message, inner) { }

    public SystemCycleException(IReadOnlyList<Type> cycle)
        : base(FormatMessage(cycle))
    {
        ArgumentNullException.ThrowIfNull(cycle);
        Cycle = cycle.ToImmutableArray();
    }

    private static string FormatMessage(IReadOnlyList<Type> cycle) =>
        $"Cycle detected in system dependency graph: {string.Join(" -> ", cycle.Select(t => t.FullName))}";
}
