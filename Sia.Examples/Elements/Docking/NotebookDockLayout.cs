namespace Sia_Examples.Notebook;

public static partial class NotebookDockLayout
{
    private const int FloatingWidth = 520;
    private const int FloatingHeight = 360;

    private const double ExpandedScriptRatio = 0.72;

    private static NotebookDockState ExpandSurface(
        NotebookDockState state,
        string cellId)
        => SetSurfaceScriptShare(state, cellId, ExpandedScriptRatio);

    private static NotebookDockState SetSurfaceScriptShare(
        NotebookDockState state,
        string cellId,
        double scriptShare)
    {
        var surfaceSplit = FindSurfaceSplit(state, cellId);
        var script = FindGroupForCell(state, cellId, DockWindowKind.Script);
        if (surfaceSplit is null || script is null) {
            return state;
        }
        var ratio = surfaceSplit.First is DockTabGroup first && first.Id == script.Id
            ? scriptShare
            : 1 - scriptShare;
        if (surfaceSplit.Ratio == ratio) {
            return state;
        }
        return TransformRoots(
            state,
            root => UpdateSplitRatio(root, surfaceSplit.Id, ratio));
    }

    private static DockNode UpdateSplitRatio(
        DockNode node,
        string splitId,
        double ratio)
    {
        if (node is DockTabGroup) {
            return node;
        }
        var split = (DockSplit)node;
        if (split.Id == splitId) {
            return split with { Ratio = ratio };
        }
        return split with {
            First = UpdateSplitRatio(split.First, splitId, ratio),
            Second = UpdateSplitRatio(split.Second, splitId, ratio),
        };
    }

    public static NotebookDockState Activate(NotebookDockState state, string tabId)
    {
        if (!state.Tabs.ContainsKey(tabId)
            || FindGroupContaining(state, tabId) is null) {
            return state;
        }

        return UpdateGroupContaining(
            state,
            tabId,
            group => group with { ActiveTabId = tabId });
    }

    public static NotebookDockState OpenWindow(
        NotebookDockState state,
        string windowId)
    {
        if (!state.Windows.TryGetValue(windowId, out var window)) {
            return state;
        }

        var tab = state.GetTabForWindow(windowId);
        if (FindGroupContaining(state, tab.Id) is not null) {
            var activated = Activate(state, tab.Id);
            return window.Kind is DockWindowKind.Output or DockWindowKind.Render
                ? ExpandSurface(activated, window.CellId)
                : activated;
        }

        if (window.Kind is DockWindowKind.Output or DockWindowKind.Render) {
            var surface = FindSurfaceGroupForCell(state, window.CellId);
            if (surface is not null) {
                var opened = UpdateGroup(
                    state,
                    surface.Id,
                    group => group with {
                        TabIds = group.TabIds.Add(tab.Id),
                        ActiveTabId = tab.Id,
                    });
                return ExpandSurface(opened, window.CellId);
            }
            var script = FindGroupForCell(state, window.CellId, DockWindowKind.Script);
            if (script is not null) {
                var surfaceGroup = new DockTabGroup(
                    $"group-{state.NextNodeId}",
                    [tab.Id],
                    tab.Id);
                var split = new DockSplit(
                    $"split-{state.NextNodeId + 1}",
                    DockAxis.Vertical,
                    ExpandedScriptRatio,
                    script,
                    surfaceGroup);
                return ReplaceGroup(state, script.Id, split) with {
                    NextNodeId = state.NextNodeId + 2,
                };
            }
        }

        var regionIndex = FindRegionIndex(state.Regions, window.HomeRegionId);
        if (regionIndex < 0 || state.Regions[regionIndex].Root is not null) {
            return state;
        }

        var group = new DockTabGroup(
            $"group-{state.NextNodeId}",
            [tab.Id],
            tab.Id);
        return state with {
            Regions = state.Regions.SetItem(
                regionIndex,
                state.Regions[regionIndex] with { Root = group }),
            NextNodeId = state.NextNodeId + 1,
        };
    }

    public static NotebookDockState CloseWindow(
        NotebookDockState state,
        string windowId)
    {
        if (!state.Windows.TryGetValue(windowId, out var window)
            || window.Kind == DockWindowKind.Script) {
            return state;
        }

        if (window.Kind == DockWindowKind.Output
            && FindSurfaceSplit(state, window.CellId) is not null) {
            return SetSurfaceScriptShare(
                state,
                window.CellId,
                NotebookDockState.CollapsedScriptRatio);
        }

        var tab = state.GetTabForWindow(windowId);
        var next = RemoveTab(state, tab.Id, out var removed);
        return removed ? next : state;
    }

    public static NotebookDockState Dock(
        NotebookDockState state,
        string tabId,
        string targetId,
        DockDropPosition position,
        int targetIndex = int.MaxValue)
    {
        if (!state.Tabs.ContainsKey(tabId)) {
            return state;
        }

        var source = FindGroupContaining(state, tabId);
        var target = FindGroup(state, targetId);
        if (target is null) {
            return DockToEmptyRegion(state, tabId, targetId);
        }

        if (source?.Id == target.Id && position == DockDropPosition.Center) {
            return Reorder(state, target, tabId, targetIndex);
        }
        if (source?.Id == target.Id && source.TabIds.Length == 1) {
            return state;
        }

        var sourceIndex = source?.TabIds.IndexOf(tabId) ?? -1;
        var withoutSource = RemoveTab(state, tabId, out var removed);
        if (!removed) {
            return state;
        }

        target = FindGroup(withoutSource, targetId);
        if (target is null) {
            return state;
        }

        if (position == DockDropPosition.Center) {
            if (source?.Id == target.Id && sourceIndex >= 0 && sourceIndex < targetIndex) {
                targetIndex--;
            }
            var insertion = Math.Clamp(targetIndex, 0, target.TabIds.Length);
            return UpdateGroup(
                withoutSource,
                target.Id,
                group => group with {
                    TabIds = group.TabIds.Insert(insertion, tabId),
                    ActiveTabId = tabId,
                });
        }

        var groupId = $"group-{withoutSource.NextNodeId}";
        var splitId = $"split-{withoutSource.NextNodeId + 1}";
        var movedGroup = new DockTabGroup(groupId, [tabId], tabId);
        var before = position is DockDropPosition.Left or DockDropPosition.Top;
        var axis = position is DockDropPosition.Left or DockDropPosition.Right
            ? DockAxis.Horizontal
            : DockAxis.Vertical;
        var split = new DockSplit(
            splitId,
            axis,
            0.5,
            before ? movedGroup : target,
            before ? target : movedGroup);
        return ReplaceGroup(withoutSource, target.Id, split) with {
            NextNodeId = withoutSource.NextNodeId + 2,
        };
    }

    public static NotebookDockState Detach(
        NotebookDockState state,
        string tabId,
        int pointerX,
        int pointerY)
    {
        if (!state.Tabs.ContainsKey(tabId)
            || FindGroupContaining(state, tabId) is null) {
            return state;
        }

        var withoutSource = RemoveTab(state, tabId, out _);
        var groupId = $"group-{withoutSource.NextNodeId}";
        var floatingId = $"floating-{withoutSource.NextNodeId + 1}";
        var group = new DockTabGroup(groupId, [tabId], tabId);
        var floating = new DockFloatingHost(
            floatingId,
            group,
            Math.Max(8, pointerX - 80),
            Math.Max(48, pointerY - 20),
            FloatingWidth,
            FloatingHeight);
        return withoutSource with {
            FloatingHosts = withoutSource.FloatingHosts.Add(floating),
            NextNodeId = withoutSource.NextNodeId + 2,
        };
    }

    public static NotebookDockState ReconcileDocument(
        NotebookDockState previous,
        NotebookDocument previousDocument,
        NotebookDocument nextDocument)
    {
        ArgumentNullException.ThrowIfNull(previousDocument);
        ArgumentNullException.ThrowIfNull(nextDocument);

        var previousCellIds = CellIds(previousDocument);
        var nextCellIds = CellIds(nextDocument);

        var state = previous;
        foreach (var cellId in previousCellIds) {
            if (!nextCellIds.Contains(cellId)) {
                state = RemoveCell(state, cellId);
            }
        }

        var cellIndex = 0;
        foreach (var section in nextDocument.Sections) {
            foreach (var cell in section.Blocks.OfType<CodeCellBlock>()) {
                if (!previousCellIds.Contains(cell.Id)) {
                    state = AddCell(state, cell.Id, cellIndex);
                }
                cellIndex++;
            }
        }

        return state;
    }

    private static HashSet<string> CellIds(NotebookDocument document)
        => document.Sections
            .SelectMany(static section => section.Blocks.OfType<CodeCellBlock>())
            .Select(static cell => cell.Id)
            .ToHashSet(StringComparer.Ordinal);

    private static NotebookDockState AddCell(
        NotebookDockState state,
        string cellId,
        int cellIndex)
    {
        var windows = state.Windows.ToBuilder();
        var tabs = state.Tabs.ToBuilder();
        var region = NotebookDockState.RegisterCell(windows, tabs, cellId, cellIndex, state.NextNodeId);

        return state with {
            Windows = windows.ToImmutable(),
            Tabs = tabs.ToImmutable(),
            Regions = state.Regions.Add(region),
            NextNodeId = state.NextNodeId + 3,
        };
    }

    private static NotebookDockState RemoveCell(NotebookDockState state, string cellId)
    {
        foreach (var window in state.Windows.Values.Where(w => w.CellId == cellId).ToArray()) {
            var tab = state.GetTabForWindow(window.Id);
            state = RemoveTab(state, tab.Id, out _);
            state = state with {
                Windows = state.Windows.Remove(window.Id),
                Tabs = state.Tabs.Remove(tab.Id),
            };
        }

        var regionId = $"region-{cellId}";
        var regionIndex = FindRegionIndex(state.Regions, regionId);
        if (regionIndex >= 0 && state.Regions[regionIndex].Root is null) {
            state = state with { Regions = state.Regions.RemoveAt(regionIndex) };
        }

        return state;
    }
}
