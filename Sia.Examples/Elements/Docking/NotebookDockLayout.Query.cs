using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public static partial class NotebookDockLayout
{
    public static DockTabGroup? FindGroupContaining(
        NotebookDockState state,
        string tabId)
        => EnumerateGroups(state).FirstOrDefault(group => group.TabIds.Contains(tabId));

    public static DockTabGroup? FindGroup(NotebookDockState state, string groupId)
        => EnumerateGroups(state).FirstOrDefault(group => group.Id == groupId);

    public static IEnumerable<DockTabGroup> EnumerateGroups(NotebookDockState state)
    {
        foreach (var region in state.Regions) {
            if (region.Root is not null) {
                foreach (var group in EnumerateGroups(region.Root)) {
                    yield return group;
                }
            }
        }
        foreach (var floating in state.FloatingHosts) {
            foreach (var group in EnumerateGroups(floating.Root)) {
                yield return group;
            }
        }
    }

    public static IEnumerable<DockSplit> EnumerateSplits(NotebookDockState state)
    {
        foreach (var region in state.Regions) {
            if (region.Root is not null) {
                foreach (var split in EnumerateSplits(region.Root)) {
                    yield return split;
                }
            }
        }
        foreach (var floating in state.FloatingHosts) {
            foreach (var split in EnumerateSplits(floating.Root)) {
                yield return split;
            }
        }
    }

    public static DockSplit? FindSurfaceSplit(NotebookDockState state, string cellId)
    {
        var script = FindGroupForCell(state, cellId, DockWindowKind.Script);
        var surface = FindSurfaceGroupForCell(state, cellId);
        if (script is null || surface is null || script.Id == surface.Id) {
            return null;
        }
        return EnumerateSplits(state).FirstOrDefault(split =>
            split.First is DockTabGroup first && split.Second is DockTabGroup second
                && ((first.Id == script.Id && second.Id == surface.Id)
                    || (first.Id == surface.Id && second.Id == script.Id)));
    }

    public static (DockSplit Split, int SurfaceIndex)? FindSurfacePlacement(
        NotebookDockState state,
        string cellId)
    {
        var split = FindSurfaceSplit(state, cellId);
        var surface = FindSurfaceGroupForCell(state, cellId);
        if (split is null || surface is null) {
            return null;
        }
        var surfaceIndex = split.Second is DockTabGroup second && second.Id == surface.Id
            ? 1
            : 0;
        return (split, surfaceIndex);
    }

    public static bool IsValid(NotebookDockState state)
    {
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var placedTabs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in EnumerateGroups(state)) {
            if (group.TabIds.IsDefaultOrEmpty
                || !group.TabIds.Contains(group.ActiveTabId)) {
                return false;
            }
        }
        foreach (var region in state.Regions) {
            if (region.Root is not null
                && !ValidateNode(region.Root, state, nodeIds, placedTabs)) {
                return false;
            }
        }
        foreach (var floating in state.FloatingHosts) {
            if (!ValidateNode(floating.Root, state, nodeIds, placedTabs)) {
                return false;
            }
        }
        return state.Windows.All(pair => pair.Key == pair.Value.Id)
            && state.Tabs.All(pair => pair.Key == pair.Value.Id
                && state.Windows.ContainsKey(pair.Value.WindowId));
    }

    private static DockTabGroup? FindGroupForCell(
        NotebookDockState state,
        string cellId,
        DockWindowKind kind)
    {
        foreach (var group in EnumerateGroups(state)) {
            foreach (var tabId in group.TabIds) {
                var window = state.Windows[state.Tabs[tabId].WindowId];
                if (window.CellId == cellId && window.Kind == kind) {
                    return group;
                }
            }
        }
        return null;
    }

    private static DockTabGroup? FindSurfaceGroupForCell(
        NotebookDockState state,
        string cellId)
        => FindGroupForCell(state, cellId, DockWindowKind.Output)
            ?? FindGroupForCell(state, cellId, DockWindowKind.Render);

    private static IEnumerable<DockTabGroup> EnumerateGroups(DockNode node)
    {
        if (node is DockTabGroup group) {
            yield return group;
            yield break;
        }
        var split = (DockSplit)node;
        foreach (var childGroup in EnumerateGroups(split.First)) {
            yield return childGroup;
        }
        foreach (var childGroup in EnumerateGroups(split.Second)) {
            yield return childGroup;
        }
    }

    private static IEnumerable<DockSplit> EnumerateSplits(DockNode node)
    {
        if (node is DockTabGroup) {
            yield break;
        }
        var split = (DockSplit)node;
        yield return split;
        foreach (var nested in EnumerateSplits(split.First)) {
            yield return nested;
        }
        foreach (var nested in EnumerateSplits(split.Second)) {
            yield return nested;
        }
    }

    private static int FindRegionIndex(
        ImmutableArray<DockRegion> regions,
        string regionId)
    {
        for (var index = 0; index < regions.Length; index++) {
            if (regions[index].Id == regionId) {
                return index;
            }
        }
        return -1;
    }

    private static bool Contains(DockNode node, string tabId)
        => node is DockTabGroup group
            ? group.TabIds.Contains(tabId)
            : Contains(((DockSplit)node).First, tabId)
                || Contains(((DockSplit)node).Second, tabId);

    private static bool ValidateNode(
        DockNode node,
        NotebookDockState state,
        ISet<string> nodeIds,
        ISet<string> placedTabs)
    {
        if (!nodeIds.Add(node.Id)) {
            return false;
        }
        if (node is DockTabGroup group) {
            if (group.TabIds.IsDefaultOrEmpty
                || !group.TabIds.Contains(group.ActiveTabId)) {
                return false;
            }
            foreach (var tabId in group.TabIds) {
                if (!state.Tabs.ContainsKey(tabId) || !placedTabs.Add(tabId)) {
                    return false;
                }
            }
            return true;
        }

        var split = (DockSplit)node;
        return split.Ratio is >= 0 and <= 1
            && ValidateNode(split.First, state, nodeIds, placedTabs)
            && ValidateNode(split.Second, state, nodeIds, placedTabs);
    }
}
