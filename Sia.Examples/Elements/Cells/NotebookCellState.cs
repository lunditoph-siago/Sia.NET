using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public readonly record struct NotebookCellState(
    ImmutableDictionary<string, CellWindow> Windows,
    ImmutableDictionary<string, CellTab> Tabs,
    ImmutableArray<CellRegion> Regions,
    ImmutableArray<CellFloatingHost> FloatingHosts,
    int NextNodeId)
{
    internal const double CollapsedScriptRatio = 1.0;
    private const int NodesPerCell = 3;

    public static NotebookCellState Create(NotebookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var windows = ImmutableDictionary.CreateBuilder<string, CellWindow>();
        var tabs = ImmutableDictionary.CreateBuilder<string, CellTab>();
        var regions = ImmutableArray.CreateBuilder<CellRegion>();
        var cellIndex = 0;
        var nextNodeId = 0;

        foreach (var section in document.Sections) {
            foreach (var cell in section.Blocks.OfType<CodeCellBlock>()) {
                regions.Add(RegisterCell(windows, tabs, cell.Id, cellIndex, nextNodeId));
                nextNodeId += NodesPerCell;
                cellIndex++;
            }
        }

        return new(
            windows.ToImmutable(),
            tabs.ToImmutable(),
            regions.ToImmutable(),
            [],
            nextNodeId);
    }

    public CellWindow GetWindow(string cellId, CellWindowKind kind)
        => Windows.Values.Single(window => window.CellId == cellId && window.Kind == kind);

    public CellTab GetTabForWindow(string windowId)
        => Tabs.Values.Single(tab => tab.WindowId == windowId);

    internal static CellRegion RegisterCell(
        IDictionary<string, CellWindow> windows,
        IDictionary<string, CellTab> tabs,
        string cellId,
        int cellIndex,
        int groupNodeId)
    {
        var regionId = $"region-{cellId}";
        var script = AddWindow(windows, tabs, cellId, regionId, CellWindowKind.Script, $"[{cellIndex + 1}] {cellId}");
        var output = AddWindow(windows, tabs, cellId, regionId, CellWindowKind.Output, $"Output · {cellId}");
        AddWindow(windows, tabs, cellId, regionId, CellWindowKind.Render, $"Render · {cellId}");

        var scriptGroup = new CellTabGroup($"group-{groupNodeId}", [script.Id], script.Id);
        var surfaceGroup = new CellTabGroup($"group-{groupNodeId}-surface", [output.Id], output.Id);
        var split = new CellSplit(
            $"split-{groupNodeId}-surface",
            CellAxis.Vertical,
            CollapsedScriptRatio,
            scriptGroup,
            surfaceGroup);
        return new(regionId, split);
    }

    private static CellTab AddWindow(
        IDictionary<string, CellWindow> windows,
        IDictionary<string, CellTab> tabs,
        string cellId,
        string homeRegionId,
        CellWindowKind kind,
        string title)
    {
        var suffix = kind.ToString().ToLowerInvariant();
        var window = new CellWindow(
            $"window-{cellId}-{suffix}",
            cellId,
            homeRegionId,
            kind,
            title);
        var tab = new CellTab($"tab-{cellId}-{suffix}", window.Id);
        windows.Add(window.Id, window);
        tabs.Add(tab.Id, tab);
        return tab;
    }
}
