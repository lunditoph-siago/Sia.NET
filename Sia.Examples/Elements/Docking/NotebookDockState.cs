using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public readonly record struct NotebookDockState(
    ImmutableDictionary<string, DockWindow> Windows,
    ImmutableDictionary<string, DockTab> Tabs,
    ImmutableArray<DockRegion> Regions,
    ImmutableArray<DockFloatingHost> FloatingHosts,
    int NextNodeId)
{
    public static NotebookDockState Create(NotebookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var windows = ImmutableDictionary.CreateBuilder<string, DockWindow>();
        var tabs = ImmutableDictionary.CreateBuilder<string, DockTab>();
        var regions = ImmutableArray.CreateBuilder<DockRegion>();
        var cellIndex = 0;
        var nextNodeId = 0;

        foreach (var section in document.Sections) {
            foreach (var cell in section.Blocks.OfType<CodeCellBlock>()) {
                regions.Add(RegisterCell(windows, tabs, cell.Id, cellIndex, nextNodeId++));
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

    public DockWindow GetWindow(string cellId, DockWindowKind kind)
        => Windows.Values.Single(window => window.CellId == cellId && window.Kind == kind);

    public DockTab GetTabForWindow(string windowId)
        => Tabs.Values.Single(tab => tab.WindowId == windowId);

    internal static DockRegion RegisterCell(
        IDictionary<string, DockWindow> windows,
        IDictionary<string, DockTab> tabs,
        string cellId,
        int cellIndex,
        int groupNodeId)
    {
        var regionId = $"region-{cellId}";
        var script = AddWindow(windows, tabs, cellId, regionId, DockWindowKind.Script, $"[{cellIndex + 1}] {cellId}");
        AddWindow(windows, tabs, cellId, regionId, DockWindowKind.Output, $"Output · {cellId}");
        AddWindow(windows, tabs, cellId, regionId, DockWindowKind.Render, $"Render · {cellId}");
        var scriptGroup = new DockTabGroup($"group-{groupNodeId}", [script.Id], script.Id);
        return new(regionId, scriptGroup);
    }

    private static DockTab AddWindow(
        IDictionary<string, DockWindow> windows,
        IDictionary<string, DockTab> tabs,
        string cellId,
        string homeRegionId,
        DockWindowKind kind,
        string title)
    {
        var suffix = kind.ToString().ToLowerInvariant();
        var window = new DockWindow(
            $"window-{cellId}-{suffix}",
            cellId,
            homeRegionId,
            kind,
            title);
        var tab = new DockTab($"tab-{cellId}-{suffix}", window.Id);
        windows.Add(window.Id, window);
        tabs.Add(tab.Id, tab);
        return tab;
    }
}
