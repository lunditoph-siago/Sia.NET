namespace Sia_Examples.Notebook;

public sealed class BrowserPackagePanel : IDisposable
{
    private readonly BrowserElement _container = BrowserElement.Find("packages-popover");
    private readonly BrowserElement _badge = BrowserElement.Find("packages-badge");
    private readonly BrowserElement _body = BrowserElement.Create("div").Class("packages-body");
    private readonly BrowserElement _empty = BrowserElement.Create("div")
        .Class("packages-empty")
        .Text("No packages declared or added yet.");
    private readonly BrowserElement _list = BrowserElement.Create("div");
    private readonly Dictionary<PackageIdentity, PackageNode> _nodes = [];

    public BrowserPackagePanel()
    {
        _body.Append(_empty).Append(_list);
        using var addRow = BrowserElement.Create("div").Class("package-add");
        using var id = BrowserElement.Create("input")
            .Class("package-add-input")
            .Id("package-add-id")
            .Attr("placeholder", "Package Id")
            .Attr("list", "framework-assemblies");
        using var version = BrowserElement.Create("input")
            .Class("package-add-input")
            .Id("package-add-version")
            .Attr("placeholder", "Version (optional)");
        using var addNuGet = BrowserElement.Create("button")
            .Class("btn")
            .On("click", "add-package:NuGet")
            .Text("+ NuGet");
        using var addFramework = BrowserElement.Create("button")
            .Class("btn")
            .On("click", "add-package:Framework")
            .Text("+ Framework");
        addRow.Append(id).Append(version).Append(addNuGet).Append(addFramework);
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
        PackageSource Source,
        string Id,
        string? Version)
    {
        public static PackageIdentity From(PackageRef package)
            => new(package.Source, package.Id, package.Version);
    }

    private sealed class PackageNode(
        BrowserElement root,
        BrowserElement row,
        BrowserElement source,
        BrowserElement id,
        BrowserElement state,
        BrowserElement error) : IDisposable
    {
        public BrowserElement Root { get; } = root;

        public int Index { get; private set; } = int.MaxValue;

        public static PackageNode Create()
        {
            var root = BrowserElement.Create("div");
            var row = BrowserElement.Create("div").Class("package");
            var source = BrowserElement.Create("span").Class("package-source");
            var id = BrowserElement.Create("span").Class("package-id");
            var state = BrowserElement.Create("span").Class("package-state");
            var error = BrowserElement.Create("div").Class("package-error").Class("hidden");
            row.Append(source).Append(id).Append(state);
            root.Append(row).Append(error);
            return new(root, row, source, id, state, error);
        }

        public void Update(PackageView view)
        {
            var status = view.Status;
            Index = view.Index;
            source.Text(status.Package.Source == PackageSource.NuGet ? "NuGet" : "Framework");
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
            source.Dispose();
            row.Dispose();
            Root.Dispose();
        }
    }
}
