using System.Globalization;
using System.Text;

using Sia_Examples.Dom;

namespace Sia_Examples.Notebook;

public sealed class BrowserCellWorkspaceView(DomElement floatingLayer) : IDisposable
{
    private readonly Dictionary<string, DomElement> _regions = [];
    private readonly Dictionary<string, BrowserCellWindowView> _windows = [];
    private readonly Dictionary<string, TabHeaderView> _tabHeaders = [];
    private readonly Dictionary<string, GroupView> _groups = [];
    private readonly Dictionary<string, string> _groupContainers = [];
    private readonly Dictionary<string, SplitView> _splits = [];
    private readonly Dictionary<string, string> _splitContainers = [];
    private readonly Dictionary<string, DomElement> _floatingHosts = [];
    private readonly Dictionary<string, string> _regionShapes = [];
    private readonly Dictionary<string, string> _floatingShapes = [];
    private bool _disposed;

    public void RegisterRegion(string regionId, string cellId, DomElement root)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _regions.Add(regionId, root
            .Class("cell")
            .Attr("data-cell-region", regionId)
            .Attr("data-cell-owner", cellId));
    }

    public void RegisterWindow(BrowserCellWindowView window)
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

    public void Apply(NotebookCellState state)
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
        floatingLayer.Attr("data-cell-layout-revision", state.Revision.ToString(CultureInfo.InvariantCulture));
    }

    private static Dictionary<string, int> FindSurfaceChildren(NotebookCellState state)
    {
        var surfaceChildren = new Dictionary<string, int>();
        foreach (var cellId in state.Windows.Values.Select(window => window.CellId).Distinct()) {
            if (NotebookCellLayout.FindSurfacePlacement(state, cellId) is { } placement) {
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

    private void RebuildRegion(CellRegion region)
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

    private void RebuildFloatingHost(CellFloatingHost floating)
    {
        RemoveFloatingHostElement(floating.Id);

        var containerId = FloatingContainerId(floating.Id);
        var root = DomElement.Create("div")
            .Class("floating-host")
            .Attr("data-cell-floating", floating.Id)
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

    private void AppendNode(DomElement parent, CellNode node, string containerId)
    {
        if (node is CellTabGroup group) {
            var root = DomElement.Create("div")
                .Class("cell-group")
                .Attr("data-cell-group", group.Id);
            var tabs = DomElement.Create("div")
                .Class("cell-tabs")
                .Attr("role", "tablist")
                .Attr("aria-label", "Cell windows");
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

        var split = (CellSplit)node;
        var splitRoot = DomElement.Create("div")
            .Class("cell-split")
            .Class(split.Axis == CellAxis.Horizontal
                ? "horizontal"
                : "vertical")
            .Attr("data-cell-split", split.Id);
        var first = DomElement.Create("div").Class("split-child");
        var second = DomElement.Create("div").Class("split-child");
        var separator = DomElement.Create("div")
            .Class("separator")
            .Attr("role", "separator")
            .Attr("tabindex", "0")
            .Attr("aria-valuemin", "15")
            .Attr("aria-valuemax", "85")
            .Attr("aria-orientation", split.Axis == CellAxis.Horizontal
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
        NotebookCellState state,
        IReadOnlyDictionary<string, int> surfaceChildren)
    {
        foreach (var split in NotebookCellLayout.EnumerateSplits(state)) {
            if (!_splits.TryGetValue(split.Id, out var view)) {
                continue;
            }
            var ratio = split.Ratio;
            view.Separator.Attr(
                "aria-valuenow",
                Math.Round(ratio * 100).ToString(CultureInfo.InvariantCulture));
            if (surfaceChildren.TryGetValue(split.Id, out var surfaceIndex)) {
                var surfaceShare = surfaceIndex == 0 ? ratio : 1 - ratio;
                view.Root.ToggleClass("surface", true);
                view.Separator.Attr("aria-disabled", "true");
                view.First.ToggleClass("surface-pane", surfaceIndex == 0);
                view.Second.ToggleClass("surface-pane", surfaceIndex == 1);
                view.First.ToggleClass("collapsed", surfaceIndex == 0 && surfaceShare <= 0);
                view.Second.ToggleClass("collapsed", surfaceIndex == 1 && surfaceShare <= 0);
                view.First.Attr("style", string.Empty);
                view.Second.Attr("style", string.Empty);
            } else {
                view.Root.ToggleClass("surface", false);
                view.Separator.Attr("aria-disabled", "false");
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
            $"--cell-split-share:{share.ToString("0.###", CultureInfo.InvariantCulture)}");

    private void ApplyGroups(NotebookCellState state)
    {
        var changed = new List<(CellTabGroup Group, GroupView View)>();
        foreach (var group in NotebookCellLayout.EnumerateGroups(state)) {
            if (!_groups.TryGetValue(group.Id, out var groupView)) {
                continue;
            }
            foreach (var tabId in group.TabIds) {
                if (!state.Tabs.TryGetValue(tabId, out var tab)
                    || !state.Windows.TryGetValue(tab.WindowId, out var window)) {
                    continue;
                }
                EnsureTabHeader(tab, window, NotebookCellLayout.CanCloseTab(state, tabId));
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
            MountGroup(state, group, groupView);
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
        groupView.Adder?.Remove();
        groupView.Adder?.Dispose();
        groupView.Adder = null;
        if (groupView.ActiveWindowId is { } windowId
            && _windows.TryGetValue(windowId, out var window)) {
            window.Toolbar?.Remove();
            window.Content.Remove();
        }
        groupView.ActiveWindowId = null;
        groupView.Signature = null;
    }

    private void MountGroup(NotebookCellState state, CellTabGroup group, GroupView groupView)
    {
        var scriptCellId = NotebookCellLayout.GetScriptGroupCellId(state, group);
        foreach (var tabId in group.TabIds) {
            var tab = state.Tabs[tabId];
            var window = state.Windows[tab.WindowId];
            var closable = NotebookCellLayout.CanCloseTab(state, tabId);
            var header = EnsureTabHeader(tab, window, closable);
            var active = tabId == group.ActiveTabId;
            header.Root.ToggleClass("active", active);
            header.Tab
                .ToggleClass("active", active)
                .Attr("aria-selected", active ? "true" : "false")
                .Attr("tabindex", active ? "0" : "-1");
            groupView.TabList.Append(header.Root);
            groupView.MountedTabs.Add(tabId);
        }

        if (scriptCellId is not null) {
            var adder = DomElement.Create("button")
                .Class("icon-btn")
                .Class("tab-add")
                .Attr("type", "button")
                .Attr("aria-label", "Add script")
                .Attr("title", "Add script")
                .On("click", $"add-script:{scriptCellId}")
                .Text("+");
            groupView.TabList.Append(adder);
            groupView.Adder = adder;
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

    private TabHeaderView EnsureTabHeader(CellTab tab, CellWindow window, bool closable)
    {
        if (_tabHeaders.TryGetValue(tab.Id, out var existing)) {
            SyncLabel(existing, window);
            SyncCloseButton(existing, window, closable);
            return existing;
        }
        var root = DomElement.Create("div").Class("tab-entry");
        var header = DomElement.Create("button")
            .Class("tab")
            .Id(tab.Id)
            .Attr("type", "button")
            .Attr("role", "tab")
            .Attr("aria-controls", window.Id)
            .Attr("data-cell-tab", tab.Id)
            .Attr("data-cell-owner", window.CellId)
            .On("click", $"activate-tab:{tab.Id}");
        root.Append(header);
        var view = new TabHeaderView(root, header);
        SyncLabel(view, window);
        SyncCloseButton(view, window, closable);
        _tabHeaders.Add(tab.Id, view);
        return view;
    }

    private static void SyncLabel(TabHeaderView view, CellWindow window)
    {
        view.Tab
            .Attr("aria-label", window.Title)
            .Attr("title", window.Title)
            .Text(window.Title);
        view.Close?
            .Attr("aria-label", $"Close {window.Title}")
            .Attr("title", $"Close {window.Title}");
    }

    private static void SyncCloseButton(TabHeaderView view, CellWindow window, bool closable)
    {
        view.Root.ToggleClass("has-close", closable);
        if (closable == (view.Close is not null)) {
            return;
        }
        if (!closable) {
            view.Close?.Remove();
            view.Close?.Dispose();
            view.Close = null;
            return;
        }
        var payload = window.Kind == CellWindowKind.Script
            ? $"remove-script:{window.SourceId}"
            : $"close-window:{window.Id}";
        view.Close = DomElement.Create("button")
            .Class("close")
            .Attr("type", "button")
            .Attr("aria-label", $"Close {window.Title}")
            .Attr("title", $"Close {window.Title}")
            .On("click", payload)
            .Text("×");
        view.Root.Append(view.Close);
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

    private static string BuildRegionShape(CellNode? root)
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

        public DomElement? Adder { get; set; }

        public List<string> MountedTabs { get; } = [];

        public void Dispose()
        {
            Adder?.Dispose();
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
