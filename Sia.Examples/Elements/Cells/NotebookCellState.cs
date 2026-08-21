using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public readonly record struct NotebookCellState(
    ImmutableDictionary<string, CellWindow> Windows,
    ImmutableDictionary<string, CellTab> Tabs,
    ImmutableArray<CellRegion> Regions,
    ImmutableArray<CellFloatingHost> FloatingHosts,
    int NextNodeId,
    long Revision)
{
    internal const double CollapsedScriptRatio = 1.0;
    private const int NodesPerCell = 3;

    public static NotebookCellState Create(NotebookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var windows = ImmutableDictionary.CreateBuilder<string, CellWindow>();
        var tabs = ImmutableDictionary.CreateBuilder<string, CellTab>();
        var regions = ImmutableArray.CreateBuilder<CellRegion>();
        var nextNodeId = 0;

        foreach (var section in document.Sections) {
            foreach (var block in section.Blocks) {
                if (Describe(block) is not { } cell) {
                    continue;
                }
                regions.Add(RegisterCell(windows, tabs, cell, nextNodeId));
                nextNodeId += NodesPerCell;
            }
        }

        return new(
            windows.ToImmutable(),
            tabs.ToImmutable(),
            regions.ToImmutable(),
            [],
            nextNodeId,
            0);
    }

    public CellWindow GetWindow(string cellId, CellWindowKind kind)
        => Windows.Values.Single(window => window.CellId == cellId && window.Kind == kind);

    public CellWindow GetScriptWindow(string scriptId)
        => Windows[WindowId(scriptId, CellWindowKind.Script)];

    public CellTab GetTabForWindow(string windowId)
        => Tabs.Values.Single(tab => tab.WindowId == windowId);

    internal static PresentationCell? Describe(NotebookBlock block)
        => block switch {
            CodeCellBlock cell => new(
                cell.Id,
                [.. cell.Scripts.Select((script, index) => (script.Id, script.Name ?? DefaultScriptName(index)))]),
            _ => null,
        };

    internal static string DefaultScriptName(int index)
        => index == 0 ? "Source" : $"Source {index + 1}";

    internal static CellRegion RegisterCell(
        IDictionary<string, CellWindow> windows,
        IDictionary<string, CellTab> tabs,
        PresentationCell cell,
        int groupNodeId)
    {
        var regionId = $"region-{cell.PresentationId}";
        var scriptTabs = cell.Scripts
            .Select(script => AddWindow(
                windows, tabs, cell.PresentationId, script.ScriptId, regionId,
                CellWindowKind.Script, script.Title))
            .ToImmutableArray();
        var output = AddWindow(
            windows, tabs, cell.PresentationId, cell.PresentationId, regionId,
            CellWindowKind.Output, $"Output · {cell.PresentationId}");
        AddWindow(
            windows, tabs, cell.PresentationId, cell.PresentationId, regionId,
            CellWindowKind.Render, $"Render · {cell.PresentationId}");

        var scriptGroup = new CellTabGroup(
            $"group-{groupNodeId}",
            [.. scriptTabs.Select(static tab => tab.Id)],
            scriptTabs[0].Id);
        var surfaceGroup = new CellTabGroup($"group-{groupNodeId}-surface", [output.Id], output.Id);
        var split = new CellSplit(
            $"split-{groupNodeId}-surface",
            CellAxis.Vertical,
            CollapsedScriptRatio,
            scriptGroup,
            surfaceGroup);
        return new(regionId, split);
    }

    internal static CellTab AddWindow(
        IDictionary<string, CellWindow> windows,
        IDictionary<string, CellTab> tabs,
        string presentationId,
        string windowKey,
        string homeRegionId,
        CellWindowKind kind,
        string title)
    {
        var window = new CellWindow(
            WindowId(windowKey, kind), presentationId, homeRegionId, kind, title, windowKey);
        var tab = new CellTab($"tab-{windowKey}-{Suffix(kind)}", window.Id);
        windows.Add(window.Id, window);
        tabs.Add(tab.Id, tab);
        return tab;
    }

    internal static string WindowId(string windowKey, CellWindowKind kind)
        => $"window-{windowKey}-{Suffix(kind)}";

    private static string Suffix(CellWindowKind kind) => kind.ToString().ToLowerInvariant();

    internal readonly record struct PresentationCell(
        string PresentationId,
        ImmutableArray<(string ScriptId, string Title)> Scripts);
}
