using System.Globalization;
using System.Text;

using Sia_Examples.Dom;

namespace Sia_Examples.Notebook;

public sealed class BrowserDockWorkspaceView(DomElement floatingLayer) : IDisposable
{
    private readonly Dictionary<string, DomElement> _regions = [];
    private readonly Dictionary<string, BrowserDockWindowView> _windows = [];
    private readonly Dictionary<string, TabHeaderView> _tabHeaders = [];
    private readonly Dictionary<string, GroupView> _groups = [];
    private readonly Dictionary<string, string> _groupContainers = [];
    private readonly Dictionary<string, SplitView> _splits = [];
    private readonly Dictionary<string, string> _splitContainers = [];
    private readonly Dictionary<string, DomElement> _floatingHosts = [];
    private readonly Dictionary<string, string> _regionShapes = [];
    private readonly Dictionary<string, string> _floatingShapes = [];
    private bool _disposed;

    public void RegisterRegion(string regionId, DomElement root)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _regions.Add(regionId, root
            .Class("dock-region")
            .Attr("data-dock-region", regionId));
    }

    public void RegisterWindow(BrowserDockWindowView window)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _windows.Add(window.Window.Id, window);
    }

    public void UnregisterWindow(string windowId, string tabId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_tabHeaders.Remove(tabId, out var header)) {
            header.Dispose();
        }
        _windows.Remove(windowId);
    }

    public void UnregisterRegion(string regionId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_regions.Remove(regionId, out var region)) {
            DisposeContainer(RegionContainerId(regionId));
            _regionShapes.Remove(regionId);
            region.Remove();
            region.Dispose();
        }
    }

    public void Apply(NotebookDockState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var region in state.Regions) {
            var shape = BuildRegionShape(region.Root);
            if (!_regionShapes.TryGetValue(region.Id, out var previous) || previous != shape) {
                RebuildRegion(region);
                _regionShapes[region.Id] = shape;
            }
        }

        var seenFloating = new HashSet<string>(state.FloatingHosts.Length);
        foreach (var floating in state.FloatingHosts) {
            seenFloating.Add(floating.Id);
            var shape = BuildFloatingShape(floating);
            if (!_floatingShapes.TryGetValue(floating.Id, out var previous) || previous != shape) {
                RebuildFloatingHost(floating);
                _floatingShapes[floating.Id] = shape;
            }
        }
        foreach (var floatingId in _floatingHosts.Keys.Where(id => !seenFloating.Contains(id)).ToArray()) {
            RemoveFloatingHost(floatingId);
        }

        SyncSplitRatios(state, FindSurfaceChildren(state));
        ApplyGroups(state);
    }

    private static Dictionary<string, int> FindSurfaceChildren(NotebookDockState state)
    {
        var surfaceChildren = new Dictionary<string, int>();
        foreach (var cellId in state.Windows.Values.Select(window => window.CellId).Distinct()) {
            if (NotebookDockLayout.FindSurfacePlacement(state, cellId) is { } placement) {
                surfaceChildren[placement.Split.Id] = placement.SurfaceIndex;
            }
        }
        return surfaceChildren;
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        foreach (var group in _groups.Values) {
            DetachGroup(group);
            group.Dispose();
        }
        _groups.Clear();
        _groupContainers.Clear();
        foreach (var split in _splits.Values) {
            split.Dispose();
        }
        _splits.Clear();
        _splitContainers.Clear();
        foreach (var header in _tabHeaders.Values) {
            header.Dispose();
        }
        _tabHeaders.Clear();
        foreach (var floating in _floatingHosts.Values) {
            floating.Dispose();
        }
        _floatingHosts.Clear();
        _regionShapes.Clear();
        _floatingShapes.Clear();
        foreach (var region in _regions.Values) {
            region.Remove();
            region.Dispose();
        }
        _regions.Clear();
        _windows.Clear();
        floatingLayer.Remove();
        floatingLayer.Dispose();
    }

    private void RebuildRegion(DockRegion region)
    {
        if (!_regions.TryGetValue(region.Id, out var root)) {
            return;
        }
        var containerId = RegionContainerId(region.Id);
        DisposeContainer(containerId);
        root.Text(string.Empty);

        var empty = region.Root is null;
        root.ToggleClass("is-empty", empty);
        if (region.Root is not null) {
            AppendNode(root, region.Root, containerId);
            return;
        }
        using var placeholder = DomElement.Create("div")
            .Class("empty")
            .Text("Drop a window here");
        using var preview = CreateDropPreview();
        root.Append(placeholder).Append(preview);
    }

    private void RebuildFloatingHost(DockFloatingHost floating)
    {
        RemoveFloatingHostElement(floating.Id);

        var containerId = FloatingContainerId(floating.Id);
        var root = DomElement.Create("div")
            .Class("floating-host")
            .Attr("data-dock-floating", floating.Id)
            .Attr(
                "style",
                $"left:{floating.X.ToString(CultureInfo.InvariantCulture)}px;"
                + $"top:{floating.Y.ToString(CultureInfo.InvariantCulture)}px;"
                + $"width:{floating.Width.ToString(CultureInfo.InvariantCulture)}px;"
                + $"height:{floating.Height.ToString(CultureInfo.InvariantCulture)}px");
        AppendNode(root, floating.Root, containerId);
        floatingLayer.Append(root);
        _floatingHosts[floating.Id] = root;
    }

    private void RemoveFloatingHost(string floatingId)
    {
        RemoveFloatingHostElement(floatingId);
        _floatingShapes.Remove(floatingId);
    }

    private void RemoveFloatingHostElement(string floatingId)
    {
        DisposeContainer(FloatingContainerId(floatingId));
        if (_floatingHosts.Remove(floatingId, out var root)) {
            root.Remove();
            root.Dispose();
        }
    }

    private void AppendNode(DomElement parent, DockNode node, string containerId)
    {
        if (node is DockTabGroup group) {
            var root = DomElement.Create("div")
                .Class("dock-group")
                .Attr("data-dock-group", group.Id);
            var tabs = DomElement.Create("div")
                .Class("cell-header")
                .Class("dock-tabs")
                .Attr("role", "tablist")
                .Attr("aria-label", "Docked windows");
            var tabList = DomElement.Create("div").Class("tab-list");
            var content = DomElement.Create("div").Class("group-content");
            using (var preview = CreateDropPreview()) {
                tabs.Append(tabList);
                root.Append(tabs).Append(content).Append(preview);
            }
            parent.Append(root);
            _groups.Add(group.Id, new(root, tabs, tabList, content));
            _groupContainers[group.Id] = containerId;
            return;
        }

        var split = (DockSplit)node;
        var splitRoot = DomElement.Create("div")
            .Class("dock-split")
            .Class(split.Axis == DockAxis.Horizontal
                ? "horizontal"
                : "vertical")
            .Attr("data-dock-split", split.Id);
        var first = DomElement.Create("div").Class("split-child");
        var second = DomElement.Create("div").Class("split-child");
        var separator = DomElement.Create("div")
            .Class("separator")
            .Attr("role", "separator")
            .Attr("aria-orientation", split.Axis == DockAxis.Horizontal
                ? "vertical"
                : "horizontal");
        AppendNode(first, split.First, containerId);
        AppendNode(second, split.Second, containerId);
        splitRoot.Append(first).Append(separator).Append(second);
        parent.Append(splitRoot);
        _splits.Add(split.Id, new(splitRoot, first, separator, second));
        _splitContainers[split.Id] = containerId;
    }

    private void SyncSplitRatios(
        NotebookDockState state,
        IReadOnlyDictionary<string, int> surfaceChildren)
    {
        foreach (var split in NotebookDockLayout.EnumerateSplits(state)) {
            if (!_splits.TryGetValue(split.Id, out var view)) {
                continue;
            }
            var ratio = split.Ratio;
            if (surfaceChildren.TryGetValue(split.Id, out var surfaceIndex)) {
                var surfaceShare = surfaceIndex == 0 ? ratio : 1 - ratio;
                view.Root.ToggleClass("surface", true);
                view.First.ToggleClass("surface-pane", surfaceIndex == 0);
                view.Second.ToggleClass("surface-pane", surfaceIndex == 1);
                view.First.ToggleClass("collapsed", surfaceIndex == 0 && surfaceShare <= 0);
                view.Second.ToggleClass("collapsed", surfaceIndex == 1 && surfaceShare <= 0);
                view.First.Attr("style", string.Empty);
                view.Second.Attr("style", string.Empty);
            } else {
                view.Root.ToggleClass("surface", false);
                view.First.ToggleClass("surface-pane", false);
                view.Second.ToggleClass("surface-pane", false);
                view.First.ToggleClass("collapsed", ratio <= 0);
                view.Second.ToggleClass("collapsed", ratio >= 1);
                SetShare(view.First, ratio);
                SetShare(view.Second, 1 - ratio);
            }
            view.Separator.ToggleClass("hidden", ratio is <= 0 or >= 1);
        }
    }

    private static void SetShare(DomElement child, double share)
        => child.Attr(
            "style",
            $"flex-grow:{share.ToString("0.###", CultureInfo.InvariantCulture)}");

    private void ApplyGroups(NotebookDockState state)
    {
        foreach (var group in NotebookDockLayout.EnumerateGroups(state)) {
            if (!_groups.TryGetValue(group.Id, out var groupView)) {
                continue;
            }
            var signature = BuildGroupSignature(group);
            if (groupView.Signature == signature) {
                continue;
            }
            DetachGroup(groupView);
            MountGroup(state, group, groupView);
        }
    }

    private static string BuildGroupSignature(DockTabGroup group)
        => group.ActiveTabId + "|" + string.Join(',', group.TabIds);

    private void DetachGroup(GroupView groupView)
    {
        foreach (var tabId in groupView.MountedTabs) {
            if (_tabHeaders.TryGetValue(tabId, out var header)) {
                header.Root.Remove();
            }
        }
        groupView.MountedTabs.Clear();
        if (groupView.ActiveWindowId is { } windowId
            && _windows.TryGetValue(windowId, out var window)) {
            window.Toolbar?.Remove();
            window.Content.Remove();
        }
        groupView.ActiveWindowId = null;
        groupView.Signature = null;
    }

    private void MountGroup(NotebookDockState state, DockTabGroup group, GroupView groupView)
    {
        foreach (var tabId in group.TabIds) {
            var tab = state.Tabs[tabId];
            var window = state.Windows[tab.WindowId];
            var header = EnsureTabHeader(tab, window);
            var active = tabId == group.ActiveTabId;
            header.Root.ToggleClass("active", active);
            header.Tab
                .ToggleClass("active", active)
                .Attr("aria-selected", active ? "true" : "false")
                .Attr("tabindex", active ? "0" : "-1");
            groupView.TabList.Append(header.Root);
            groupView.MountedTabs.Add(tabId);
        }

        var activeTab = state.Tabs[group.ActiveTabId];
        var activeWindow = _windows[activeTab.WindowId];
        if (activeWindow.Toolbar is { } toolbar) {
            groupView.Tabs.Append(toolbar);
        }
        activeWindow.Content
            .Attr("aria-labelledby", activeTab.Id)
            .Attr("aria-hidden", "false");
        groupView.Content.Append(activeWindow.Content);
        groupView.ActiveWindowId = activeWindow.Window.Id;
        groupView.Signature = BuildGroupSignature(group);
    }

    private TabHeaderView EnsureTabHeader(DockTab tab, DockWindow window)
    {
        if (_tabHeaders.TryGetValue(tab.Id, out var existing)) {
            return existing;
        }
        var root = DomElement.Create("div").Class("tab-entry");
        var header = DomElement.Create("button")
            .Class("tab")
            .Id(tab.Id)
            .Attr("type", "button")
            .Attr("role", "tab")
            .Attr("aria-controls", window.Id)
            .Attr("data-dock-tab", tab.Id)
            .Attr("data-dock-label", window.Title)
            .Attr("title", window.Title)
            .On("click", $"activate-tab:{tab.Id}")
            .Text(window.Title);
        root.Append(header);
        DomElement? close = null;
        if (window.Kind != DockWindowKind.Script) {
            close = DomElement.Create("button")
                .Class("close")
                .Attr("type", "button")
                .Attr("aria-label", $"Close {window.Title}")
                .Attr("title", $"Close {window.Title}")
                .On("click", $"close-window:{window.Id}")
                .Text("×");
            root.Append(close);
        }
        var view = new TabHeaderView(root, header, close);
        _tabHeaders.Add(tab.Id, view);
        return view;
    }

    private void DisposeContainer(string containerId)
    {
        foreach (var groupId in OwnedIds(_groupContainers, containerId)) {
            _groupContainers.Remove(groupId);
            if (_groups.Remove(groupId, out var group)) {
                DetachGroup(group);
                group.Dispose();
            }
        }
        foreach (var splitId in OwnedIds(_splitContainers, containerId)) {
            _splitContainers.Remove(splitId);
            if (_splits.Remove(splitId, out var split)) {
                split.Dispose();
            }
        }
    }

    private static string[] OwnedIds(
        Dictionary<string, string> containers,
        string containerId)
        => [.. containers
            .Where(pair => pair.Value == containerId)
            .Select(pair => pair.Key)];

    private static string RegionContainerId(string regionId) => "r:" + regionId;

    private static string FloatingContainerId(string floatingId) => "f:" + floatingId;

    private static DomElement CreateDropPreview()
        => DomElement.Create("div")
            .Class("drop-preview")
            .Attr("aria-hidden", "true");

    private static string BuildRegionShape(DockNode? root)
    {
        if (root is null) {
            return string.Empty;
        }
        var builder = new StringBuilder();
        AppendShape(builder, root);
        return builder.ToString();
    }

    private static string BuildFloatingShape(DockFloatingHost floating)
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

    private static void AppendShape(StringBuilder builder, DockNode node)
    {
        if (node is DockTabGroup group) {
            builder.Append("G(").Append(group.Id).Append(')');
            return;
        }
        var split = (DockSplit)node;
        builder.Append(split.Axis == DockAxis.Horizontal ? "H(" : "V(");
        AppendShape(builder, split.First);
        builder.Append('|');
        AppendShape(builder, split.Second);
        builder.Append(')');
    }

    private sealed class GroupView(
        DomElement root,
        DomElement tabs,
        DomElement tabList,
        DomElement content) : IDisposable
    {
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

    private sealed record TabHeaderView(
        DomElement Root,
        DomElement Tab,
        DomElement? Close) : IDisposable
    {
        public void Dispose()
        {
            Root.Remove();
            Close?.Dispose();
            Tab.Dispose();
            Root.Dispose();
        }
    }
}
