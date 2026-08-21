using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public static partial class NotebookCellLayout
{
    public static CellTabGroup? FindGroupContaining(
        NotebookCellState state,
        string tabId)
        => EnumerateGroups(state).FirstOrDefault(group => group.TabIds.Contains(tabId));

    public static CellTabGroup? FindGroup(NotebookCellState state, string groupId)
        => EnumerateGroups(state).FirstOrDefault(group => group.Id == groupId);

    public static bool CanPlaceTab(
        NotebookCellState state,
        string tabId,
        string targetId)
    {
        if (!state.Tabs.TryGetValue(tabId, out var tab)
            || !state.Windows.TryGetValue(tab.WindowId, out var sourceWindow)) {
            return false;
        }
        var target = FindGroup(state, targetId);
        if (target is not null) {
            return target.TabIds.All(targetTabId =>
                state.Tabs.TryGetValue(targetTabId, out var targetTab)
                && state.Windows.TryGetValue(targetTab.WindowId, out var targetWindow)
                && targetWindow.CellId == sourceWindow.CellId);
        }
        var regionIndex = FindRegionIndex(state.Regions, targetId);
        return regionIndex >= 0
            && state.Regions[regionIndex].Root is null
            && sourceWindow.HomeRegionId == targetId;
    }

    public static bool CanCloseTab(NotebookCellState state, string tabId)
    {
        if (!state.Tabs.TryGetValue(tabId, out var tab)
            || !state.Windows.TryGetValue(tab.WindowId, out var window)) {
            return false;
        }
        return window.Kind != CellWindowKind.Script
            || state.Windows.Values.Count(candidate =>
                candidate.CellId == window.CellId
                && candidate.Kind == CellWindowKind.Script) > 1;
    }

    public static string? GetScriptGroupCellId(
        NotebookCellState state,
        CellTabGroup group)
    {
        string? cellId = null;
        foreach (var tabId in group.TabIds) {
            if (!state.Tabs.TryGetValue(tabId, out var tab)
                || !state.Windows.TryGetValue(tab.WindowId, out var window)
                || window.Kind != CellWindowKind.Script
                || (cellId is not null && cellId != window.CellId)) {
                return null;
            }
            cellId = window.CellId;
        }
        return cellId;
    }

    public static IEnumerable<CellTabGroup> EnumerateGroups(NotebookCellState state)
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

    public static IEnumerable<CellSplit> EnumerateSplits(NotebookCellState state)
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

    public static CellSplit? FindSurfaceSplit(NotebookCellState state, string cellId)
    {
        var script = FindGroupForCell(state, cellId, CellWindowKind.Script);
        var surface = FindSurfaceGroupForCell(state, cellId);
        if (script is null || surface is null || script.Id == surface.Id) {
            return null;
        }
        return EnumerateSplits(state).FirstOrDefault(split =>
            split.Axis == CellAxis.Vertical
                && ((split.First is CellTabGroup first
                        && first.Id == surface.Id
                        && ContainsGroup(split.Second, script.Id))
                    || (split.Second is CellTabGroup second
                        && second.Id == surface.Id
                        && ContainsGroup(split.First, script.Id))));
    }

    public static (CellSplit Split, int SurfaceIndex)? FindSurfacePlacement(
        NotebookCellState state,
        string cellId)
    {
        var split = FindSurfaceSplit(state, cellId);
        var surface = FindSurfaceGroupForCell(state, cellId);
        if (split is null || surface is null) {
            return null;
        }
        var surfaceIndex = split.Second is CellTabGroup second && second.Id == surface.Id
            ? 1
            : 0;
        return (split, surfaceIndex);
    }

    public static bool IsValid(NotebookCellState state)
    {
        if (state.NextNodeId < 0
            || state.Revision < 0
            || state.Regions.Select(static region => region.Id).Distinct(StringComparer.Ordinal).Count()
                != state.Regions.Length
            || state.FloatingHosts.Select(static host => host.Id).Distinct(StringComparer.Ordinal).Count()
                != state.FloatingHosts.Length
            || state.FloatingHosts.Any(static host => host.Width <= 0 || host.Height <= 0)) {
            return false;
        }

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
            if (region.Root is not null
                && EnumerateTabIds(region.Root).Any(tabId =>
                    state.Windows[state.Tabs[tabId].WindowId].HomeRegionId != region.Id)) {
                return false;
            }
        }
        foreach (var floating in state.FloatingHosts) {
            if (!ValidateNode(floating.Root, state, nodeIds, placedTabs)) {
                return false;
            }
            if (EnumerateTabIds(floating.Root)
                .Select(tabId => state.Windows[state.Tabs[tabId].WindowId].CellId)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any()) {
                return false;
            }
        }
        var tabWindowIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, tab) in state.Tabs) {
            if (key != tab.Id
                || !state.Windows.ContainsKey(tab.WindowId)
                || !tabWindowIds.Add(tab.WindowId)) {
                return false;
            }
        }
        if (tabWindowIds.Count != state.Windows.Count) {
            return false;
        }

        var regionIds = state.Regions.Select(static region => region.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, window) in state.Windows) {
            if (key != window.Id || !regionIds.Contains(window.HomeRegionId)) {
                return false;
            }
            if (window.Kind == CellWindowKind.Script
                && !placedTabs.Contains(state.GetTabForWindow(window.Id).Id)) {
                return false;
            }
        }
        return true;
    }

    private static CellTabGroup? FindGroupForCell(
        NotebookCellState state,
        string cellId,
        CellWindowKind kind)
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

    private static CellTabGroup? FindSurfaceGroupForCell(
        NotebookCellState state,
        string cellId)
        => FindGroupForCell(state, cellId, CellWindowKind.Output)
            ?? FindGroupForCell(state, cellId, CellWindowKind.Render);

    private static bool GroupContainsKind(
        NotebookCellState state,
        CellTabGroup group,
        CellWindowKind kind)
        => group.TabIds.Any(tabId => state.Windows[state.Tabs[tabId].WindowId].Kind == kind);

    private static bool ContainsGroup(CellNode node, string groupId)
        => node is CellTabGroup group
            ? group.Id == groupId
            : ContainsGroup(((CellSplit)node).First, groupId)
                || ContainsGroup(((CellSplit)node).Second, groupId);

    private static IEnumerable<CellTabGroup> EnumerateGroups(CellNode node)
    {
        if (node is CellTabGroup group) {
            yield return group;
            yield break;
        }
        var split = (CellSplit)node;
        foreach (var childGroup in EnumerateGroups(split.First)) {
            yield return childGroup;
        }
        foreach (var childGroup in EnumerateGroups(split.Second)) {
            yield return childGroup;
        }
    }

    private static IEnumerable<CellSplit> EnumerateSplits(CellNode node)
    {
        if (node is CellTabGroup) {
            yield break;
        }
        var split = (CellSplit)node;
        yield return split;
        foreach (var nested in EnumerateSplits(split.First)) {
            yield return nested;
        }
        foreach (var nested in EnumerateSplits(split.Second)) {
            yield return nested;
        }
    }

    private static IEnumerable<string> EnumerateTabIds(CellNode node)
        => EnumerateGroups(node).SelectMany(static group => group.TabIds);

    private static int FindRegionIndex(
        ImmutableArray<CellRegion> regions,
        string regionId)
    {
        for (var index = 0; index < regions.Length; index++) {
            if (regions[index].Id == regionId) {
                return index;
            }
        }
        return -1;
    }

    private static bool Contains(CellNode node, string tabId)
        => node is CellTabGroup group
            ? group.TabIds.Contains(tabId)
            : Contains(((CellSplit)node).First, tabId)
                || Contains(((CellSplit)node).Second, tabId);

    private static bool ValidateNode(
        CellNode node,
        NotebookCellState state,
        ISet<string> nodeIds,
        ISet<string> placedTabs)
    {
        if (!nodeIds.Add(node.Id)) {
            return false;
        }
        if (node is CellTabGroup group) {
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

        var split = (CellSplit)node;
        return split.Ratio is >= 0 and <= 1
            && ValidateNode(split.First, state, nodeIds, placedTabs)
            && ValidateNode(split.Second, state, nodeIds, placedTabs);
    }
}
