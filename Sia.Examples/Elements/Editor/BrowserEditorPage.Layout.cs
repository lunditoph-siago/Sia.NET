using System.Collections.Immutable;
using System.Globalization;
using Sia;
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
    private readonly Dictionary<string, Entity> _editorEntities = [];
    private readonly Dictionary<Entity, EditorMemento> _mementos = [];
    private string _regionShape = string.Empty;

    private const string ConsoleWindowId = "window-console-output";
    private const string RenderWindowId = "window-render-render";
    private const string SeparatorAriaLabel = "Resize editor panes";

    private IEnumerable<string> SurfaceIds => _editorEntities.Keys;

    private NotebookCellState CreateLayoutState()
    {
        var windows = ImmutableDictionary.CreateBuilder<string, CellWindow>();
        var tabs = ImmutableDictionary.CreateBuilder<string, CellTab>();
        NotebookCellState.AddWindow(
            windows, tabs, ProjectId, "console", RegionId, CellWindowKind.Output, "Console");
        NotebookCellState.AddWindow(
            windows, tabs, ProjectId, "render", RegionId, CellWindowKind.Render, "Render");
        return new(
            windows.ToImmutable(),
            tabs.ToImmutable(),
            [new(RegionId, null)],
            [],
            0,
            0);
    }

    private void EnsureFileWindow(Entity entity)
    {
        var windowId = WindowIdFor(entity);
        if (_state.Windows.ContainsKey(windowId)) {
            return;
        }
        var tabId = TabIdFor(entity);
        var window = new CellWindow(
            windowId, ProjectId, RegionId, CellWindowKind.Script, EditorWorkspace.NameOf(entity), KeyFor(entity));
        var tab = new CellTab(tabId, windowId);
        _state = _state with {
            Windows = _state.Windows.Add(windowId, window),
            Tabs = _state.Tabs.Add(tabId, tab),
        };
    }

    private void ApplyLayout()
    {
        SaveEditorSources();
        var region = _state.Regions.Single(static region => region.Id == RegionId);
        var shape = CellLayoutDom.BuildNodeShape(region.Root);
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
        foreach (var (groupId, entity) in _editorEntities) {
            if (entity.IsValid && _editors.TryGetSource(groupId, out var source)) {
                _workspace.SetContent(entity, source);
            }
        }
    }

    private void RefreshEditorForEntity(Entity entity)
    {
        foreach (var (groupId, current) in _editorEntities) {
            if (current == entity) {
                var content = entity.Get<WorkspaceFile>().Content;
                _editors.Update(groupId, content, HighlightsFor(content));
            }
        }
    }

    private void RebuildRegion(CellRegion region)
    {
        var containerId = CellLayoutDom.RegionContainerId(region.Id);
        DisposeContainer(containerId);
        _workbench.Text(string.Empty);

        var empty = region.Root is null;
        _workbench.ToggleClass("is-empty", empty);
        if (region.Root is not null) {
            CellLayoutDom.AppendNode(
                _workbench, region.Root, containerId, "Editor windows", _groupViews, _groupContainers,
                _splitViews, _splitContainers, SeparatorAriaLabel);
            return;
        }
        using var placeholder = DomElement.Create("div")
            .Class("empty")
            .Text("Loading workspace…");
        using var preview = CellLayoutDom.CreateDropPreview();
        _workbench.Append(placeholder).Append(preview);
    }

    private void SyncFloatingHosts()
        => CellLayoutDom.SyncFloatingHosts(
            _state.FloatingHosts, _floatingShapes, _floatingHosts.Keys, RebuildFloatingHost, RemoveFloatingHost);

    private void RebuildFloatingHost(CellFloatingHost floating)
    {
        if (_floatingHosts.Remove(floating.Id, out var oldRoot)) {
            DisposeContainer(CellLayoutDom.FloatingContainerId(floating.Id));
            oldRoot.Remove();
            oldRoot.Dispose();
        }

        var containerId = CellLayoutDom.FloatingContainerId(floating.Id);
        var root = CellLayoutDom.CreateFloatingHostElement(floating);
        CellLayoutDom.AppendNode(
            root, floating.Root, containerId, "Editor windows", _groupViews, _groupContainers,
            _splitViews, _splitContainers, SeparatorAriaLabel);
        _layoutRevision.Append(root);
        _floatingHosts[floating.Id] = root;
    }

    private void RemoveFloatingHost(string floatingId)
    {
        DisposeContainer(CellLayoutDom.FloatingContainerId(floatingId));
        if (_floatingHosts.Remove(floatingId, out var root)) {
            root.Remove();
            root.Dispose();
        }
        _floatingShapes.Remove(floatingId);
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
            CellLayoutDom.SetShare(view.First, split.Ratio);
            CellLayoutDom.SetShare(view.Second, 1 - split.Ratio);
        }
    }

    private void SyncGroups()
        => CellLayoutDom.SyncGroups(
            NotebookCellLayout.EnumerateGroups(_state), _groupViews, EnsureTabsForGroup, DetachGroup, MountGroup);

    private void EnsureTabsForGroup(CellTabGroup group)
    {
        foreach (var tabId in group.TabIds) {
            if (_state.Tabs.TryGetValue(tabId, out var tab)
                && _state.Windows.ContainsKey(tab.WindowId)) {
                EnsureTabHeader(tabId);
            }
        }
    }

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
            groupView.Signature = CellLayoutDom.BuildGroupSignature(group);
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
        groupView.Signature = CellLayoutDom.BuildGroupSignature(group);
    }

    private void MountEditorSurface(CellTabGroup group, GroupView groupView, CellWindow window)
    {
        if (!TryResolve(window.SourceId, out var entity) || !entity.IsValid) {
            return;
        }
        var content = entity.Get<WorkspaceFile>().Content;
        if (!_surfaces.TryGetValue(group.Id, out var surface)) {
            surface = DomElement.Create("div").Class("editor-page-editor");
            _surfaces[group.Id] = surface;
            _editors.Add(surface, group.Id, content, HighlightsFor(content));
            _editorEntities[group.Id] = entity;
            RestoreMemento(group.Id, entity);
        } else if (_editorEntities.TryGetValue(group.Id, out var currentEntity)
            && currentEntity != entity) {
            CaptureMemento(group.Id, currentEntity);
            if (currentEntity.IsValid && _editors.TryGetSource(group.Id, out var previousSource)) {
                _workspace.SetContent(currentEntity, previousSource);
            }
            _editors.Update(group.Id, content, HighlightsFor(content));
            _editorEntities[group.Id] = entity;
            RestoreMemento(group.Id, entity);
        }
        groupView.Content.Append(surface);
    }

    private void CaptureMemento(string groupId, Entity entity)
    {
        if (entity.IsValid && _editors.TryGetPosition(groupId, out var memento)) {
            _mementos[entity] = memento;
        }
    }

    private void RestoreMemento(string groupId, Entity entity)
    {
        if (_mementos.TryGetValue(entity, out var memento)) {
            _editors.RestorePosition(groupId, memento);
        }
    }

    private TabHeaderView EnsureTabHeader(string tabId)
    {
        var tab = _state.Tabs[tabId];
        var window = _state.Windows[tab.WindowId];
        var entity = window.Kind == CellWindowKind.Script
            && TryResolve(window.SourceId, out var resolved) && resolved.IsValid
                ? resolved
                : (Entity?)null;
        var title = entity is { } named ? EditorWorkspace.NameOf(named) : window.Title;

        if (_tabHeaders.TryGetValue(tabId, out var existing)) {
            existing.Tab
                .Attr("data-cell-label", title)
                .Attr("aria-label", title)
                .Attr("title", title)
                .Text(title);
            if (entity is { } liveEntity) {
                existing.SetDirty(EditorWorkspace.IsDirty(liveEntity));
                existing.SetPreview(liveEntity.Contains<PreviewTab>());
            }
            return existing;
        }

        var root = DomElement.Create("div").Class("tab-entry").ToggleClass("has-close", true);
        var header = DomElement.Create("button")
            .Class("tab")
            .Id(tab.Id)
            .Attr("type", "button")
            .Attr("role", "tab")
            .Attr("aria-controls", window.Id)
            .Attr("data-cell-tab", tab.Id)
            .Attr("data-cell-owner", ProjectId)
            .Attr("data-cell-label", title)
            .Attr("aria-label", title)
            .Attr("title", title)
            .Text(title)
            .On("click", $"editor-page-tab:{tab.Id}");
        if (entity is { } dblClickEntity) {
            header.On("dblclick", $"editor-page-tree-pin:{KeyFor(dblClickEntity)}");
        }
        root.Append(header);
        var close = DomElement.Create("button")
            .Class("close")
            .Attr("type", "button")
            .Attr("aria-label", $"Close {title}")
            .Attr("title", "Close")
            .On("click", $"editor-page-tab-close:{tab.Id}");
        root.Append(close);
        var view = new TabHeaderView(root, header, close);
        if (entity is { } newEntity) {
            view.SetDirty(EditorWorkspace.IsDirty(newEntity));
            view.SetPreview(newEntity.Contains<PreviewTab>());
        }
        _tabHeaders.Add(tabId, view);
        return view;
    }

    private void SyncDirtyIndicators()
    {
        foreach (var (groupId, entity) in _editorEntities) {
            if (!entity.IsValid || !_tabHeaders.TryGetValue(TabIdFor(entity), out var header)) {
                continue;
            }
            var live = _editors.TryGetSource(groupId, out var source)
                ? source
                : entity.Get<WorkspaceFile>().Content;
            header.SetDirty(live != entity.Get<WorkspaceFile>().SavedContent);
        }
    }

    private void DisposeContainer(string containerId)
        => CellLayoutDom.DisposeContainer(
            containerId, _groupContainers, _groupViews,
            group => {
                DetachGroup(group);
                DisposeEditorSurface(group.Id);
            },
            _splitContainers, _splitViews);

    private void DisposeEditorSurface(string groupId)
    {
        if (_editorEntities.Remove(groupId, out var entity)) {
            CaptureMemento(groupId, entity);
        }
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
            DisposeContainer(CellLayoutDom.FloatingContainerId(floatingId));
            if (_floatingHosts.Remove(floatingId, out var root)) {
                root.Remove();
                root.Dispose();
            }
        }
        _floatingShapes.Clear();
        DisposeContainer(CellLayoutDom.RegionContainerId(RegionId));
        foreach (var header in _tabHeaders.Values) {
            header.Dispose();
        }
        _tabHeaders.Clear();
    }

    private sealed class TabHeaderView(DomElement root, DomElement tab, DomElement close) : IDisposable
    {
        private bool _dirty;
        private bool _preview;

        public DomElement Root { get; } = root;

        public DomElement Tab { get; } = tab;

        public DomElement Close { get; } = close;

        public void SetDirty(bool dirty)
        {
            if (_dirty == dirty) {
                return;
            }
            _dirty = dirty;
            Root.ToggleClass("dirty", dirty);
        }

        public void SetPreview(bool preview)
        {
            if (_preview == preview) {
                return;
            }
            _preview = preview;
            Tab.ToggleClass("preview", preview);
        }

        public void Dispose()
        {
            Root.Remove();
            Close.Dispose();
            Tab.Dispose();
            Root.Dispose();
        }
    }
}
