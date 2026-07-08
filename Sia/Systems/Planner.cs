namespace Sia;

using System.Collections.Immutable;

public static class Planner
{
    private readonly record struct SystemEdge(int From, int To);

    public static ExecutionPlan Plan(IReadOnlyList<SystemChain.Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!HasConstraints(entries)) {
            return new ExecutionPlan([.. entries]);
        }

        var successors = CreateSuccessors(entries, out var inDegree);
        return new ExecutionPlan(TopologicalSort(entries, successors, inDegree));
    }

    private static bool HasConstraints(IReadOnlyList<SystemChain.Entry> entries)
    {
        for (var i = 0; i < entries.Count; i++) {
            var descriptor = entries[i].Descriptor;
            if (!descriptor.RunsBefore.IsEmpty || !descriptor.RunsAfter.IsEmpty) {
                return true;
            }
        }
        return false;
    }

    private static List<int>[] CreateSuccessors(
        IReadOnlyList<SystemChain.Entry> entries,
        out int[] inDegree)
    {
        var systemIndex = new Dictionary<SystemId, List<int>>(entries.Count);
        var setIndex = new Dictionary<SystemSetLabel, List<int>>();

        for (var i = 0; i < entries.Count; i++) {
            var entry = entries[i];
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

        var edges = new HashSet<SystemEdge>();
        for (var i = 0; i < entries.Count; i++) {
            foreach (var target in entries[i].Descriptor.RunsBefore) {
                AddTargetEdges(edges, i, target, systemIndex, setIndex, before: true);
            }
            foreach (var target in entries[i].Descriptor.RunsAfter) {
                AddTargetEdges(edges, i, target, systemIndex, setIndex, before: false);
            }
        }

        inDegree = new int[entries.Count];
        var successors = new List<int>[entries.Count];
        for (var i = 0; i < successors.Length; i++) {
            successors[i] = [];
        }
        foreach (var edge in edges) {
            successors[edge.From].Add(edge.To);
            inDegree[edge.To]++;
        }
        foreach (var list in successors) {
            list.Sort();
        }
        return successors;
    }

    private static ImmutableArray<SystemChain.Entry> TopologicalSort(
        IReadOnlyList<SystemChain.Entry> entries,
        IReadOnlyList<int>[] successors,
        int[] inDegree)
    {
        var ready = new PriorityQueue<int, int>(entries.Count);
        for (var i = 0; i < entries.Count; i++) {
            if (inDegree[i] == 0) {
                ready.Enqueue(i, i);
            }
        }

        var sorted = ImmutableArray.CreateBuilder<SystemChain.Entry>(entries.Count);
        while (ready.TryDequeue(out var current, out _)) {
            sorted.Add(entries[current]);
            foreach (var successor in successors[current]) {
                if (--inDegree[successor] == 0) {
                    ready.Enqueue(successor, successor);
                }
            }
        }

        if (sorted.Count != entries.Count) {
            throw new SystemCycleException(FindCycle(entries, successors));
        }
        return sorted.MoveToImmutable();
    }

    private static void AddTargetEdges(
        HashSet<SystemEdge> edges,
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
                ? new SystemEdge(nodeIndex, targetIndex)
                : new SystemEdge(targetIndex, nodeIndex));
        }
    }

    private static IReadOnlyList<SystemId> FindCycle(
        IReadOnlyList<SystemChain.Entry> entries,
        IReadOnlyList<int>[] successors)
    {
        var state = new byte[entries.Count];
        var path = new List<int>();
        var positions = new int[entries.Count];

        List<SystemId>? Visit(int node)
        {
            state[node] = 1;
            positions[node] = path.Count;
            path.Add(node);

            foreach (var successor in successors[node]) {
                if (state[successor] == 0) {
                    var cycle = Visit(successor);
                    if (cycle is not null) {
                        return cycle;
                    }
                }
                else if (state[successor] == 1) {
                    var cycle = new List<SystemId>(path.Count - positions[successor] + 1);
                    for (var i = positions[successor]; i < path.Count; i++) {
                        cycle.Add(entries[path[i]].Id);
                    }
                    cycle.Add(entries[successor].Id);
                    return cycle;
                }
            }

            path.RemoveAt(path.Count - 1);
            state[node] = 2;
            return null;
        }

        for (var i = 0; i < entries.Count; i++) {
            if (state[i] == 0 && Visit(i) is { } cycle) {
                return cycle;
            }
        }
        throw new InvalidOperationException("A cycle was expected but could not be located.");
    }
}
