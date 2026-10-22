using Sia.Examples.Dom;
using Sia.Examples.Editor;
using Sia.Examples.Notebook;

namespace Sia.Examples;

public sealed class BrowserApplicationView :
    IRenderHost<ExampleItemView>,
    IDisposable
{
    private readonly DomElement _builtInNotebooks = DomElement.Find("built-in-notebooks");
    private readonly DomElement _userNotebooks = DomElement.Find("user-notebooks");
    private readonly SortedDictionary<int, ExampleNode> _examples = [];
    private readonly DomElement _newButton;
    private readonly DomElement _home;
    private readonly DomElement[] _navigationButtons;
    private readonly DomElement _homeNavigation = DomElement.Find("application-home");
    private readonly DomElement _contentHeader = DomElement.Find("content-header");
    private readonly DomElement _pageTitle = DomElement.Find("page-title");
    private readonly DomElement _headerPackages = DomElement.Find("header-packages");
    private readonly DomElement _loading;
    private readonly DomElement _notebookBar;
    private readonly DomElement _status;
    private readonly DomElement _saveButton;
    private readonly DomElement _deleteButton;
    private readonly DomElement _conflictBanner;
    private readonly DomElement _conflictMessage;
    private readonly List<DomElement> _noticeButtons = [];

    internal DomElement HomeNavigation => _homeNavigation;

    public BrowserApplicationView()
    {
        _home = DomElement.Find("notebook-home");
        _navigationButtons = [
            BindAction("home-start-guide", "select:0"),
            BindAction("home-open-editor", "open-editor"),
        ];
        _homeNavigation.On("click", "application-home");

        using (var actions = DomElement.Find("user-notebook-actions")) {
            _newButton = CreateIconButton("+", "New notebook", "new-notebook")
                .Attr("data-sidebar-dismiss", string.Empty);
            actions.Append(_newButton);
        }

        using (var actions = DomElement.Find("notebook-actions")) {
            _notebookBar = DomElement.Create("div")
                .Class("notebook-bar")
                .Class("hidden");
            _status = DomElement.Create("span").Class("notebook-status").Attr("role", "status");
            _saveButton = CreateIconButton(
                "▣",
                "Save notebook",
                "save-notebook");
            _deleteButton = CreateIconButton(
                "×",
                "Delete notebook",
                "delete-notebook").Class("danger");
            _notebookBar.Append(_status).Append(_saveButton).Append(_deleteButton);
            actions.Append(_notebookBar);
        }

        using var content = DomElement.Find("content");
        using var notebook = DomElement.Find("notebook");
        _conflictBanner = DomElement.Create("div")
            .Class("save-conflict-banner")
            .Class("hidden")
            .Attr("role", "alert");
        _conflictMessage = DomElement.Create("span").Class("save-conflict-message");
        _conflictBanner.Append(_conflictMessage);
        content.InsertBefore(_conflictBanner, notebook);
        _loading = DomElement.Create("div")
            .Class("notebook-loading")
            .Class("hidden")
            .Attr("role", "status");
        content.InsertBefore(_loading, notebook);
    }

    public void ShowNotebookBar(NotebookInfo info, string? version)
    {
        SetPage(info.Name, notebook: true);
        _notebookBar.ToggleClass("hidden", false);
        var saveLabel = info.Origin == NotebookOrigin.BuiltIn ? "Save a copy" : "Save notebook";
        _saveButton.Attr("aria-label", saveLabel).Attr("title", saveLabel);
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

    internal void ShowHome()
    {
        using var notebook = DomElement.Find("notebook");
        notebook.Text(string.Empty).Append(_home);
        HideNotebookBar();
        SetPage(null);
    }

    internal void ShowEditor() => SetPage("Editor Lab");

    internal void ShowNotebookTitle(string title) => SetPage(title, notebook: true);

    private void SetPage(string? title, bool notebook = false)
    {
        _contentHeader.Append(_homeNavigation);
        _homeNavigation.ToggleClass("hidden", title is null);
        _headerPackages.ToggleClass("hidden", !notebook);
        _pageTitle.Text(title is null ? "Sia.NET Examples" : $"{title} · Sia.NET Examples");
    }

    internal void SetNotebookLoading(string? name)
    {
        _loading.Text(name is null ? string.Empty : $"Loading {name}…")
            .ToggleClass("hidden", name is null);
        DomRuntime.Flush();
    }

    public void ShowConflict(WorkspaceConflictException error)
    {
        ShowNotice(error.ActualVersion is { } actual
            ? $"Storage changed from {error.ExpectedVersion} to {actual}."
            : "This notebook was deleted from storage.",
            ("Overwrite mine", "resolve-conflict-overwrite"),
            ("Discard mine, reload theirs", "resolve-conflict-reload"));
    }

    internal void ShowNotice(string message, params (string Label, string Payload)[] actions)
    {
        ClearNoticeButtons();
        _conflictMessage.Text(message);
        foreach (var (label, payload) in actions) {
            var button = DomElement.Create("button")
                .Class("btn")
                .Attr("type", "button")
                .On("click", payload)
                .Text(label);
            _noticeButtons.Add(button);
            _conflictBanner.Append(button);
        }
        _conflictBanner.ToggleClass("hidden", false);
    }

    internal void ShowConfirmation(string message, params (string Label, string Payload)[] actions)
    {
        ShowNotice(message, actions);
        _noticeButtons[^1].Attr("data-notice-cancel", string.Empty).Focus();
    }

    public void HideConflict()
    {
        _conflictBanner.ToggleClass("hidden", true);
        ClearNoticeButtons();
    }

    private void ClearNoticeButtons()
    {
        foreach (var button in _noticeButtons) {
            button.Remove();
            button.Dispose();
        }
        _noticeButtons.Clear();
    }

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
        ClearNoticeButtons();
        _conflictMessage.Dispose();
        _conflictBanner.Dispose();
        _deleteButton.Dispose();
        _saveButton.Dispose();
        _status.Dispose();
        _notebookBar.Dispose();
        _newButton.Dispose();
        _loading.Remove();
        _loading.Dispose();
        foreach (var button in _navigationButtons) {
            button.Dispose();
        }
        _home.Dispose();
        _homeNavigation.Dispose();
        _contentHeader.Dispose();
        _pageTitle.Dispose();
        _headerPackages.Dispose();
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

    private static DomElement BindAction(string id, string payload)
        => DomElement.Find(id).On("click", payload);

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
                .Attr("type", "button")
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
                .Attr("aria-current", view.Active ? "page" : "false")
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
