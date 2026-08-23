using System.Globalization;
using System.Text;

using Sia_Examples.Dom;

namespace Sia_Examples.Notebook;

internal static class CellLayoutDom
{
    public static DomElement CreateDropPreview()
        => DomElement.Create("div")
            .Class("drop-preview")
            .Attr("aria-hidden", "true");

    public static void SetShare(DomElement child, double share)
        => child.Attr(
            "style",
            $"--cell-split-share:{share.ToString("0.###", CultureInfo.InvariantCulture)}");

    public static string BuildGroupSignature(CellTabGroup group)
        => group.ActiveTabId + "|" + string.Join(',', group.TabIds);

    public static string[] OwnedIds(Dictionary<string, string> containers, string containerId)
        => [.. containers
            .Where(pair => pair.Value == containerId)
            .Select(pair => pair.Key)];

    public static string RegionContainerId(string regionId) => "r:" + regionId;

    public static string FloatingContainerId(string floatingId) => "f:" + floatingId;

    public static string BuildNodeShape(CellNode? root)
    {
        if (root is null) {
            return string.Empty;
        }
        var builder = new StringBuilder();
        AppendShape(builder, root);
        return builder.ToString();
    }

    public static string BuildFloatingShape(CellFloatingHost floating)
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

    public static DomElement CreateFloatingHostElement(CellFloatingHost floating)
        => DomElement.Create("div")
            .Class("floating-host")
            .Attr("data-cell-floating", floating.Id)
            .Attr(
                "style",
                $"left:{floating.X.ToString(CultureInfo.InvariantCulture)}px;"
                + $"top:{floating.Y.ToString(CultureInfo.InvariantCulture)}px;"
                + $"width:{floating.Width.ToString(CultureInfo.InvariantCulture)}px;"
                + $"height:{floating.Height.ToString(CultureInfo.InvariantCulture)}px");

    public static void AppendNode(
        DomElement parent,
        CellNode node,
        string containerId,
        string tabsAriaLabel,
        Dictionary<string, GroupView> groups,
        Dictionary<string, string> groupContainers,
        Dictionary<string, SplitView> splits,
        Dictionary<string, string> splitContainers,
        string? separatorAriaLabel = null)
    {
        if (node is CellTabGroup group) {
            var root = DomElement.Create("div")
                .Class("cell-group")
                .Attr("data-cell-group", group.Id);
            var tabs = DomElement.Create("div")
                .Class("cell-tabs")
                .Attr("role", "tablist")
                .Attr("aria-label", tabsAriaLabel);
            var tabList = DomElement.Create("div").Class("tab-list");
            var content = DomElement.Create("div").Class("group-content");
            using (var preview = CreateDropPreview()) {
                tabs.Append(tabList);
                root.Append(tabs).Append(content).Append(preview);
            }
            parent.Append(root);
            groups.Add(group.Id, new(group.Id, root, tabs, tabList, content));
            groupContainers[group.Id] = containerId;
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
            .Attr("aria-valuemin", "15")
            .Attr("aria-valuemax", "85")
            .Attr("aria-orientation", split.Axis == CellAxis.Horizontal ? "vertical" : "horizontal");
        if (separatorAriaLabel is not null) {
            separator.Attr("aria-label", separatorAriaLabel);
        }
        AppendNode(
            first, split.First, containerId, tabsAriaLabel, groups, groupContainers, splits, splitContainers,
            separatorAriaLabel);
        AppendNode(
            second, split.Second, containerId, tabsAriaLabel, groups, groupContainers, splits, splitContainers,
            separatorAriaLabel);
        splitRoot.Append(first).Append(separator).Append(second);
        parent.Append(splitRoot);
        splits.Add(split.Id, new(splitRoot, first, separator, second));
        splitContainers[split.Id] = containerId;
    }

    public static void DisposeContainer(
        string containerId,
        Dictionary<string, string> groupContainers,
        Dictionary<string, GroupView> groups,
        Action<GroupView> detachGroup,
        Dictionary<string, string> splitContainers,
        Dictionary<string, SplitView> splits)
    {
        foreach (var groupId in OwnedIds(groupContainers, containerId)) {
            groupContainers.Remove(groupId);
            if (groups.Remove(groupId, out var group)) {
                detachGroup(group);
                group.Dispose();
            }
        }
        foreach (var splitId in OwnedIds(splitContainers, containerId)) {
            splitContainers.Remove(splitId);
            if (splits.Remove(splitId, out var split)) {
                split.Dispose();
            }
        }
    }

    public static void SyncFloatingHosts(
        IEnumerable<CellFloatingHost> floatingHosts,
        Dictionary<string, string> floatingShapes,
        IEnumerable<string> mountedFloatingIds,
        Action<CellFloatingHost> rebuild,
        Action<string> remove)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var floating in floatingHosts) {
            seen.Add(floating.Id);
            var shape = BuildFloatingShape(floating);
            if (!floatingShapes.TryGetValue(floating.Id, out var previous) || previous != shape) {
                rebuild(floating);
                floatingShapes[floating.Id] = shape;
            }
        }
        foreach (var floatingId in mountedFloatingIds.Where(id => !seen.Contains(id)).ToArray()) {
            remove(floatingId);
        }
    }

    public static void SyncGroups(
        IEnumerable<CellTabGroup> groups,
        Dictionary<string, GroupView> groupViews,
        Action<CellTabGroup> ensureTabs,
        Action<GroupView> detachGroup,
        Action<CellTabGroup, GroupView> mountGroup)
    {
        var changed = new List<(CellTabGroup Group, GroupView View)>();
        foreach (var group in groups) {
            if (!groupViews.TryGetValue(group.Id, out var groupView)) {
                continue;
            }
            ensureTabs(group);
            var signature = BuildGroupSignature(group);
            if (groupView.Signature == signature) {
                continue;
            }
            changed.Add((group, groupView));
        }
        foreach (var (_, groupView) in changed) {
            detachGroup(groupView);
        }
        foreach (var (group, groupView) in changed) {
            mountGroup(group, groupView);
        }
    }
}

internal sealed class GroupView(
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

internal sealed record SplitView(
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
