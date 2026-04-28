namespace Sia;

internal readonly record struct SystemNode(
    int Index,
    SystemChain.Entry Entry,
    SystemDescriptor Descriptor);

internal readonly record struct SystemEdge(int From, int To);

public sealed class SystemGraph(SystemChain chain)
{
    private readonly SystemChain _chain = chain;

    public IEnumerable<ISystem> BuildSortedSystems()
    {
        var nodes = CreateNodes(_chain.Entries);
        var edges = CreateEdges(nodes);
        var sorted = TopologicalSort(nodes, edges);
        var systems = new ISystem[sorted.Length];
        for (var i = 0; i < sorted.Length; i++) {
            systems[i] = sorted[i].Entry.Creator();
        }
        return systems;
    }

    public SystemStage CreateStage(World world)
        => new(world, BuildSortedSystems());

    private static SystemNode[] CreateNodes(
        IReadOnlyList<SystemChain.Entry> entries)
    {
        var nodes = new SystemNode[entries.Count];
        for (var i = 0; i < entries.Count; i++) {
            var entry = entries[i];
            nodes[i] = new SystemNode(
                i,
                entry,
                entry.Descriptor);
        }
        return nodes;
    }

    private static SystemEdge[] CreateEdges(SystemNode[] nodes)
    {
        var systemIndex = new Dictionary<SystemId, List<int>>(nodes.Length);
        var setIndex = new Dictionary<SystemSetLabel, List<int>>();

        foreach (var node in nodes) {
            if (!systemIndex.TryGetValue(node.Entry.Id, out var systemNodes)) {
                systemNodes = [];
                systemIndex[node.Entry.Id] = systemNodes;
            }
            systemNodes.Add(node.Index);

            foreach (var set in node.Descriptor.MemberOf) {
                if (!setIndex.TryGetValue(set, out var setNodes)) {
                    setNodes = [];
                    setIndex[set] = setNodes;
                }
                setNodes.Add(node.Index);
            }
        }

        var edges = new HashSet<SystemEdge>();
        foreach (var node in nodes) {
            foreach (var target in node.Descriptor.RunsBefore) {
                AddTargetEdges(edges, node.Index, target, systemIndex, setIndex, before: true);
            }
            foreach (var target in node.Descriptor.RunsAfter) {
                AddTargetEdges(edges, node.Index, target, systemIndex, setIndex, before: false);
            }
        }
        return [.. edges];
    }

    private static SystemNode[] TopologicalSort(SystemNode[] nodes, SystemEdge[] edges)
    {
        var successors = new List<int>[nodes.Length];
        var inDegree = new int[nodes.Length];
        for (var i = 0; i < successors.Length; i++) {
            successors[i] = [];
        }

        foreach (var edge in edges) {
            successors[edge.From].Add(edge.To);
            inDegree[edge.To]++;
        }

        var queue = new Queue<int>(nodes.Length);
        foreach (var node in nodes) {
            if (inDegree[node.Index] == 0) {
                queue.Enqueue(node.Index);
            }
        }

        var sorted = new SystemNode[nodes.Length];
        var count = 0;
        while (queue.Count > 0) {
            var currentIndex = queue.Dequeue();
            sorted[count++] = nodes[currentIndex];
            foreach (var successorIndex in successors[currentIndex]) {
                if (--inDegree[successorIndex] == 0) {
                    queue.Enqueue(successorIndex);
                }
            }
        }

        if (count != nodes.Length) {
            var cycle = FindCycle(nodes, successors);
            throw new SystemCycleException(cycle);
        }

        return sorted;
    }

    private static void AddTargetEdges(
        HashSet<SystemEdge> edges,
        int nodeIndex,
        SystemDependencyTarget target,
        IReadOnlyDictionary<SystemId, List<int>> systemIndex,
        IReadOnlyDictionary<SystemSetLabel, List<int>> setIndex,
        bool before)
    {
        var targets = ResolveTargets(target, systemIndex, setIndex);
        if (targets == null) {
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

    private static List<int>? ResolveTargets(
        SystemDependencyTarget target,
        IReadOnlyDictionary<SystemId, List<int>> systemIndex,
        IReadOnlyDictionary<SystemSetLabel, List<int>> setIndex)
        => target.Kind switch {
            SystemDependencyTargetKind.System
                => systemIndex.TryGetValue(target.System, out var systemNodes)
                        ? systemNodes
                        : null,
            SystemDependencyTargetKind.Set
                => setIndex.TryGetValue(target.Set, out var setNodes)
                    ? setNodes
                    : null,
            _ => null
        };

    private static List<Type> FindCycle(
        SystemNode[] nodes,
        IReadOnlyList<int>[] successors)
    {
        var visited = new HashSet<int>();
        var stack = new HashSet<int>();
        var path = new List<Type>();

        bool Dfs(int nodeIndex)
        {
            visited.Add(nodeIndex);
            stack.Add(nodeIndex);
            path.Add(nodes[nodeIndex].Entry.Type);

            foreach (var successor in successors[nodeIndex]) {
                if (!visited.Contains(successor)) {
                    if (Dfs(successor)) return true;
                }
                else if (stack.Contains(successor)) {
                    path.Add(nodes[successor].Entry.Type);
                    return true;
                }
            }

            stack.Remove(nodeIndex);
            path.RemoveAt(path.Count - 1);
            return false;
        }

        foreach (var node in nodes) {
            if (!visited.Contains(node.Index) && Dfs(node.Index))
                return path;
        }

        var fallback = new List<Type>(nodes.Length);
        foreach (var node in nodes) {
            fallback.Add(node.Entry.Type);
        }
        return fallback;
    }
}
