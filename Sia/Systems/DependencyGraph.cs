using System.Buffers;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sia;

internal readonly record struct DependencyEdge(int From, int To);

internal readonly record struct DependencySortResult<TNode>(
    ImmutableArray<TNode> Order,
    ImmutableArray<TNode> Cycle)
{
    public bool HasCycle => !Cycle.IsEmpty;
}

internal static class DependencyGraph
{
    private const int StackWorkspaceLimit = 256;

    public static DependencySortResult<TNode> Sort<TNode>(
        IReadOnlyList<TNode> nodes,
        IReadOnlyCollection<DependencyEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        var nodeCount = nodes.Count;
        var edgeCount = edges.Count;
        if (edgeCount == 0) {
            if (nodeCount == 0) {
                return new([], []);
            }
            if (nodes is TNode[] array) {
                return new(ImmutableArray.Create<TNode>(array), []);
            }
            var orderedNodes = new TNode[nodeCount];
            for (var i = 0; i < nodeCount; i++) {
                orderedNodes[i] = nodes[i];
            }
            return new(ImmutableCollectionsMarshal.AsImmutableArray(orderedNodes), []);
        }
        var nodeSlots = checked(nodeCount * 2);
        var readyWords = (int)(((uint)nodeCount + 31) >> 5);
        var readySlots = readyWords + ((readyWords + 31) >> 5);
        var edgeOffset = checked(nodeSlots + readySlots);
        var workspaceLength = checked(edgeOffset + edgeCount * 2);
        int[]? rented = null;
        Span<int> workspace = workspaceLength <= StackWorkspaceLimit
            ? stackalloc int[workspaceLength]
            : (rented = ArrayPool<int>.Shared.Rent(workspaceLength)).AsSpan(0, workspaceLength);
        try {
            return SortCore(nodes, edges, workspace[..nodeCount], workspace.Slice(nodeCount, nodeCount),
                workspace.Slice(nodeSlots, readySlots), workspace.Slice(edgeOffset, edgeCount),
                workspace[(edgeOffset + edgeCount)..]);
        }
        finally {
            if (rented != null) {
                ArrayPool<int>.Shared.Return(rented);
            }
        }
    }

    private static DependencySortResult<TNode> SortCore<TNode>(
        IReadOnlyList<TNode> nodes, IReadOnlyCollection<DependencyEdge> edges,
        Span<int> heads, Span<int> inDegree, Span<int> ready, Span<int> targets, Span<int> links)
    {
        var nodeCount = heads.Length;
        heads.Fill(-1);
        inDegree.Clear();
        var edgeIndex = 0;
        foreach (var edge in edges) {
            ArgumentOutOfRangeException.ThrowIfNegative(edge.From);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(edge.From, nodeCount);
            ArgumentOutOfRangeException.ThrowIfNegative(edge.To);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(edge.To, nodeCount);
            targets[edgeIndex] = edge.To;
            links[edgeIndex] = heads[edge.From];
            heads[edge.From] = edgeIndex++;
            inDegree[edge.To]++;
        }

        var wordCount = (int)(((uint)nodeCount + 31) >> 5);
        var readyBits = ready[..wordCount];
        var readyGroups = ready.Slice(wordCount, (wordCount + 31) >> 5);
        readyBits.Clear();
        readyGroups.Clear();
        var firstGroup = readyGroups.Length;
        for (var i = 0; i < nodeCount; i++) {
            if (inDegree[i] == 0) {
                EnqueueReady(readyBits, readyGroups, ref firstGroup, i);
            }
        }

        var order = new TNode[nodeCount];
        var emitted = 0;
        int current;
        while ((current = DequeueReady(readyBits, readyGroups, ref firstGroup)) >= 0) {
            order[emitted++] = nodes[current];
            for (var edge = heads[current]; edge >= 0; edge = links[edge]) {
                var successor = targets[edge];
                if (--inDegree[successor] == 0) {
                    EnqueueReady(readyBits, readyGroups, ref firstGroup, successor);
                }
            }
        }

        return emitted == nodeCount
            ? new(ImmutableCollectionsMarshal.AsImmutableArray(order), [])
            : new([], FindCycle(nodes, MaterializeSuccessors(heads, targets, links)));
    }

    private static void EnqueueReady(Span<int> bits, Span<int> groups, ref int firstGroup, int node)
    {
        var word = node >> 5;
        var group = word >> 5;
        bits[word] |= 1 << (node & 31);
        groups[group] |= 1 << (word & 31);
        firstGroup = Math.Min(firstGroup, group);
    }

    private static int DequeueReady(Span<int> bits, Span<int> groups, ref int firstGroup)
    {
        for (; firstGroup < groups.Length; firstGroup++) {
            var groupBits = (uint)groups[firstGroup];
            if (groupBits == 0) {
                continue;
            }
            var word = (firstGroup << 5) + BitOperations.TrailingZeroCount(groupBits);
            var wordBits = (uint)bits[word];
            var remaining = wordBits & (wordBits - 1);
            bits[word] = (int)remaining;
            if (remaining == 0) {
                groups[firstGroup] = (int)(groupBits & (groupBits - 1));
            }
            return (word << 5) + BitOperations.TrailingZeroCount(wordBits);
        }
        return -1;
    }

    private static List<int>?[] MaterializeSuccessors(
        ReadOnlySpan<int> heads, ReadOnlySpan<int> targets, ReadOnlySpan<int> links)
    {
        var successors = new List<int>?[heads.Length];
        for (var node = 0; node < heads.Length; node++) {
            for (var edge = heads[node]; edge >= 0; edge = links[edge]) {
                (successors[node] ??= []).Add(targets[edge]);
            }
        }
        return successors;
    }

    private static ImmutableArray<TNode> FindCycle<TNode>(
        IReadOnlyList<TNode> nodes,
        List<int>?[] successors)
    {
        foreach (var outgoing in successors) {
            outgoing?.Sort();
        }
        var state = new byte[successors.Length];
        var path = new List<int>();
        var positions = new int[successors.Length];

        ImmutableArray<TNode> Visit(int node)
        {
            state[node] = 1;
            positions[node] = path.Count;
            path.Add(node);

            if (successors[node] is { } outgoing) {
                foreach (var successor in outgoing) {
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
