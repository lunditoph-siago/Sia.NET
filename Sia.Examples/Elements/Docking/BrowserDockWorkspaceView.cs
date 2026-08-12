using System.Globalization;
using System.Text;

using Sia_Examples.Dom;

namespace Sia_Examples.Notebook;

public sealed class BrowserDockWorkspaceView : IDisposable
{
    private readonly DomElement _floatingLayer;
    private readonly Dictionary<string, DomElement> _regions = [];
    private readonly Dictionary<string, BrowserDockWindowView> _windows = [];
    private readonly Dictionary<string, TabHeaderView> _tabHeaders = [];
    private readonly Dictionary<string, GroupView> _groups = [];
    private readonly List<DomElement> _floatingHosts = [];
    private string? _shape;
    private bool _disposed;

    public BrowserDockWorkspaceView(DomElement floatingLayer)
    {
        _floatingLayer = floatingLayer;
    }

    public void RegisterRegion(string regionId, DomElement root)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _regions.Add(regionId, root
            .Class("cell")
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
            region.Remove();
            region.Dispose();
        }
    }

    public void Apply(NotebookDockState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var shape = BuildShape(state);
        if (_shape != shape) {
            RebuildStructure(state);
            _shape = shape;
        }
        ApplyGroups(state);
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        DetachReusableElements();
        foreach (var header in _tabHeaders.Values) {
            header.Dispose();
        }
        _tabHeaders.Clear();
        DisposeStructure();
        foreach (var region in _regions.Values) {
            region.Remove();
            region.Dispose();
        }
        _regions.Clear();
        _windows.Clear();
        _floatingLayer.Remove();
        _floatingLayer.Dispose();
    }

    private void RebuildStructure(NotebookDockState state)
    {
        DetachReusableElements();
        foreach (var region in _regions.Values) {
            region.Text(string.Empty);
        }
        _floatingLayer.Text(string.Empty);
        DisposeStructure();

        foreach (var region in state.Regions) {
            if (!_regions.TryGetValue(region.Id, out var root)) {
                continue;
            }
            var empty = region.Root is null;
            root.ToggleClass("is-empty", empty);
            if (region.Root is not null) {
                AppendNode(root, region.Root);
                continue;
            }
            using var placeholder = DomElement.Create("div")
                .Class("empty")
                .Text("Drop a window here");
            using var preview = CreateDropPreview();
            root.Append(placeholder).Append(preview);
        }

        foreach (var floating in state.FloatingHosts) {
            var root = DomElement.Create("div")
                .Class("cell")
                .Class("floating-host")
                .Attr("data-dock-floating", floating.Id)
                .Attr(
                    "style",
                    $"left:{floating.X.ToString(CultureInfo.InvariantCulture)}px;"
                    + $"top:{floating.Y.ToString(CultureInfo.InvariantCulture)}px;"
                    + $"width:{floating.Width.ToString(CultureInfo.InvariantCulture)}px;"
                    + $"height:{floating.Height.ToString(CultureInfo.InvariantCulture)}px");
            AppendNode(root, floating.Root);
            _floatingLayer.Append(root);
            _floatingHosts.Add(root);
        }
    }

    private void AppendNode(DomElement parent, DockNode node)
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
            return;
        }

        var split = (DockSplit)node;
        using var splitRoot = DomElement.Create("div")
            .Class("dock-split")
            .Class(split.Axis == DockAxis.Horizontal
                ? "horizontal"
                : "vertical")
            .Attr("data-dock-split", split.Id)
            .Attr(
                "style",
                $"--dock-ratio:{split.Ratio.ToString(CultureInfo.InvariantCulture)}");
        using var first = DomElement.Create("div").Class("split-child");
        using var second = DomElement.Create("div").Class("split-child");
        using var separator = DomElement.Create("div")
            .Class("separator")
            .Attr("role", "separator")
            .Attr("aria-orientation", split.Axis == DockAxis.Horizontal
                ? "vertical"
                : "horizontal");
        AppendNode(first, split.First);
        AppendNode(second, split.Second);
        splitRoot.Append(first).Append(separator).Append(second);
        parent.Append(splitRoot);
    }

    private void ApplyGroups(NotebookDockState state)
    {
        DetachReusableElements();
        foreach (var group in NotebookDockLayout.EnumerateGroups(state)) {
            if (!_groups.TryGetValue(group.Id, out var groupView)) {
                continue;
            }
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
        }
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

    private void DetachReusableElements()
    {
        foreach (var header in _tabHeaders.Values) {
            header.Root.Remove();
        }
        foreach (var window in _windows.Values) {
            window.Toolbar?.Remove();
            window.Content.Remove();
        }
    }

    private void DisposeStructure()
    {
        foreach (var group in _groups.Values) {
            group.Dispose();
        }
        _groups.Clear();
        foreach (var floating in _floatingHosts) {
            floating.Dispose();
        }
        _floatingHosts.Clear();
    }

    private static DomElement CreateDropPreview()
        => DomElement.Create("div")
            .Class("drop-preview")
            .Attr("aria-hidden", "true");

    private static string BuildShape(NotebookDockState state)
    {
        var builder = new StringBuilder();
        foreach (var region in state.Regions) {
            builder.Append("R(").Append(region.Id).Append(':');
            if (region.Root is not null) {
                AppendShape(builder, region.Root);
            }
            builder.Append(')');
        }
        foreach (var floating in state.FloatingHosts) {
            builder
                .Append("F(")
                .Append(floating.Id).Append(':')
                .Append(floating.X).Append(',')
                .Append(floating.Y).Append(',')
                .Append(floating.Width).Append(',')
                .Append(floating.Height).Append(':');
            AppendShape(builder, floating.Root);
            builder.Append(')');
        }
        return builder.ToString();
    }

    private static void AppendShape(StringBuilder builder, DockNode node)
    {
        if (node is DockTabGroup group) {
            builder.Append("G(").Append(group.Id).Append(':');
            foreach (var tabId in group.TabIds) {
                builder.Append(tabId).Append(',');
            }
            builder.Append(')');
            return;
        }
        var split = (DockSplit)node;
        builder.Append(split.Axis == DockAxis.Horizontal ? "H(" : "V(");
        AppendShape(builder, split.First);
        builder.Append('|');
        AppendShape(builder, split.Second);
        builder.Append(')');
    }

    private sealed record GroupView(
        DomElement Root,
        DomElement Tabs,
        DomElement TabList,
        DomElement Content) : IDisposable
    {
        public void Dispose()
        {
            Content.Dispose();
            TabList.Dispose();
            Tabs.Dispose();
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
