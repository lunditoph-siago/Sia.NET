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
                var regionId = $"region-{cellIndex}";
                var script = AddWindow(
                    windows,
                    tabs,
                    cellIndex,
                    cell.Id,
                    regionId,
                    DockWindowKind.Script,
                    $"[{cellIndex + 1}] {cell.Id}");
                AddWindow(
                    windows,
                    tabs,
                    cellIndex,
                    cell.Id,
                    regionId,
                    DockWindowKind.Output,
                    $"Output · {cell.Id}");
                AddWindow(
                    windows,
                    tabs,
                    cellIndex,
                    cell.Id,
                    regionId,
                    DockWindowKind.Render,
                    $"Render · {cell.Id}");

                var scriptGroup = new DockTabGroup(
                    $"group-{nextNodeId++}",
                    [script.Id],
                    script.Id);
                regions.Add(new(regionId, scriptGroup));
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

    private static DockTab AddWindow(
        IDictionary<string, DockWindow> windows,
        IDictionary<string, DockTab> tabs,
        int cellIndex,
        string cellId,
        string homeRegionId,
        DockWindowKind kind,
        string title)
    {
        var suffix = kind.ToString().ToLowerInvariant();
        var window = new DockWindow(
            $"window-{cellIndex}-{suffix}",
            cellId,
            homeRegionId,
            kind,
            title);
        var tab = new DockTab($"tab-{cellIndex}-{suffix}", window.Id);
        windows.Add(window.Id, window);
        tabs.Add(tab.Id, tab);
        return tab;
    }
}
