using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public static partial class NotebookCellLayout
{
    private const int FloatingWidth = 520;
    private const int FloatingHeight = 360;
    private const int FloatingMargin = 8;
    private const int FloatingHeaderOffsetX = 80;
    private const int FloatingHeaderOffsetY = 20;
    private const int FloatingPreferredTop = 48;

    private const double PrimaryPaneShare = 5.0 / 8.0;
    private const double SecondaryPaneShare = 3.0 / 8.0;
    private const double MinPaneShare = 0.15;

    private const double ExpandedScriptRatio = PrimaryPaneShare;

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
        var surface = FindSurfaceGroupForCell(state, cellId);
        if (surface is null) {
            return state;
        }
        var ratio = surfaceSplit.Second is CellTabGroup second && second.Id == surface.Id
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

    public static NotebookCellState ResizeSplit(
        NotebookCellState state,
        string splitId,
        double ratio)
    {
        if (!double.IsFinite(ratio)
            || !EnumerateSplits(state).Any(split => split.Id == splitId)) {
            return state;
        }
        var normalized = Math.Clamp(ratio, MinPaneShare, 1 - MinPaneShare);
        return TransformRoots(state, root => UpdateSplitRatio(root, splitId, normalized));
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
        if (!state.Tabs.ContainsKey(tabId)
            || !CanPlaceTab(state, tabId, targetId)) {
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
            before
                ? DefaultMovedPaneShare(state, tabId, target)
                : 1 - DefaultMovedPaneShare(state, tabId, target),
            before ? movedGroup : target,
            before ? target : movedGroup);
        return ReplaceGroup(withoutSource, target.Id, split) with {
            NextNodeId = withoutSource.NextNodeId + 2,
        };
    }

    private static double DefaultMovedPaneShare(
        NotebookCellState state,
        string tabId,
        CellTabGroup target)
    {
        var movedIsScript = state.Windows[state.Tabs[tabId].WindowId].Kind
            == CellWindowKind.Script;
        var targetHasScript = GroupContainsKind(state, target, CellWindowKind.Script);
        return movedIsScript && !targetHasScript
            ? PrimaryPaneShare
            : SecondaryPaneShare;
    }

    public static NotebookCellState Detach(
        NotebookCellState state,
        string tabId,
        int pointerX,
        int pointerY,
        int viewportWidth,
        int viewportHeight)
    {
        if (!state.Tabs.ContainsKey(tabId)
            || FindGroupContaining(state, tabId) is null) {
            return state;
        }

        var withoutSource = RemoveTab(state, tabId, out _);
        var groupId = $"group-{withoutSource.NextNodeId}";
        var floatingId = $"floating-{withoutSource.NextNodeId + 1}";
        var group = new CellTabGroup(groupId, [tabId], tabId);
        var maxX = Math.Max(FloatingMargin, viewportWidth - FloatingWidth - FloatingMargin);
        var maxY = Math.Max(FloatingMargin, viewportHeight - FloatingHeight - FloatingMargin);
        var minY = Math.Min(FloatingPreferredTop, maxY);
        var floating = new CellFloatingHost(
            floatingId,
            group,
            Math.Clamp(pointerX - FloatingHeaderOffsetX, FloatingMargin, maxX),
            Math.Clamp(pointerY - FloatingHeaderOffsetY, minY, maxY),
            FloatingWidth,
            FloatingHeight);
        return withoutSource with {
            FloatingHosts = withoutSource.FloatingHosts.Add(floating),
            NextNodeId = withoutSource.NextNodeId + 2,
        };
    }

    public static NotebookCellState NormalizeFloatingHosts(
        NotebookCellState state,
        int viewportWidth,
        int viewportHeight)
    {
        var changed = false;
        var hosts = state.FloatingHosts.Select(host => {
            var maxX = Math.Max(FloatingMargin, viewportWidth - host.Width - FloatingMargin);
            var maxY = Math.Max(FloatingMargin, viewportHeight - host.Height - FloatingMargin);
            var x = Math.Clamp(host.X, FloatingMargin, maxX);
            var y = Math.Clamp(host.Y, FloatingMargin, maxY);
            if (x == host.X && y == host.Y) {
                return host;
            }
            changed = true;
            return host with { X = x, Y = y };
        }).ToImmutableArray();
        return changed ? state with { FloatingHosts = hosts } : state;
    }

    public static NotebookCellState ReconcileDocument(
        NotebookCellState previous,
        NotebookDocument previousDocument,
        NotebookDocument nextDocument)
    {
        ArgumentNullException.ThrowIfNull(previousDocument);
        ArgumentNullException.ThrowIfNull(nextDocument);

        var previousCells = PresentationCells(previousDocument);
        var nextCells = PresentationCells(nextDocument);

        var state = previous;
        foreach (var presentationId in previousCells.Keys) {
            if (!nextCells.ContainsKey(presentationId)) {
                state = RemoveCell(state, presentationId);
            }
        }

        foreach (var (presentationId, cell) in nextCells) {
            state = previousCells.TryGetValue(presentationId, out var previousCell)
                ? ReconcileScripts(state, previousCell, cell)
                : AddCell(state, cell);
        }

        return state;
    }

    private static Dictionary<string, NotebookCellState.PresentationCell> PresentationCells(
        NotebookDocument document)
    {
        var result = new Dictionary<string, NotebookCellState.PresentationCell>(StringComparer.Ordinal);
        foreach (var section in document.Sections) {
            foreach (var block in section.Blocks) {
                if (NotebookCellState.Describe(block) is not { } cell) {
                    continue;
                }
                result.Add(cell.PresentationId, cell);
            }
        }
        return result;
    }

    private static NotebookCellState AddCell(
        NotebookCellState state,
        NotebookCellState.PresentationCell cell)
    {
        var windows = state.Windows.ToBuilder();
        var tabs = state.Tabs.ToBuilder();
        var region = NotebookCellState.RegisterCell(windows, tabs, cell, state.NextNodeId);

        return state with {
            Windows = windows.ToImmutable(),
            Tabs = tabs.ToImmutable(),
            Regions = state.Regions.Add(region),
            NextNodeId = state.NextNodeId + 3,
        };
    }

    private static NotebookCellState RemoveCell(NotebookCellState state, string presentationId)
    {
        foreach (var window in state.Windows.Values.Where(w => w.CellId == presentationId).ToArray()) {
            var tab = state.GetTabForWindow(window.Id);
            state = RemoveTab(state, tab.Id, out _);
            state = state with {
                Windows = state.Windows.Remove(window.Id),
                Tabs = state.Tabs.Remove(tab.Id),
            };
        }

        var regionId = $"region-{presentationId}";
        var regionIndex = FindRegionIndex(state.Regions, regionId);
        if (regionIndex >= 0) {
            state = state with { Regions = state.Regions.RemoveAt(regionIndex) };
        }

        return state;
    }

    private static NotebookCellState ReconcileScripts(
        NotebookCellState state,
        NotebookCellState.PresentationCell previous,
        NotebookCellState.PresentationCell next)
    {
        if (previous.Scripts.SequenceEqual(next.Scripts)) {
            return state;
        }
        var previousIds = previous.Scripts.Select(static s => s.ScriptId).ToHashSet(StringComparer.Ordinal);
        var nextIds = next.Scripts.Select(static s => s.ScriptId).ToHashSet(StringComparer.Ordinal);

        foreach (var scriptId in previousIds) {
            if (nextIds.Contains(scriptId)) {
                continue;
            }
            var windowId = NotebookCellState.WindowId(scriptId, CellWindowKind.Script);
            if (!state.Windows.TryGetValue(windowId, out var window)) {
                continue;
            }
            var tab = state.GetTabForWindow(window.Id);
            state = RemoveTab(state, tab.Id, out _);
            state = state with {
                Windows = state.Windows.Remove(window.Id),
                Tabs = state.Tabs.Remove(tab.Id),
            };
        }

        var regionId = $"region-{next.PresentationId}";
        var windows = state.Windows.ToBuilder();
        var tabs = state.Tabs.ToBuilder();
        var newTabIds = ImmutableArray.CreateBuilder<string>();
        foreach (var script in next.Scripts) {
            var windowId = NotebookCellState.WindowId(script.ScriptId, CellWindowKind.Script);
            if (windows.TryGetValue(windowId, out var existing)) {
                if (existing.Title != script.Title) {
                    windows[windowId] = existing with { Title = script.Title };
                }
                continue;
            }
            newTabIds.Add(NotebookCellState.AddWindow(
                windows, tabs, next.PresentationId, script.ScriptId, regionId,
                CellWindowKind.Script, script.Title).Id);
        }
        state = state with { Windows = windows.ToImmutable(), Tabs = tabs.ToImmutable() };

        if (newTabIds.Count > 0) {
            var scriptGroup = FindGroupForCell(state, next.PresentationId, CellWindowKind.Script);
            if (scriptGroup is not null) {
                state = UpdateGroup(state, scriptGroup.Id, group => group with {
                    TabIds = group.TabIds.AddRange(newTabIds),
                    ActiveTabId = newTabIds[newTabIds.Count - 1],
                });
            }
        }

        return state;
    }
}
