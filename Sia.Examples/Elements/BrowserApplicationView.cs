namespace Sia_Examples;

public sealed class BrowserApplicationView :
    IRenderHost<ExampleItemView>,
    IDisposable
{
    private readonly BrowserElement _sidebar = BrowserElement.Find("sidebar");
    private readonly BrowserElement _frameworkAssemblies =
        BrowserElement.Find("framework-assemblies");
    private readonly SortedDictionary<int, ExampleNode> _examples = [];

    public void SetFrameworkAssemblyNames(IEnumerable<string> names)
    {
        foreach (var name in names.OrderBy(
            static name => name,
            StringComparer.OrdinalIgnoreCase)) {
            using var option = BrowserElement.Create("option").Attr("value", name);
            _frameworkAssemblies.Append(option);
        }
    }

    public void Upsert(in ExampleItemView view)
    {
        if (!_examples.TryGetValue(view.Index, out var node)) {
            node = ExampleNode.Create(view.Index);
            _examples.Add(view.Index, node);
            _sidebar.InsertBefore(node.Root, FindNext(view.Index)?.Root);
        }
        node.Update(view);
    }

    public void Remove(in ExampleItemView view)
    {
        if (_examples.Remove(view.Index, out var node)) {
            node.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var node in _examples.Values) {
            node.Dispose();
        }
        _examples.Clear();
        _frameworkAssemblies.Dispose();
        _sidebar.Dispose();
    }

    private ExampleNode? FindNext(int index)
    {
        foreach (var (candidateIndex, node) in _examples) {
            if (candidateIndex > index) {
                return node;
            }
        }
        return null;
    }

    private sealed class ExampleNode(
        BrowserElement root,
        BrowserElement name,
        BrowserElement description) : IDisposable
    {
        public BrowserElement Root { get; } = root;

        public static ExampleNode Create(int index)
        {
            var root = BrowserElement.Create("button")
                .Class("example-btn")
                .On("click", $"select:{index}");
            var name = BrowserElement.Create("span").Class("name");
            var description = BrowserElement.Create("span").Class("desc");
            root.Append(name).Append(description);
            return new(root, name, description);
        }

        public void Update(ExampleItemView view)
        {
            name.Text(view.Name);
            description.Text(view.Description);
            Root.ToggleClass("active", view.Active);
        }

        public void Dispose()
        {
            Root.Remove();
            description.Dispose();
            name.Dispose();
            Root.Dispose();
        }
    }
}
