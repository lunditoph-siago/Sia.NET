namespace Sia;

using System.Collections.Immutable;

internal readonly record struct DependencyEdge(int From, int To);

internal readonly record struct DependencySortResult<TNode>(
    ImmutableArray<TNode> Order,
    ImmutableArray<TNode> Cycle)
{
    public bool HasCycle => !Cycle.IsEmpty;
}

internal static class DependencyGraph
{
    public static DependencySortResult<TNode> Sort<TNode>(
        IReadOnlyList<TNode> nodes,
        IReadOnlyCollection<DependencyEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        var nodeCount = nodes.Count;
        var successors = new List<int>[nodeCount];
        var inDegree = new int[nodeCount];
        for (var i = 0; i < nodeCount; i++) {
            successors[i] = [];
        }

        foreach (var edge in edges) {
            ArgumentOutOfRangeException.ThrowIfNegative(edge.From);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(edge.From, nodeCount);
            ArgumentOutOfRangeException.ThrowIfNegative(edge.To);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(edge.To, nodeCount);
            successors[edge.From].Add(edge.To);
            inDegree[edge.To]++;
        }
        foreach (var list in successors) {
            list.Sort();
        }

        var ready = new PriorityQueue<int, int>(nodeCount);
        for (var i = 0; i < nodeCount; i++) {
            if (inDegree[i] == 0) {
                ready.Enqueue(i, i);
            }
        }

        var order = ImmutableArray.CreateBuilder<TNode>(nodeCount);
        while (ready.TryDequeue(out var current, out _)) {
            order.Add(nodes[current]);
            foreach (var successor in successors[current]) {
                if (--inDegree[successor] == 0) {
                    ready.Enqueue(successor, successor);
                }
            }
        }

        return order.Count == nodeCount
            ? new(order.MoveToImmutable(), [])
            : new([], FindCycle(nodes, successors));
    }

    private static ImmutableArray<TNode> FindCycle<TNode>(
        IReadOnlyList<TNode> nodes,
        IReadOnlyList<int>[] successors)
    {
        var state = new byte[successors.Length];
        var path = new List<int>();
        var positions = new int[successors.Length];

        ImmutableArray<TNode> Visit(int node)
        {
            state[node] = 1;
            positions[node] = path.Count;
            path.Add(node);

            foreach (var successor in successors[node]) {
                if (state[successor] == 0) {
                    var cycle = Visit(successor);
                    if (!cycle.IsEmpty) {
                        return cycle;
                    }
                }
                else if (state[successor] == 1) {
                    var cycle = ImmutableArray.CreateBuilder<TNode>(
                        path.Count - positions[successor] + 1);
                    for (var i = positions[successor]; i < path.Count; i++) {
                        cycle.Add(nodes[path[i]]);
                    }
                    cycle.Add(nodes[successor]);
                    return cycle.MoveToImmutable();
                }
            }

            path.RemoveAt(path.Count - 1);
            state[node] = 2;
            return [];
        }

        for (var i = 0; i < successors.Length; i++) {
            if (state[i] == 0 && Visit(i) is { IsEmpty: false } cycle) {
                return cycle;
            }
        }
        throw new InvalidOperationException("A cycle was expected but could not be located.");
    }
}
