using System.Collections.Immutable;

namespace Sia;

public sealed class ExecutionPlan
{
    public ImmutableArray<SystemChain.Entry> Entries { get; }

    internal ExecutionPlan(ImmutableArray<SystemChain.Entry> entries)
    {
        Entries = entries;
    }
}
