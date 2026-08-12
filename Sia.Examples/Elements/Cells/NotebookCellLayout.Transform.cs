using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public static partial class NotebookCellLayout
{
    private static NotebookCellState CellToEmptyRegion(
        NotebookCellState state,
        string tabId,
        string regionId)
    {
        var regionIndex = FindRegionIndex(state.Regions, regionId);
        if (regionIndex < 0 || state.Regions[regionIndex].Root is not null) {
            return state;
        }

        var withoutSource = RemoveTab(state, tabId, out var removed);
        if (!removed) {
            return state;
        }
        regionIndex = FindRegionIndex(withoutSource.Regions, regionId);
        if (regionIndex < 0 || withoutSource.Regions[regionIndex].Root is not null) {
            return state;
        }

        var group = new CellTabGroup(
            $"group-{withoutSource.NextNodeId}",
            [tabId],
            tabId);
        return withoutSource with {
            Regions = withoutSource.Regions.SetItem(
                regionIndex,
                withoutSource.Regions[regionIndex] with { Root = group }),
            NextNodeId = withoutSource.NextNodeId + 1,
        };
    }

    private static NotebookCellState Reorder(
        NotebookCellState state,
        CellTabGroup group,
        string tabId,
        int targetIndex)
    {
        var sourceIndex = group.TabIds.IndexOf(tabId);
        if (sourceIndex < 0) {
            return state;
        }
        var tabs = group.TabIds.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex) {
            targetIndex--;
        }
        var insertion = Math.Clamp(targetIndex, 0, tabs.Length);
        tabs = tabs.Insert(insertion, tabId);
        return UpdateGroup(
            state,
            group.Id,
            current => current with { TabIds = tabs, ActiveTabId = tabId });
    }

    private static NotebookCellState UpdateGroupContaining(
        NotebookCellState state,
        string tabId,
        Func<CellTabGroup, CellTabGroup> update)
        => TransformRoots(
            state,
            root => UpdateGroupContaining(root, tabId, update));

    private static NotebookCellState UpdateGroup(
        NotebookCellState state,
        string groupId,
        Func<CellTabGroup, CellTabGroup> update)
        => TransformRoots(state, root => UpdateGroup(root, groupId, update));

    private static NotebookCellState ReplaceGroup(
        NotebookCellState state,
        string groupId,
        CellNode replacement)
        => TransformRoots(state, root => ReplaceGroup(root, groupId, replacement));

    private static NotebookCellState TransformRoots(
        NotebookCellState state,
        Func<CellNode, CellNode> transform)
        => state with {
            Regions = state.Regions
                .Select(region => region.Root is null
                    ? region
                    : region with { Root = transform(region.Root) })
                .ToImmutableArray(),
            FloatingHosts = state.FloatingHosts
                .Select(floating => floating with { Root = transform(floating.Root) })
                .ToImmutableArray(),
        };

    private static CellNode UpdateGroupContaining(
        CellNode node,
        string tabId,
        Func<CellTabGroup, CellTabGroup> update)
    {
        if (node is CellTabGroup group) {
            return group.TabIds.Contains(tabId) ? update(group) : group;
        }
        var split = (CellSplit)node;
        return split with {
            First = UpdateGroupContaining(split.First, tabId, update),
            Second = UpdateGroupContaining(split.Second, tabId, update),
        };
    }

    private static CellNode UpdateGroup(
        CellNode node,
        string groupId,
        Func<CellTabGroup, CellTabGroup> update)
    {
        if (node is CellTabGroup group) {
            return group.Id == groupId ? update(group) : group;
        }
        var split = (CellSplit)node;
        return split with {
            First = UpdateGroup(split.First, groupId, update),
            Second = UpdateGroup(split.Second, groupId, update),
        };
    }

    private static CellNode ReplaceGroup(
        CellNode node,
        string groupId,
        CellNode replacement)
    {
        if (node is CellTabGroup group) {
            return group.Id == groupId ? replacement : group;
        }
        var split = (CellSplit)node;
        return split with {
            First = ReplaceGroup(split.First, groupId, replacement),
            Second = ReplaceGroup(split.Second, groupId, replacement),
        };
    }

    private static NotebookCellState RemoveTab(
        NotebookCellState state,
        string tabId,
        out bool removed)
    {
        removed = false;
        var regions = state.Regions.ToBuilder();
        for (var index = 0; index < regions.Count; index++) {
            var root = regions[index].Root;
            if (root is null || !Contains(root, tabId)) {
                continue;
            }
            regions[index] = regions[index] with { Root = RemoveTab(root, tabId) };
            removed = true;
            break;
        }

        var floating = state.FloatingHosts.ToBuilder();
        if (!removed) {
            for (var index = 0; index < floating.Count; index++) {
                if (!Contains(floating[index].Root, tabId)) {
                    continue;
                }
                var root = RemoveTab(floating[index].Root, tabId);
                if (root is null) {
                    floating.RemoveAt(index);
                } else {
                    floating[index] = floating[index] with { Root = root };
                }
                removed = true;
                break;
            }
        }

        return state with {
            Regions = regions.ToImmutable(),
            FloatingHosts = floating.ToImmutable(),
        };
    }

    private static CellNode? RemoveTab(CellNode node, string tabId)
    {
        if (node is CellTabGroup group) {
            if (!group.TabIds.Contains(tabId)) {
                return group;
            }
            var tabs = group.TabIds.Remove(tabId);
            if (tabs.Length == 0) {
                return null;
            }
            return group with {
                TabIds = tabs,
                ActiveTabId = group.ActiveTabId == tabId ? tabs[0] : group.ActiveTabId,
            };
        }

        var split = (CellSplit)node;
        var first = RemoveTab(split.First, tabId);
        var second = RemoveTab(split.Second, tabId);
        return (first, second) switch {
            (null, null) => null,
            (null, { } remaining) => remaining,
            ( { } remaining, null) => remaining,
            ( { } left, { } right) => split with { First = left, Second = right },
        };
    }
}
