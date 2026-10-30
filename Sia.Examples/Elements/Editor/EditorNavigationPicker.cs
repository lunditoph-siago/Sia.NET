using Sia.Examples.Dom;

namespace Sia.Examples.Editor;

internal sealed record EditorNavigationChoice(string Id, string Name, string Path, SourcePosition? Target = null)
{
    internal static EditorNavigationChoice FromSymbol(DocumentSymbol symbol)
        => new($"{symbol.Location.DocumentId}:{symbol.Location.Start}", symbol.Name,
            $"{symbol.Container}{(symbol.Container.Length == 0 ? "" : " · ")}{symbol.Kind} · {symbol.Location.Path}:{symbol.Location.Line}", symbol.Location);

    internal static EditorNavigationChoice[] Match(IEnumerable<EditorNavigationChoice> files, string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return [..files];
        return [..files.Select(file => (File: file, Ranks: terms.Select(term =>
                System.Math.Max(CompletionMatch.Rank(file.Name, term), CompletionMatch.Rank(file.Path, term))).ToArray()))
            .Where(static item => item.Ranks.All(static rank => rank >= 0))
            .OrderByDescending(static item => item.Ranks.Sum()).ThenBy(static item => item.File.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.File.Id, StringComparer.Ordinal).Select(static item => item.File)];
    }
}

internal sealed class EditorNavigationPicker(string eventName, Func<IReadOnlyList<EditorNavigationChoice>> files,
    Action<EditorNavigationChoice> open) : IDisposable
{
    private const int k_PageSize = 50;
    private readonly DomElement _root = DomElement.Find("app");
    private readonly string _instance = Guid.NewGuid().ToString("N");
    private readonly List<DomElement> _elements = [];
    private readonly List<DomElement> _rows = [];
    private readonly List<DomElement> _pageButtons = [];
    private DomElement? _dialog;
    private DomElement _input = null!, _list = null!, _message = null!;
    private DomElement _pages = null!;
    private Action _returnFocus = static () => {};
    private EditorNavigationChoice[] _matches = [];
    private string _query = "";
    private string _emptyMessage = "No matching files.";
    private string? _failure;
    private CancellationTokenSource? _loadCancellation;
    private EditorNavigationChoice[]? _symbols;
    private Func<EditorNavigationChoice, bool>? _validateSymbol;
    private Action<EditorNavigationChoice>? _navigateSymbol;
    private int _generation, _revision, _selected, _offset;
    private bool _disposed;

    internal void Open(Action returnFocus)
        => Open("Quick Open", "Search workspace files", "Type a file name or path", "Workspace files", returnFocus);

    internal async Task OpenSymbolsAsync(bool workspace, Func<CancellationToken, Task<DocumentSymbol[]>> load,
        Func<SourcePosition, bool> validate, Action<SourcePosition> navigate, Action returnFocus)
    {
        if (_disposed) return;
        Open(workspace ? "Workspace Symbols" : "Document Symbols", "Search C# symbols", "Type a symbol name or container", "C# symbols", returnFocus);
        var generation = _generation;
        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        _loadCancellation = cancellation;
        _symbols = [];
        _emptyMessage = "No matching symbols.";
        _validateSymbol = choice => choice.Target is { } target && validate(target);
        _navigateSymbol = choice => navigate(choice.Target!);
        _dialog!.Attr("aria-busy", "true");
        Filter();
        try {
            var symbols = await load(token);
            if (_disposed || token.IsCancellationRequested || generation != _generation || _dialog is null) return;
            _symbols = [..symbols.Select(EditorNavigationChoice.FromSymbol)];
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || generation != _generation || _disposed) { return; }
        catch (Exception error) {
            if (_disposed || generation != _generation || _dialog is null) return;
            _failure = $"Symbol search failed: {error.Message}";
        }
        finally {
            if (ReferenceEquals(_loadCancellation, cancellation)) _loadCancellation = null;
            cancellation.Dispose();
        }
        _dialog!.Attr("aria-busy", "false");
        Filter();
    }

    private void Open(string title, string searchLabel, string placeholder, string listLabel, Action returnFocus)
    {
        if (_disposed) return;
        Close();
        _returnFocus = returnFocus;
        _dialog = Own(DomElement.Create("dialog").Class("editor-dialog").Attr("aria-label", title)
            .Attr("data-editor-dialog", eventName).Attr("data-editor-dialog-stamp", $"{_instance}:{_generation}")
            .Attr("data-editor-dialog-list", ""));
        var header = Own(DomElement.Create("header").Class("editor-dialog-header"));
        header.Append(Own(DomElement.Create("h2").Text(title)))
            .Append(Own(DomElement.Create("button").Class("btn").Attr("type", "button").Text("Close").On("click", Payload("close"))));
        _input = Own(DomElement.Create("input").Attr("type", "search").Attr("role", "combobox")
            .Attr("aria-label", searchLabel).Attr("placeholder", placeholder)
            .Attr("autocomplete", "off").Attr("spellcheck", "false").Attr("data-editor-dialog-focus", "")
            .Attr("aria-autocomplete", "list").Attr("aria-expanded", "true").Attr("aria-controls", $"{_instance}-files")
            .On("input", Payload("input")));
        _list = Own(DomElement.Create("div").Class("editor-dialog-list").Id($"{_instance}-files")
            .Attr("role", "listbox").Attr("aria-label", listLabel));
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
                _selected = System.Math.Clamp(_offset + direction * k_PageSize, 0, _matches.Length - 1);
                RenderRows(); _input.Focus(); return true;
            case "move" when parts.Length == 5 && int.TryParse(parts[3], out var visibleRevision) && visibleRevision == _revision
                && int.TryParse(parts[4], out var step):
                if (_query != _input.Value()) { Filter(); return true; }
                if (_matches.Length == 0) return true;
                _selected = (int)(((long)_selected + step) % _matches.Length + _matches.Length) % _matches.Length;
                if (_selected < _offset || _selected >= _offset + k_PageSize) RenderRows(); else SelectRow();
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
                if (_symbols is not null) {
                    if (_validateSymbol?.Invoke(choice) != true) {
                        _symbols = []; Filter(); _message.Text("The source changed. Reopen symbol search."); return true;
                    }
                    var navigate = _navigateSymbol;
                    Close(); navigate?.Invoke(choice); return true;
                }
                if (!files().Contains(choice)) { Filter(); _message.Text("The workspace changed. Select a file again."); return true; }
                Close(); open(choice); return true;
            default: return false;
        }
    }

    private void Filter()
    {
        _revision++;
        _query = _input.Value();
        _matches = EditorNavigationChoice.Match(_symbols ?? files(), _query);
        _selected = 0;
        RenderRows();
    }

    private void RenderRows()
    {
        _dialog!.Attr("data-editor-dialog-revision", _revision.ToString());
        _offset = _selected / k_PageSize * k_PageSize;
        _pages.ToggleClass("hidden", _matches.Length <= k_PageSize);
        foreach (var button in _pageButtons) { button.Remove(); button.Dispose(); }
        _pageButtons.Clear();
        if (_matches.Length > k_PageSize) {
            foreach (var (label, direction, disabled) in new[] { ("Previous page", -1, _offset == 0), ("Next page", 1, _offset + k_PageSize >= _matches.Length) }) {
                var button = DomElement.Create("button").Class("btn").Attr("type", "button").Text(label)
                    .Attr("aria-disabled", disabled ? "true" : "false").On("click", Payload($"page:{_revision}:{direction}"));
                _pageButtons.Add(button); _pages.Append(button);
            }
        }
        DomRuntime.BeginUpdate(_list);
        try {
            foreach (var row in _rows) { row.Remove(); row.Dispose(); }
            _rows.Clear();
            for (var index = _offset; index < System.Math.Min(_offset + k_PageSize, _matches.Length); index++) {
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
        _message.Text(_failure ?? (_loadCancellation is not null ? "Loading symbols…" : _matches.Length == 0
            ? _emptyMessage : $"{_selected + 1} / {_matches.Length} · Enter to open · Escape to cancel"));
        if (_rows.Count > 0) DomRuntime.EnsureVisible(_list, _rows[_selected - _offset]);
    }

    private string Payload(string action) => $"{eventName}:{_instance}:{_generation}:{action}";
    private DomElement Own(DomElement element) { _elements.Add(element); return element; }
    internal void Close()
    {
        _generation++;
        _loadCancellation?.Cancel();
        _loadCancellation = null;
        _dialog?.Remove();
        foreach (var row in _rows) row.Dispose();
        _rows.Clear();
        foreach (var button in _pageButtons) button.Dispose();
        _pageButtons.Clear();
        foreach (var element in _elements) element.Dispose();
        _elements.Clear(); _dialog = null; _matches = []; _query = "";
        _symbols = null; _validateSymbol = null; _navigateSymbol = null; _failure = null; _emptyMessage = "No matching files.";
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Close(); _root.Dispose(); }
}
