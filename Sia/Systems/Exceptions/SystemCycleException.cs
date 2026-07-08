namespace Sia;

using System.Collections.Immutable;

[Serializable]
public class SystemCycleException : InvalidSystemDependencyException
{
    public ImmutableArray<SystemId> Cycle { get; }

    public SystemCycleException() { }
    public SystemCycleException(string message) : base(message) { }
    public SystemCycleException(string message, Exception inner) : base(message, inner) { }

    public SystemCycleException(IReadOnlyList<SystemId> cycle)
        : base(FormatMessage(cycle))
    {
        Cycle = [.. cycle];
    }

    private static string FormatMessage(IReadOnlyList<SystemId> cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        return $"Cycle detected in system dependency graph: {string.Join(" -> ", cycle)}";
    }
}
