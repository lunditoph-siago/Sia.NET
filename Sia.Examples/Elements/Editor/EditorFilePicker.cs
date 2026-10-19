using Sia_Examples.Dom;

namespace Sia_Examples.Editor;

internal sealed record EditorFileChoice(string Id, string Name, string Path)
{
    internal static EditorFileChoice[] Match(IEnumerable<EditorFileChoice> files, string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [.. files.Select(file => (File: file, Ranks: terms.Select(term =>
                Math.Max(CompletionMatch.Rank(file.Name, term), CompletionMatch.Rank(file.Path, term))).ToArray()))
            .Where(static item => item.Ranks.All(static rank => rank >= 0))
            .OrderByDescending(static item => item.Ranks.Sum()).ThenBy(static item => item.File.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.File.Id, StringComparer.Ordinal).Select(static item => item.File)];
    }
}

internal sealed class EditorFilePicker(string eventName, Func<IReadOnlyList<EditorFileChoice>> files,
    Action<EditorFileChoice> open) : IDisposable
{
    private const int PageSize = 50;
    private readonly DomElement _root = DomElement.Find("app");
    private readonly string _instance = Guid.NewGuid().ToString("N");
    private readonly List<DomElement> _elements = [];
    private readonly List<DomElement> _rows = [];
    private readonly List<DomElement> _pageButtons = [];
    private DomElement? _dialog;
    private DomElement _input = null!, _list = null!, _message = null!;
    private DomElement _pages = null!;
    private Action _returnFocus = static () => { };
    private EditorFileChoice[] _matches = [];
    private string _query = "";
    private int _generation, _revision, _selected, _offset;
    private bool _disposed;

    internal void Open(Action returnFocus)
    {
        if (_disposed) return;
        Close();
        _returnFocus = returnFocus;
        _dialog = Own(DomElement.Create("dialog").Class("editor-dialog").Attr("aria-label", "Quick Open")
            .Attr("data-editor-dialog", eventName).Attr("data-editor-dialog-stamp", $"{_instance}:{_generation}")
            .Attr("data-editor-dialog-list", ""));
        var header = Own(DomElement.Create("header").Class("editor-dialog-header"));
        header.Append(Own(DomElement.Create("h2").Text("Quick Open")))
            .Append(Own(DomElement.Create("button").Class("btn").Attr("type", "button").Text("Close").On("click", Payload("close"))));
        _input = Own(DomElement.Create("input").Attr("type", "search").Attr("role", "combobox")
            .Attr("aria-label", "Search workspace files").Attr("placeholder", "Type a file name or path")
            .Attr("autocomplete", "off").Attr("spellcheck", "false").Attr("data-editor-dialog-focus", "")
            .Attr("aria-autocomplete", "list").Attr("aria-expanded", "true").Attr("aria-controls", $"{_instance}-files")
            .On("input", Payload("input")));
        _list = Own(DomElement.Create("div").Class("editor-dialog-list").Id($"{_instance}-files")
            .Attr("role", "listbox").Attr("aria-label", "Workspace files"));
        _message = Own(DomElement.Create("p").Class("editor-dialog-message").Attr("role", "status"));
        _pages = Own(DomElement.Create("footer").Class("editor-dialog-footer"));
        _dialog.Append(header).Append(_input).Append(_list).Append(_message).Append(_pages);
        _root.Append(_dialog);
        Filter();
    }

    internal bool Route(string argument)
    {
        var parts = argument.Split(':', 5);
        if (_disposed || _dialog is null || parts.Length < 3 || parts[0] != _instance
            || !int.TryParse(parts[1], out var generation) || generation != _generation) return false;
        switch (parts[2]) {
            case "close" or "escape": Close(); _returnFocus(); return true;
            case "input": Filter(); return true;
            case "page" when parts.Length == 5 && int.TryParse(parts[3], out var pageRevision) && pageRevision == _revision
                && int.TryParse(parts[4], out var direction) && direction is -1 or 1:
                if (_query != _input.Value()) { Filter(); return true; }
                if (_matches.Length == 0) return true;
                _selected = Math.Clamp(_offset + direction * PageSize, 0, _matches.Length - 1);
                RenderRows(); _input.Focus(); return true;
            case "move" when parts.Length == 5 && int.TryParse(parts[3], out var visibleRevision) && visibleRevision == _revision
                && int.TryParse(parts[4], out var step):
                if (_query != _input.Value()) { Filter(); return true; }
                if (_matches.Length == 0) return true;
                _selected = (int)(((long)_selected + step) % _matches.Length + _matches.Length) % _matches.Length;
                if (_selected < _offset || _selected >= _offset + PageSize) RenderRows(); else SelectRow();
                return true;
            case "accept":
            case "pick":
                if (_query != _input.Value()) { Filter(); return true; }
                var index = _selected;
                if (parts[2] == "accept" && (parts.Length != 4 || !int.TryParse(parts[3], out var acceptedRevision)
                    || acceptedRevision != _revision)) return false;
                if (parts[2] == "pick" && (parts.Length != 5 || !int.TryParse(parts[3], out var revision) || revision != _revision
                    || !int.TryParse(parts[4], out index))) return false;
                if (index < 0 || index >= _matches.Length) return false;
                var choice = _matches[index];
                if (!files().Contains(choice)) { Filter(); _message.Text("The workspace changed. Select a file again."); return true; }
                Close(); open(choice); return true;
            default: return false;
        }
    }

    private void Filter()
    {
        _revision++;
        _query = _input.Value();
        _matches = EditorFileChoice.Match(files(), _query);
        _selected = 0;
        RenderRows();
    }

    private void RenderRows()
    {
        _dialog!.Attr("data-editor-dialog-revision", _revision.ToString());
        _offset = _selected / PageSize * PageSize;
        _pages.ToggleClass("hidden", _matches.Length <= PageSize);
        foreach (var button in _pageButtons) { button.Remove(); button.Dispose(); }
        _pageButtons.Clear();
        if (_matches.Length > PageSize) {
            foreach (var (label, direction, disabled) in new[] { ("Previous page", -1, _offset == 0), ("Next page", 1, _offset + PageSize >= _matches.Length) }) {
                var button = DomElement.Create("button").Class("btn").Attr("type", "button").Text(label)
                    .Attr("aria-disabled", disabled ? "true" : "false").On("click", Payload($"page:{_revision}:{direction}"));
                _pageButtons.Add(button); _pages.Append(button);
            }
        }
        DomRuntime.BeginUpdate(_list);
        try {
            foreach (var row in _rows) { row.Remove(); row.Dispose(); }
            _rows.Clear();
            for (var index = _offset; index < Math.Min(_offset + PageSize, _matches.Length); index++) {
                var file = _matches[index];
                var row = DomElement.Create("button").Class("editor-dialog-row").Attr("type", "button").Attr("role", "option")
                    .Attr("tabindex", "-1")
                    .Id($"{_instance}-file-{index}").Attr("aria-posinset", (index + 1).ToString()).Attr("aria-setsize", _matches.Length.ToString())
                    .Attr("title", file.Path).On("click", Payload($"pick:{_revision}:{index}"));
                using var label = DomElement.Create("span").Class("keymap-command-title")
                    .Text(file.Name == file.Path ? file.Name : $"{file.Name} — {file.Path}");
                _rows.Add(row); _list.Append(row.Append(label));
            }
            SelectRow();
        }
        finally { DomRuntime.EndUpdate(_list); }
    }

    private void SelectRow()
    {
        for (var index = 0; index < _rows.Count; index++) _rows[index].Attr("aria-selected", _offset + index == _selected ? "true" : "false");
        _input.Attr("aria-activedescendant", _matches.Length == 0 ? "" : $"{_instance}-file-{_selected}");
        _message.Text(_matches.Length == 0 ? "No matching files." : $"{_selected + 1} / {_matches.Length} · Enter to open · Escape to cancel");
        if (_rows.Count > 0) DomRuntime.EnsureVisible(_list, _rows[_selected - _offset]);
    }

    private string Payload(string action) => $"{eventName}:{_instance}:{_generation}:{action}";
    private DomElement Own(DomElement element) { _elements.Add(element); return element; }
    internal void Close()
    {
        _generation++;
        _dialog?.Remove();
        foreach (var row in _rows) row.Dispose();
        _rows.Clear();
        foreach (var button in _pageButtons) button.Dispose();
        _pageButtons.Clear();
        foreach (var element in _elements) element.Dispose();
        _elements.Clear(); _dialog = null; _matches = []; _query = "";
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Close(); _root.Dispose(); }
}
