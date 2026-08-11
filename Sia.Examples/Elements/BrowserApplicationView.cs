using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples;

public sealed class BrowserApplicationView :
    IRenderHost<ExampleItemView>,
    IDisposable
{
    private readonly DomElement _builtInNotebooks = DomElement.Find("built-in-notebooks");
    private readonly DomElement _userNotebooks = DomElement.Find("user-notebooks");
    private readonly DomElement _frameworkAssemblies =
        DomElement.Find("framework-assemblies");
    private readonly SortedDictionary<int, ExampleNode> _examples = [];
    private readonly DomElement _newButton;
    private readonly DomElement _notebookBar;
    private readonly DomElement _status;
    private readonly DomElement _deleteButton;
    private readonly DomElement _conflictBanner;
    private readonly DomElement _conflictMessage;

    public BrowserApplicationView()
    {
        using (var actions = DomElement.Find("sidebar-actions")) {
            _newButton = CreateIconButton("+", "New notebook", "new-notebook");
            actions.Append(_newButton);
        }

        using (var actions = DomElement.Find("notebook-actions")) {
            _notebookBar = DomElement.Create("div")
                .Class("notebook-bar")
                .Class("hidden");
            _status = DomElement.Create("span").Class("notebook-status");
            using var saveButton = CreateIconButton(
                "▣",
                "Save notebook",
                "save-notebook");
            _deleteButton = CreateIconButton(
                "×",
                "Delete notebook",
                "delete-notebook").Class("danger");
            _notebookBar.Append(_status).Append(saveButton).Append(_deleteButton);
            actions.Append(_notebookBar);
        }

        using var content = DomElement.Find("content");
        using var notebook = DomElement.Find("notebook");
        _conflictBanner = DomElement.Create("div")
            .Class("save-conflict-banner")
            .Class("hidden");
        _conflictMessage = DomElement.Create("span").Class("save-conflict-message");
        using var overwrite = DomElement.Create("button")
            .Class("btn")
            .On("click", "resolve-conflict-overwrite")
            .Text("Overwrite mine");
        using var reload = DomElement.Create("button")
            .Class("btn")
            .On("click", "resolve-conflict-reload")
            .Text("Discard mine, reload theirs");
        _conflictBanner.Append(_conflictMessage).Append(overwrite).Append(reload);
        content.InsertBefore(_conflictBanner, notebook);
    }

    public void SetFrameworkAssemblyNames(IEnumerable<string> names)
    {
        foreach (var name in names.OrderBy(
            static name => name,
            StringComparer.OrdinalIgnoreCase)) {
            using var option = DomElement.Create("option").Attr("value", name);
            _frameworkAssemblies.Append(option);
        }
    }

    public void ShowNotebookBar(NotebookInfo info, string? version)
    {
        _notebookBar.ToggleClass("hidden", false);
        _deleteButton.ToggleClass(
            "hidden",
            !(info.Origin == NotebookOrigin.User && info.Key.Length > 0));
        _status.Text(info.Origin switch {
            NotebookOrigin.BuiltIn => "built-in",
            _ when info.Key.Length == 0 => "unsaved",
            _ => $"saved · {version}",
        });
        HideConflict();
    }

    public void HideNotebookBar()
    {
        _notebookBar.ToggleClass("hidden", true);
        HideConflict();
    }

    public void ShowConflict(NotebookConflictException error)
    {
        _conflictMessage.Text(error.ActualVersion is { } actual
            ? $"Storage changed from {error.ExpectedVersion} to {actual}."
            : "This notebook was deleted from storage.");
        _conflictBanner.ToggleClass("hidden", false);
    }

    public void HideConflict()
        => _conflictBanner.ToggleClass("hidden", true);

    public void Upsert(in ExampleItemView view)
    {
        if (!_examples.TryGetValue(view.Index, out var node)) {
            node = ExampleNode.Create(view.Index, view.Origin);
            _examples.Add(view.Index, node);
            var parent = view.Origin == NotebookOrigin.BuiltIn
                ? _builtInNotebooks
                : _userNotebooks;
            parent.InsertBefore(node.Root, FindNext(view.Index, view.Origin)?.Root);
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
        _conflictMessage.Dispose();
        _conflictBanner.Dispose();
        _deleteButton.Dispose();
        _status.Dispose();
        _notebookBar.Dispose();
        _newButton.Dispose();
        _frameworkAssemblies.Dispose();
        _userNotebooks.Dispose();
        _builtInNotebooks.Dispose();
    }

    private ExampleNode? FindNext(int index, NotebookOrigin origin)
    {
        foreach (var (candidateIndex, node) in _examples) {
            if (candidateIndex > index && node.Origin == origin) {
                return node;
            }
        }
        return null;
    }

    private static DomElement CreateIconButton(
        string icon,
        string label,
        string payload)
        => DomElement.Create("button")
            .Class("icon-btn")
            .Attr("type", "button")
            .Attr("aria-label", label)
            .Attr("title", label)
            .On("click", payload)
            .Text(icon);

    private sealed class ExampleNode(
        DomElement root,
        DomElement name,
        DomElement description,
        NotebookOrigin origin) : IDisposable
    {
        public DomElement Root { get; } = root;

        public NotebookOrigin Origin { get; } = origin;

        public static ExampleNode Create(int index, NotebookOrigin origin)
        {
            var root = DomElement.Create("button")
                .Class("example-btn")
                .On("click", $"select:{index}");
            using var icon = DomElement.Create("span")
                .Class("example-icon")
                .Attr("aria-hidden", "true")
                .Text("#");
            using var labels = DomElement.Create("span").Class("example-labels");
            var name = DomElement.Create("span").Class("name");
            var description = DomElement.Create("span").Class("desc");
            labels.Append(name).Append(description);
            root.Append(icon).Append(labels);
            return new(root, name, description, origin);
        }

        public void Update(ExampleItemView view)
        {
            name.Text(view.Name);
            description.Text(view.Description);
            Root
                .Attr("aria-label", $"Open {view.Name}: {view.Description}")
                .Attr("title", view.Description)
                .ToggleClass("active", view.Active);
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
