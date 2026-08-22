using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private readonly Dictionary<string, GroupView> _groupViews = [];
    private readonly Dictionary<string, string> _groupContainers = [];
    private readonly Dictionary<string, SplitView> _splitViews = [];
    private readonly Dictionary<string, string> _splitContainers = [];
    private readonly Dictionary<string, TabHeaderView> _tabHeaders = [];
    private readonly Dictionary<string, DomElement> _floatingHosts = [];
    private readonly Dictionary<string, string> _floatingShapes = [];
    private readonly Dictionary<string, DomElement> _surfaces = [];
    private readonly Dictionary<string, string> _editorFiles = [];
    private string _regionShape = string.Empty;

    private IEnumerable<string> SurfaceIds => _editorFiles.Keys;

    private NotebookCellState CreateLayoutState()
    {
        var windows = ImmutableDictionary.CreateBuilder<string, CellWindow>();
        var tabs = ImmutableDictionary.CreateBuilder<string, CellTab>();
        var fileTabIds = _fileOrder
            .Select(file => NotebookCellState.AddWindow(
                windows, tabs, ProjectId, file.Id, RegionId,
                CellWindowKind.Script, file.Name).Id)
            .ToArray();
        var console = NotebookCellState.AddWindow(
            windows, tabs, ProjectId, "console", RegionId, CellWindowKind.Output, "Console");
        var render = NotebookCellState.AddWindow(
            windows, tabs, ProjectId, "render", RegionId, CellWindowKind.Render, "Render");
        var files = new CellTabGroup(
            "group-editor-page-files",
            [.. fileTabIds],
            fileTabIds[0]);
        var output = new CellTabGroup(
            "group-editor-page-output",
            [console.Id, render.Id],
            console.Id);
        var split = new CellSplit(
            "split-editor-page-main",
            CellAxis.Vertical,
            DefaultEditorShare,
            files,
            output);
        return new(
            windows.ToImmutable(),
            tabs.ToImmutable(),
            [new(RegionId, split)],
            [],
            0,
            0);
    }

    private void ApplyLayout()
    {
        SaveEditorSources();
        var region = _state.Regions.Single(static region => region.Id == RegionId);
        var shape = BuildShape(region.Root);
        if (shape != _regionShape) {
            RebuildRegion(region);
            _regionShape = shape;
        }
        SyncFloatingHosts();
        SyncSplitRatios();
        SyncGroups();
        _layoutRevision.Attr(
            "data-cell-layout-revision",
            _state.Revision.ToString(CultureInfo.InvariantCulture));
    }

    private void SaveEditorSources()
    {
        foreach (var (groupId, fileId) in _editorFiles) {
            if (_files.TryGetValue(fileId, out var file)
                && _editors.TryGetSource(groupId, out var source)) {
                file.Source = source;
            }
        }
    }

    private void RefreshEditorForFile(EditorFile file)
    {
        foreach (var (groupId, fileId) in _editorFiles) {
            if (fileId == file.Id) {
                _editors.Update(groupId, file.Source, HighlightsFor(file.Source));
            }
        }
    }

    private void RebuildRegion(CellRegion region)
    {
        DisposeContainer(RegionContainerId(region.Id));
        _workbench.Text(string.Empty);

        var empty = region.Root is null;
        _workbench.ToggleClass("is-empty", empty);
        if (region.Root is not null) {
            AppendNode(_workbench, region.Root, RegionContainerId(region.Id));
            return;
        }
        using var placeholder = DomElement.Create("div")
            .Class("empty")
            .Text("Drop a window here");
        using var preview = CreateDropPreview();
        _workbench.Append(placeholder).Append(preview);
    }

    private void SyncFloatingHosts()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var floating in _state.FloatingHosts) {
            seen.Add(floating.Id);
            var shape = BuildFloatingShape(floating);
            if (!_floatingShapes.TryGetValue(floating.Id, out var previous) || previous != shape) {
                RebuildFloatingHost(floating);
                _floatingShapes[floating.Id] = shape;
            }
        }
        foreach (var floatingId in _floatingHosts.Keys.Where(id => !seen.Contains(id)).ToArray()) {
            DisposeContainer(FloatingContainerId(floatingId));
            if (_floatingHosts.Remove(floatingId, out var root)) {
                root.Remove();
                root.Dispose();
            }
            _floatingShapes.Remove(floatingId);
        }
    }

    private void RebuildFloatingHost(CellFloatingHost floating)
    {
        if (_floatingHosts.Remove(floating.Id, out var oldRoot)) {
            DisposeContainer(FloatingContainerId(floating.Id));
            oldRoot.Remove();
            oldRoot.Dispose();
        }

        var root = DomElement.Create("div")
            .Class("floating-host")
            .Attr("data-cell-floating", floating.Id)
            .Attr(
                "style",
                $"left:{floating.X.ToString(CultureInfo.InvariantCulture)}px;"
                + $"top:{floating.Y.ToString(CultureInfo.InvariantCulture)}px;"
                + $"width:{floating.Width.ToString(CultureInfo.InvariantCulture)}px;"
                + $"height:{floating.Height.ToString(CultureInfo.InvariantCulture)}px");
        AppendNode(root, floating.Root, FloatingContainerId(floating.Id));
        _layoutRevision.Append(root);
        _floatingHosts[floating.Id] = root;
    }

    private void AppendNode(DomElement parent, CellNode node, string containerId)
    {
        if (node is CellTabGroup group) {
            var root = DomElement.Create("div")
                .Class("cell-group")
                .Attr("data-cell-group", group.Id);
            var tabs = DomElement.Create("div")
                .Class("cell-tabs")
                .Attr("role", "tablist")
                .Attr("aria-label", "Editor windows");
            var tabList = DomElement.Create("div").Class("tab-list");
            var content = DomElement.Create("div").Class("group-content");
            using (var preview = CreateDropPreview()) {
                tabs.Append(tabList);
                root.Append(tabs).Append(content).Append(preview);
            }
            parent.Append(root);
            _groupViews.Add(group.Id, new(group.Id, root, tabs, tabList, content));
            _groupContainers[group.Id] = containerId;
            return;
        }

        var split = (CellSplit)node;
        var splitRoot = DomElement.Create("div")
            .Class("cell-split")
            .Class(split.Axis == CellAxis.Horizontal ? "horizontal" : "vertical")
            .Attr("data-cell-split", split.Id);
        var first = DomElement.Create("div").Class("split-child");
        var second = DomElement.Create("div").Class("split-child");
        var separator = DomElement.Create("div")
            .Class("separator")
            .Attr("role", "separator")
            .Attr("tabindex", "0")
            .Attr("aria-label", "Resize editor panes")
            .Attr("aria-valuemin", "15")
            .Attr("aria-valuemax", "85")
            .Attr("aria-orientation", split.Axis == CellAxis.Horizontal
                ? "vertical"
                : "horizontal");
        AppendNode(first, split.First, containerId);
        AppendNode(second, split.Second, containerId);
        splitRoot.Append(first).Append(separator).Append(second);
        parent.Append(splitRoot);
        _splitViews.Add(split.Id, new(splitRoot, first, separator, second));
        _splitContainers[split.Id] = containerId;
    }

    private void SyncSplitRatios()
    {
        foreach (var split in NotebookCellLayout.EnumerateSplits(_state)) {
            if (!_splitViews.TryGetValue(split.Id, out var view)) {
                continue;
            }
            view.Separator.Attr(
                "aria-valuenow",
                Math.Round(split.Ratio * 100).ToString(CultureInfo.InvariantCulture));
            view.First.ToggleClass("collapsed", split.Ratio <= 0);
            view.Second.ToggleClass("collapsed", split.Ratio >= 1);
            SetShare(view.First, split.Ratio);
            SetShare(view.Second, 1 - split.Ratio);
        }
    }

    private static void SetShare(DomElement child, double share)
        => child.Attr(
            "style",
            $"--cell-split-share:{share.ToString("0.###", CultureInfo.InvariantCulture)}");

    private void SyncGroups()
    {
        var changed = new List<(CellTabGroup Group, GroupView View)>();
        foreach (var group in NotebookCellLayout.EnumerateGroups(_state)) {
            if (!_groupViews.TryGetValue(group.Id, out var groupView)) {
                continue;
            }
            foreach (var tabId in group.TabIds) {
                if (_state.Tabs.TryGetValue(tabId, out var tab)
                    && _state.Windows.ContainsKey(tab.WindowId)) {
                    EnsureTabHeader(tabId);
                }
            }
            var signature = BuildGroupSignature(group);
            if (groupView.Signature == signature) {
                continue;
            }
            changed.Add((group, groupView));
        }
        foreach (var (_, groupView) in changed) {
            DetachGroup(groupView);
        }
        foreach (var (group, groupView) in changed) {
            MountGroup(group, groupView);
        }
    }

    private static string BuildGroupSignature(CellTabGroup group)
        => group.ActiveTabId + "|" + string.Join(',', group.TabIds);

    private void DetachGroup(GroupView groupView)
    {
        foreach (var tabId in groupView.MountedTabs) {
            if (_tabHeaders.TryGetValue(tabId, out var header)) {
                header.Root.Remove();
            }
        }
        groupView.MountedTabs.Clear();
        if (groupView.ActiveWindowId is { } windowId) {
            switch (windowId) {
                case ConsoleWindowId:
                    _consolePanel.Remove();
                    break;
                case RenderWindowId:
                    _renderPanel.Remove();
                    break;
            default:
                if (_surfaces.TryGetValue(groupView.Id, out var surface)) {
                    surface.Remove();
                }
                break;
            }
            groupView.ActiveWindowId = null;
        }
        groupView.Signature = null;
    }

    private void MountGroup(CellTabGroup group, GroupView groupView)
    {
        foreach (var tabId in group.TabIds) {
            if (!_state.Tabs.TryGetValue(tabId, out var tab)) {
                continue;
            }
            var header = EnsureTabHeader(tabId);
            var active = tabId == group.ActiveTabId;
            header.Root.ToggleClass("active", active);
            header.Tab
                .ToggleClass("active", active)
                .Attr("aria-selected", active ? "true" : "false")
                .Attr("tabindex", active ? "0" : "-1");
            groupView.TabList.Append(header.Root);
            groupView.MountedTabs.Add(tabId);
        }

        if (!_state.Tabs.TryGetValue(group.ActiveTabId, out var activeTab)) {
            groupView.Signature = BuildGroupSignature(group);
            return;
        }
        var activeWindow = _state.Windows[activeTab.WindowId];
        switch (activeWindow.Id) {
            case ConsoleWindowId:
                groupView.Content.Append(_consolePanel);
                break;
            case RenderWindowId:
                groupView.Content.Append(_renderPanel);
                break;
            default:
                MountEditorSurface(group, groupView, activeWindow);
                break;
        }
        groupView.ActiveWindowId = activeWindow.Id;
        groupView.Signature = BuildGroupSignature(group);
    }

    private void MountEditorSurface(CellTabGroup group, GroupView groupView, CellWindow window)
    {
        var file = _files[window.SourceId];
        if (!_surfaces.TryGetValue(group.Id, out var surface)) {
            surface = DomElement.Create("div").Class("editor-page-editor");
            _surfaces[group.Id] = surface;
            _editors.Add(surface, group.Id, file.Source, HighlightsFor(file.Source));
            _editorFiles[group.Id] = file.Id;
        } else if (_editorFiles.TryGetValue(group.Id, out var currentFileId)
            && currentFileId != file.Id) {
            if (_files.TryGetValue(currentFileId, out var previousFile)
                && _editors.TryGetSource(group.Id, out var previousSource)) {
                previousFile.Source = previousSource;
            }
            _editors.Update(group.Id, file.Source, HighlightsFor(file.Source));
            _editorFiles[group.Id] = file.Id;
        }
        groupView.Content.Append(surface);
    }

    private TabHeaderView EnsureTabHeader(string tabId)
    {
        if (_tabHeaders.TryGetValue(tabId, out var existing)) {
            return existing;
        }
        var tab = _state.Tabs[tabId];
        var window = _state.Windows[tab.WindowId];
        var closable = window.Kind != CellWindowKind.Script;
        var root = DomElement.Create("div").Class("tab-entry");
        var header = DomElement.Create("button")
            .Class("tab")
            .Id(tab.Id)
            .Attr("type", "button")
            .Attr("role", "tab")
            .Attr("aria-controls", window.Id)
            .Attr("data-cell-tab", tab.Id)
            .Attr("data-cell-owner", ProjectId)
            .Attr("data-cell-label", window.Title)
            .Attr("aria-label", window.Title)
            .Attr("title", window.Title)
            .Text(window.Title)
            .On("click", $"editor-page-tab:{tab.Id}");
        root.Append(header);
        var view = new TabHeaderView(root, header);
        if (closable) {
            var close = DomElement.Create("button")
                .Class("close")
                .Attr("type", "button")
                .Attr("aria-label", $"Close {window.Title}")
                .Attr("title", $"Close {window.Title}")
                .On("click", $"editor-page-tab-close:{tab.Id}")
                .Text("×");
            root.Append(close);
            view.Close = close;
        }
        _tabHeaders.Add(tabId, view);
        return view;
    }

    private void DisposeContainer(string containerId)
    {
        foreach (var groupId in OwnedIds(_groupContainers, containerId)) {
            _groupContainers.Remove(groupId);
            if (_groupViews.Remove(groupId, out var group)) {
                DetachGroup(group);
                DisposeEditorSurface(groupId);
                group.Dispose();
            }
        }
        foreach (var splitId in OwnedIds(_splitContainers, containerId)) {
            _splitContainers.Remove(splitId);
            if (_splitViews.Remove(splitId, out var split)) {
                split.Dispose();
            }
        }
    }

    private void DisposeEditorSurface(string groupId)
    {
        _editorFiles.Remove(groupId);
        if (_surfaces.Remove(groupId, out var surface)) {
            _editors.Remove(groupId);
            surface.Remove();
            surface.Dispose();
        }
    }

    private void DisposeSurfaces()
    {
        foreach (var groupId in _surfaces.Keys.ToArray()) {
            DisposeEditorSurface(groupId);
        }
    }

    private void DisposeLayoutViews()
    {
        foreach (var floatingId in _floatingHosts.Keys.ToArray()) {
            DisposeContainer(FloatingContainerId(floatingId));
            if (_floatingHosts.Remove(floatingId, out var root)) {
                root.Remove();
                root.Dispose();
            }
        }
        _floatingShapes.Clear();
        DisposeContainer(RegionContainerId(RegionId));
        foreach (var header in _tabHeaders.Values) {
            header.Dispose();
        }
        _tabHeaders.Clear();
    }

    private static DomElement CreateDropPreview()
        => DomElement.Create("div")
            .Class("drop-preview")
            .Attr("aria-hidden", "true");

    private static string[] OwnedIds(Dictionary<string, string> containers, string containerId)
        => [.. containers
            .Where(pair => pair.Value == containerId)
            .Select(pair => pair.Key)];

    private static string RegionContainerId(string regionId) => "r:" + regionId;

    private static string FloatingContainerId(string floatingId) => "f:" + floatingId;

    private static string BuildShape(CellNode? root)
    {
        if (root is null) {
            return string.Empty;
        }
        var builder = new StringBuilder();
        AppendShape(builder, root);
        return builder.ToString();
    }

    private static string BuildFloatingShape(CellFloatingHost floating)
    {
        var builder = new StringBuilder();
        builder
            .Append(floating.X).Append(',')
            .Append(floating.Y).Append(',')
            .Append(floating.Width).Append(',')
            .Append(floating.Height).Append(':');
        AppendShape(builder, floating.Root);
        return builder.ToString();
    }

    private static void AppendShape(StringBuilder builder, CellNode node)
    {
        if (node is CellTabGroup group) {
            builder.Append("G(").Append(group.Id).Append(')');
            return;
        }
        var split = (CellSplit)node;
        builder.Append(split.Axis == CellAxis.Horizontal ? "H(" : "V(");
        AppendShape(builder, split.First);
        builder.Append('|');
        AppendShape(builder, split.Second);
        builder.Append(')');
    }

    private const string ConsoleWindowId = "window-console-output";
    private const string RenderWindowId = "window-render-render";

    private sealed class GroupView(
        string id,
        DomElement root,
        DomElement tabs,
        DomElement tabList,
        DomElement content) : IDisposable
    {
        public string Id { get; } = id;

        public DomElement Root { get; } = root;

        public DomElement Tabs { get; } = tabs;

        public DomElement TabList { get; } = tabList;

        public DomElement Content { get; } = content;

        public string? Signature { get; set; }

        public string? ActiveWindowId { get; set; }

        public List<string> MountedTabs { get; } = [];

        public void Dispose()
        {
            Content.Dispose();
            TabList.Dispose();
            Tabs.Dispose();
            Root.Dispose();
        }
    }

    private sealed record SplitView(
        DomElement Root,
        DomElement First,
        DomElement Separator,
        DomElement Second) : IDisposable
    {
        public void Dispose()
        {
            Second.Dispose();
            Separator.Dispose();
            First.Dispose();
            Root.Dispose();
        }
    }

    private sealed class TabHeaderView(DomElement root, DomElement tab) : IDisposable
    {
        public DomElement Root { get; } = root;

        public DomElement Tab { get; } = tab;

        public DomElement? Close { get; set; }

        public void Dispose()
        {
            Root.Remove();
            Close?.Dispose();
            Tab.Dispose();
            Root.Dispose();
        }
    }
}
