using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Sia;

public static class Planner
{
    public static ExecutionPlan Plan(IReadOnlyList<SystemChain.Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries is ImmutableList<SystemChain.Entry> immutableEntries
            && !HasConstraints(immutableEntries)) {
            return new ExecutionPlan(immutableEntries.ToImmutableArray());
        }
        var order = PlanOrder(entries);
        if (order.IsEmpty) {
            return new ExecutionPlan(ImmutableArray<SystemChain.Entry>.Empty);
        }
        var planned = new SystemChain.Entry[order.Length];
        for (var i = 0; i < planned.Length; i++) {
            planned[i] = entries[order[i]];
        }
        return new ExecutionPlan(ImmutableCollectionsMarshal.AsImmutableArray(planned));
    }

    private static bool HasConstraints(ImmutableList<SystemChain.Entry> entries)
    {
        foreach (var entry in entries) {
            if (!entry.Descriptor.RunsBefore.IsEmpty || !entry.Descriptor.RunsAfter.IsEmpty) {
                return true;
            }
        }
        return false;
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
        var systemIndex = new Dictionary<SystemId, int>(entries.Count);
        var setIndex = new Dictionary<SystemSetLabel, int>();
        List<List<int>>? duplicateGroups = null;

        for (var i = 0; i < entries.Count; i++) {
            if (entries[i] is not { } entry) {
                continue;
            }
            IndexEntry(systemIndex, entry.Id, i, ref duplicateGroups);
            foreach (var set in entry.Descriptor.MemberOf) {
                IndexEntry(setIndex, set, i, ref duplicateGroups);
            }
        }

        var edges = new HashSet<DependencyEdge>();
        for (var i = 0; i < entries.Count; i++) {
            if (entries[i] is not { } entry) {
                continue;
            }
            foreach (var target in entry.Descriptor.RunsBefore) {
                AddTargetEdges(edges, i, target, systemIndex, setIndex, duplicateGroups, before: true);
            }
            foreach (var target in entry.Descriptor.RunsAfter) {
                AddTargetEdges(edges, i, target, systemIndex, setIndex, duplicateGroups, before: false);
            }
        }

        return edges;
    }

    private static void IndexEntry<TKey>(
        Dictionary<TKey, int> index, TKey key, int entryIndex, ref List<List<int>>? duplicateGroups)
        where TKey : notnull
    {
        ref var registration = ref CollectionsMarshal.GetValueRefOrAddDefault(index, key, out var exists);
        if (!exists) {
            registration = entryIndex;
        }
        else if (registration >= 0) {
            // Nonnegative values are entry indices. Negative values encode a slot in duplicateGroups.
            var groups = duplicateGroups ??= [];
            var groupIndex = groups.Count;
            groups.Add([registration, entryIndex]);
            registration = ~groupIndex;
        }
        else {
            duplicateGroups![~registration].Add(entryIndex);
        }
    }

    private static void AddTargetEdges(
        HashSet<DependencyEdge> edges,
        int nodeIndex,
        SystemDependencyTarget target,
        Dictionary<SystemId, int> systemIndex,
        Dictionary<SystemSetLabel, int> setIndex,
        List<List<int>>? duplicateGroups,
        bool before)
    {
        var registration = 0;
        var found = target.Kind switch {
            SystemDependencyTargetKind.System => systemIndex.TryGetValue(target.System, out registration),
            SystemDependencyTargetKind.Set => setIndex.TryGetValue(target.Set, out registration),
            _ => false
        };
        if (!found) {
            return;
        }
        if (registration >= 0) {
            AddEdge(edges, nodeIndex, registration, before);
            return;
        }
        foreach (var targetIndex in CollectionsMarshal.AsSpan(duplicateGroups![~registration])) {
            AddEdge(edges, nodeIndex, targetIndex, before);
        }
    }

    private static void AddEdge(HashSet<DependencyEdge> edges, int nodeIndex, int targetIndex, bool before)
    {
        if (targetIndex != nodeIndex) {
            edges.Add(before
                ? new DependencyEdge(nodeIndex, targetIndex)
                : new DependencyEdge(targetIndex, nodeIndex));
        }
    }
}
