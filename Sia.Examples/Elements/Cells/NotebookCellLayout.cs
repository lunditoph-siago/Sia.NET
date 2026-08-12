namespace Sia_Examples.Notebook;

public static partial class NotebookCellLayout
{
    private const int FloatingWidth = 520;
    private const int FloatingHeight = 360;

    private const double ExpandedScriptRatio = 0.72;

    private static NotebookCellState ExpandSurface(
        NotebookCellState state,
        string cellId)
        => SetSurfaceScriptShare(state, cellId, ExpandedScriptRatio);

    private static NotebookCellState SetSurfaceScriptShare(
        NotebookCellState state,
        string cellId,
        double scriptShare)
    {
        var surfaceSplit = FindSurfaceSplit(state, cellId);
        var script = FindGroupForCell(state, cellId, CellWindowKind.Script);
        if (surfaceSplit is null || script is null) {
            return state;
        }
        var ratio = surfaceSplit.First is CellTabGroup first && first.Id == script.Id
            ? scriptShare
            : 1 - scriptShare;
        if (surfaceSplit.Ratio == ratio) {
            return state;
        }
        return TransformRoots(
            state,
            root => UpdateSplitRatio(root, surfaceSplit.Id, ratio));
    }

    private static CellNode UpdateSplitRatio(
        CellNode node,
        string splitId,
        double ratio)
    {
        if (node is CellTabGroup) {
            return node;
        }
        var split = (CellSplit)node;
        if (split.Id == splitId) {
            return split with { Ratio = ratio };
        }
        return split with {
            First = UpdateSplitRatio(split.First, splitId, ratio),
            Second = UpdateSplitRatio(split.Second, splitId, ratio),
        };
    }

    public static NotebookCellState Activate(NotebookCellState state, string tabId)
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

    public static NotebookCellState OpenWindow(
        NotebookCellState state,
        string windowId)
    {
        if (!state.Windows.TryGetValue(windowId, out var window)) {
            return state;
        }

        var tab = state.GetTabForWindow(windowId);
        if (FindGroupContaining(state, tab.Id) is not null) {
            var activated = Activate(state, tab.Id);
            return window.Kind is CellWindowKind.Output or CellWindowKind.Render
                ? ExpandSurface(activated, window.CellId)
                : activated;
        }

        if (window.Kind is CellWindowKind.Output or CellWindowKind.Render) {
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
            var script = FindGroupForCell(state, window.CellId, CellWindowKind.Script);
            if (script is not null) {
                var surfaceGroup = new CellTabGroup(
                    $"group-{state.NextNodeId}",
                    [tab.Id],
                    tab.Id);
                var split = new CellSplit(
                    $"split-{state.NextNodeId + 1}",
                    CellAxis.Vertical,
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

        var group = new CellTabGroup(
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

    public static NotebookCellState CloseWindow(
        NotebookCellState state,
        string windowId)
    {
        if (!state.Windows.TryGetValue(windowId, out var window)
            || window.Kind == CellWindowKind.Script) {
            return state;
        }

        if (window.Kind == CellWindowKind.Output
            && FindSurfaceSplit(state, window.CellId) is not null) {
            return SetSurfaceScriptShare(
                state,
                window.CellId,
                NotebookCellState.CollapsedScriptRatio);
        }

        var tab = state.GetTabForWindow(windowId);
        var next = RemoveTab(state, tab.Id, out var removed);
        return removed ? next : state;
    }

    public static NotebookCellState Cell(
        NotebookCellState state,
        string tabId,
        string targetId,
        CellDropPosition position,
        int targetIndex = int.MaxValue)
    {
        if (!state.Tabs.ContainsKey(tabId)) {
            return state;
        }

        var source = FindGroupContaining(state, tabId);
        var target = FindGroup(state, targetId);
        if (target is null) {
            return CellToEmptyRegion(state, tabId, targetId);
        }

        if (source?.Id == target.Id && position == CellDropPosition.Center) {
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

        if (position == CellDropPosition.Center) {
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
        var movedGroup = new CellTabGroup(groupId, [tabId], tabId);
        var before = position is CellDropPosition.Left or CellDropPosition.Top;
        var axis = position is CellDropPosition.Left or CellDropPosition.Right
            ? CellAxis.Horizontal
            : CellAxis.Vertical;
        var split = new CellSplit(
            splitId,
            axis,
            0.5,
            before ? movedGroup : target,
            before ? target : movedGroup);
        return ReplaceGroup(withoutSource, target.Id, split) with {
            NextNodeId = withoutSource.NextNodeId + 2,
        };
    }

    public static NotebookCellState Detach(
        NotebookCellState state,
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
        var group = new CellTabGroup(groupId, [tabId], tabId);
        var floating = new CellFloatingHost(
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

    public static NotebookCellState ReconcileDocument(
        NotebookCellState previous,
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

    private static NotebookCellState AddCell(
        NotebookCellState state,
        string cellId,
        int cellIndex)
    {
        var windows = state.Windows.ToBuilder();
        var tabs = state.Tabs.ToBuilder();
        var region = NotebookCellState.RegisterCell(windows, tabs, cellId, cellIndex, state.NextNodeId);

        return state with {
            Windows = windows.ToImmutable(),
            Tabs = tabs.ToImmutable(),
            Regions = state.Regions.Add(region),
            NextNodeId = state.NextNodeId + 3,
        };
    }

    private static NotebookCellState RemoveCell(NotebookCellState state, string cellId)
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
