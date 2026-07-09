namespace Sia;

using System.Collections.Immutable;

[Serializable]
public class ScheduleCycleException : InvalidSystemDependencyException
{
    public ImmutableArray<ScheduleLabel> Cycle { get; }

    public ScheduleCycleException() { }
    public ScheduleCycleException(string message) : base(message) { }
    public ScheduleCycleException(string message, Exception inner) : base(message, inner) { }

    public ScheduleCycleException(IReadOnlyList<ScheduleLabel> cycle)
        : base(FormatMessage(cycle))
    {
        Cycle = [.. cycle];
    }

    private static string FormatMessage(IReadOnlyList<ScheduleLabel> cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        return $"Cycle detected in schedule dependency graph: {string.Join(" -> ", cycle)}";
    }
}
