namespace Sia;

using System.Collections.Immutable;

public static class Planner
{
    public static ExecutionPlan Plan(IReadOnlyList<SystemChain.Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var order = PlanOrder(entries);
        return new ExecutionPlan(
            order.Select(index => entries[index]).ToImmutableArray());
    }

    internal static ImmutableArray<int> PlanOrder(
        IReadOnlyList<SystemChain.Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var systems = new SystemChain.Entry?[entries.Count];
        for (var i = 0; i < entries.Count; i++) {
            systems[i] = entries[i];
        }
        return PlanOrder(systems);
    }

    internal static ImmutableArray<int> PlanOrder(
        IReadOnlyList<SystemChain.Entry?> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!HasConstraints(entries)) {
            return Enumerable.Range(0, entries.Count).ToImmutableArray();
        }

        var nodes = Enumerable.Range(0, entries.Count).ToArray();
        var result = DependencyGraph.Sort(nodes, CreateEdges(entries));
        if (result.HasCycle) {
            throw new SystemCycleException(
                result.Cycle.Select(index => entries[index]!.Value.Id).ToArray());
        }
        return result.Order;
    }

    private static bool HasConstraints(IReadOnlyList<SystemChain.Entry?> entries)
    {
        for (var i = 0; i < entries.Count; i++) {
            if (entries[i] is not { } entry) {
                continue;
            }
            var descriptor = entry.Descriptor;
            if (!descriptor.RunsBefore.IsEmpty || !descriptor.RunsAfter.IsEmpty) {
                return true;
            }
        }
        return false;
    }

    private static HashSet<DependencyEdge> CreateEdges(
        IReadOnlyList<SystemChain.Entry?> entries)
    {
        var systemIndex = new Dictionary<SystemId, List<int>>(entries.Count);
        var setIndex = new Dictionary<SystemSetLabel, List<int>>();

        for (var i = 0; i < entries.Count; i++) {
            if (entries[i] is not { } entry) {
                continue;
            }
            if (!systemIndex.TryGetValue(entry.Id, out var systems)) {
                systems = [];
                systemIndex.Add(entry.Id, systems);
            }
            systems.Add(i);

            foreach (var set in entry.Descriptor.MemberOf) {
                if (!setIndex.TryGetValue(set, out var members)) {
                    members = [];
                    setIndex.Add(set, members);
                }
                members.Add(i);
            }
        }

        var edges = new HashSet<DependencyEdge>();
        for (var i = 0; i < entries.Count; i++) {
            if (entries[i] is not { } entry) {
                continue;
            }
            foreach (var target in entry.Descriptor.RunsBefore) {
                AddTargetEdges(edges, i, target, systemIndex, setIndex, before: true);
            }
            foreach (var target in entry.Descriptor.RunsAfter) {
                AddTargetEdges(edges, i, target, systemIndex, setIndex, before: false);
            }
        }

        return edges;
    }

    private static void AddTargetEdges(
        HashSet<DependencyEdge> edges,
        int nodeIndex,
        SystemDependencyTarget target,
        IReadOnlyDictionary<SystemId, List<int>> systemIndex,
        IReadOnlyDictionary<SystemSetLabel, List<int>> setIndex,
        bool before)
    {
        var targets = target.Kind switch {
            SystemDependencyTargetKind.System
                => systemIndex.TryGetValue(target.System, out var systems) ? systems : null,
            SystemDependencyTargetKind.Set
                => setIndex.TryGetValue(target.Set, out var members) ? members : null,
            _ => null
        };
        if (targets is null) {
            return;
        }

        foreach (var targetIndex in targets) {
            if (targetIndex == nodeIndex) {
                continue;
            }
            edges.Add(before
                ? new DependencyEdge(nodeIndex, targetIndex)
                : new DependencyEdge(targetIndex, nodeIndex));
        }
    }
}
