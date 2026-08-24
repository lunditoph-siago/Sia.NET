using Sia_Examples.Dom;

namespace Sia_Examples.Notebook;

public sealed class BrowserPackagePanel : IDisposable
{
    private readonly DomElement _container = DomElement.Find("packages-popover");
    private readonly DomElement _badge = DomElement.Find("packages-badge");
    private readonly DomElement _body = DomElement.Create("div").Class("packages-body");
    private readonly DomElement _empty = DomElement.Create("div")
        .Class("packages-empty")
        .Text("No packages declared or added yet.");
    private readonly DomElement _list = DomElement.Create("div");
    private readonly Dictionary<PackageIdentity, PackageNode> _nodes = [];

    public BrowserPackagePanel()
    {
        _body.Append(_empty).Append(_list);
        using var addRow = DomElement.Create("div").Class("package-add");
        using var id = DomElement.Create("input")
            .Class("package-add-input")
            .Id("package-add-id")
            .Attr("placeholder", "Package Id");
        using var version = DomElement.Create("input")
            .Class("package-add-input")
            .Id("package-add-version")
            .Attr("placeholder", "Version (optional)");
        using var addNuGet = DomElement.Create("button")
            .Class("btn")
            .On("click", "add-package:NuGet")
            .Text("+ NuGet");
        addRow.Append(id).Append(version).Append(addNuGet);
        _body.Append(addRow);
        _container.Text(string.Empty).Append(_body);
    }

    public void Upsert(PackageView view)
    {
        var identity = PackageIdentity.From(view.Status.Package);
        if (!_nodes.TryGetValue(identity, out var node)) {
            node = PackageNode.Create();
            _nodes.Add(identity, node);
        }
        node.Update(view);
        var next = _nodes.Values
            .Where(candidate => !ReferenceEquals(candidate, node) && candidate.Index > view.Index)
            .OrderBy(static candidate => candidate.Index)
            .FirstOrDefault();
        _list.InsertBefore(node.Root, next?.Root);
    }

    public void Remove(PackageView view)
    {
        if (_nodes.Remove(PackageIdentity.From(view.Status.Package), out var node)) {
            node.Dispose();
        }
    }

    public void UpdateCount(int count)
    {
        _badge.Text(count == 0 ? string.Empty : count.ToString());
        _empty.ToggleClass("hidden", count != 0);
    }

    public void Dispose()
    {
        foreach (var node in _nodes.Values) {
            node.Dispose();
        }
        _nodes.Clear();
        _body.Remove();
        _list.Dispose();
        _empty.Dispose();
        _body.Dispose();
        _badge.Text(string.Empty);
        _badge.Dispose();
        _container.Text(string.Empty);
        _container.Dispose();
    }

    private readonly record struct PackageIdentity(
        string Id,
        string? Version)
    {
        public static PackageIdentity From(PackageRef package)
            => new(package.Id, package.Version);
    }

    private sealed class PackageNode(
        DomElement root,
        DomElement row,
        DomElement id,
        DomElement state,
        DomElement error) : IDisposable
    {
        public DomElement Root { get; } = root;

        public int Index { get; private set; } = int.MaxValue;

        public static PackageNode Create()
        {
            var root = DomElement.Create("div");
            var row = DomElement.Create("div").Class("package");
            var id = DomElement.Create("span").Class("package-id");
            var state = DomElement.Create("span").Class("package-state");
            var error = DomElement.Create("div").Class("package-error").Class("hidden");
            row.Append(id).Append(state);
            root.Append(row).Append(error);
            return new(root, row, id, state, error);
        }

        public void Update(PackageView view)
        {
            var status = view.Status;
            Index = view.Index;
            id.Text(status.Package.Id
                + (status.Package.Version is { } version ? $" ({version})" : string.Empty));
            state.Text(status.State switch {
                PackageLoadState.Loading => "loading…",
                PackageLoadState.Loaded => "loaded",
                PackageLoadState.Failed => "failed",
                _ => status.State.ToString(),
            });
            row.ToggleClass("package-loading", status.State == PackageLoadState.Loading);
            row.ToggleClass("package-loaded", status.State == PackageLoadState.Loaded);
            row.ToggleClass("package-failed", status.State == PackageLoadState.Failed);
            error
                .Text(status.Error ?? string.Empty)
                .ToggleClass("hidden", string.IsNullOrEmpty(status.Error));
        }

        public void Dispose()
        {
            Root.Remove();
            error.Dispose();
            state.Dispose();
            id.Dispose();
            row.Dispose();
            Root.Dispose();
        }
    }
}
