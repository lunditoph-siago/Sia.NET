using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public static partial class NotebookDockLayout
{
    private static NotebookDockState DockToEmptyRegion(
        NotebookDockState state,
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

        var group = new DockTabGroup(
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

    private static NotebookDockState Reorder(
        NotebookDockState state,
        DockTabGroup group,
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

    private static NotebookDockState UpdateGroupContaining(
        NotebookDockState state,
        string tabId,
        Func<DockTabGroup, DockTabGroup> update)
        => TransformRoots(
            state,
            root => UpdateGroupContaining(root, tabId, update));

    private static NotebookDockState UpdateGroup(
        NotebookDockState state,
        string groupId,
        Func<DockTabGroup, DockTabGroup> update)
        => TransformRoots(state, root => UpdateGroup(root, groupId, update));

    private static NotebookDockState ReplaceGroup(
        NotebookDockState state,
        string groupId,
        DockNode replacement)
        => TransformRoots(state, root => ReplaceGroup(root, groupId, replacement));

    private static NotebookDockState TransformRoots(
        NotebookDockState state,
        Func<DockNode, DockNode> transform)
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

    private static DockNode UpdateGroupContaining(
        DockNode node,
        string tabId,
        Func<DockTabGroup, DockTabGroup> update)
    {
        if (node is DockTabGroup group) {
            return group.TabIds.Contains(tabId) ? update(group) : group;
        }
        var split = (DockSplit)node;
        return split with {
            First = UpdateGroupContaining(split.First, tabId, update),
            Second = UpdateGroupContaining(split.Second, tabId, update),
        };
    }

    private static DockNode UpdateGroup(
        DockNode node,
        string groupId,
        Func<DockTabGroup, DockTabGroup> update)
    {
        if (node is DockTabGroup group) {
            return group.Id == groupId ? update(group) : group;
        }
        var split = (DockSplit)node;
        return split with {
            First = UpdateGroup(split.First, groupId, update),
            Second = UpdateGroup(split.Second, groupId, update),
        };
    }

    private static DockNode ReplaceGroup(
        DockNode node,
        string groupId,
        DockNode replacement)
    {
        if (node is DockTabGroup group) {
            return group.Id == groupId ? replacement : group;
        }
        var split = (DockSplit)node;
        return split with {
            First = ReplaceGroup(split.First, groupId, replacement),
            Second = ReplaceGroup(split.Second, groupId, replacement),
        };
    }

    private static NotebookDockState RemoveTab(
        NotebookDockState state,
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

    private static DockNode? RemoveTab(DockNode node, string tabId)
    {
        if (node is DockTabGroup group) {
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

        var split = (DockSplit)node;
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
