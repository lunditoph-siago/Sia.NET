namespace Sia;

using System.Collections.Immutable;

public sealed class ExecutionPlan
{
    public ImmutableArray<SystemChain.Entry> Entries { get; }

    internal ExecutionPlan(ImmutableArray<SystemChain.Entry> entries)
    {
        Entries = entries;
    }
}
