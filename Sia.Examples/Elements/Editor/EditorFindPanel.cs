using Sia.Examples.Dom;

namespace Sia.Examples.Editor;

internal sealed class EditorFindPanel(DomElement container, string binding) : IDisposable
{
    private readonly List<DomElement> _elements = [];
    private DomElement? _root;
    private DomElement _query = null!;
    private DomElement _replacement = null!;
    private DomElement _replaceRow = null!;
    private DomElement _summary = null!;
    private DomElement _case = null!;
    private DomElement _word = null!;
    private DomElement _regex = null!;
    private bool _matchCase;
    private bool _wholeWord;
    private bool _regularExpression;
    private bool _replace;
    private int _generation;
    private int _revision;
    private string _queryText = "";
    private string _replacementText = "";
    internal bool IsOpen => _root is not null;
    internal Text? Document { get; private set; }
    internal EditorSearchResult Result { get; private set; } = new([]);
    internal int Selected { get; set; } = -1;
    internal bool InputsCurrent => IsOpen && _queryText == _query.Value() && _replacementText == _replacement.Value();

    private DomElement Own(DomElement element) { _elements.Add(element); return element; }

    private DomElement Button(string label, string action, string text)
    {
        var button = Own(DomElement.Create("button").Attr("type", "button").Attr("aria-label", label).Attr("title", label)
            .Attr("data-editor-find-action", action));
        var path = action switch {
            "previous" => "M8 13V3M4 7l4-4 4 4",
            "next" => "M8 3v10M4 9l4 4 4-4",
            "toggle" => "M3 5h10m-3-3 3 3-3 3M13 11H3m3-3-3 3 3 3",
            "close" => "m4 4 8 8m0-8-8 8",
            _ => null,
        };
        return path is null ? button.Text(text) : button.Append(Own(DomElement.Create("svg")
            .Attr("viewBox", "0 0 16 16").Attr("width", "16").Attr("height", "16").Attr("aria-hidden", "true")
            .Attr("fill", "none").Attr("stroke", "currentColor").Attr("stroke-width", "1.4")
            .Attr("stroke-linecap", "round").Attr("stroke-linejoin", "round"))
            .Append(Own(DomElement.Create("path").Attr("d", path))));
    }

    internal void Open(EditorState state, bool replace)
    {
        if (_root is not null) {
            _queryText = _query.Value(); _replacementText = _replacement.Value();
            Close();
        }
        var selected = state.Selection.Main;
        if (!selected.Empty && state.Doc.LineAt(selected.From).Number == state.Doc.LineAt(selected.To).Number) {
            var text = state.SliceDoc(selected.From, selected.To);
            _queryText = _regularExpression ? System.Text.RegularExpressions.Regex.Escape(text) : text;
        }
        if (_root is null) {
            _generation++;
            _root = Own(DomElement.Create("div").Class("editor-find").Attr("role", "search")
                .Attr("aria-label", "Find in document").Attr("data-editor-find", $"{binding}:{_generation}"));
            var row = Own(DomElement.Create("div").Class("editor-find-row"));
            _query = Own(DomElement.Create("input").Attr("type", "text").Attr("aria-label", "Find")
                .Attr("placeholder", "Find").Attr("autocomplete", "off").Attr("spellcheck", "false")
                .Attr("value", _queryText)
                .Attr("data-editor-find-field", "query").On("input", $"find:{binding}:{_generation}:query"));
            _case = Button("Match case", "case", "Aa");
            _word = Button("Match whole word", "word", "ab");
            _regex = Button("Use regular expression", "regex", ".*");
            _summary = Own(DomElement.Create("span").Class("editor-find-summary").Attr("role", "status"));
            row.Append(_query).Append(_case).Append(_word).Append(_regex)
                .Append(Button("Previous match", "previous", "↑")).Append(Button("Next match", "next", "↓"))
                .Append(Button("Show or hide replacement", "toggle", "⇄")).Append(Button("Close find", "close", "×"));
            _replacement = Own(DomElement.Create("input").Attr("type", "text").Attr("aria-label", "Replace")
                .Attr("placeholder", "Replace").Attr("autocomplete", "off").Attr("spellcheck", "false")
                .Attr("value", _replacementText)
                .Attr("data-editor-find-field", "replacement").On("input", $"find:{binding}:{_generation}:replacement"));
            _replaceRow = Own(DomElement.Create("div").Class("editor-find-row"));
            _replaceRow.Append(_replacement).Append(Button("Replace current match", "replace", "Replace"))
                .Append(Button("Replace all matches", "all", "Replace all"));
            _root.Append(row).Append(_replaceRow).Append(_summary);
            container.Append(_root);
        }
        _replace = replace;
        _replaceRow.ToggleClass("hidden", !_replace);
        container.ToggleClass("editor-find-open", true);
        Refresh(state.Doc);
        Focus();
    }

    internal void Focus() { if (IsOpen) _query.Focus(); }

    internal bool IsCurrentEvent(string argument)
    {
        var split = argument.IndexOf(':');
        return IsOpen && split >= 0 && int.TryParse(argument.AsSpan(0, split), out var generation) && generation == _generation;
    }

    internal bool TryAction(string argument, out string action)
    {
        action = "";
        var split = argument.IndexOf(':');
        if (!IsCurrentEvent(argument)) return false;
        var parts = argument[(split + 1)..].Split(':', 2);
        action = parts[0];
        if (action is "replace" or "all" && (parts.Length != 2 || !int.TryParse(parts[1], out var revision) || revision != _revision)) {
            ResultsChanged(); return false;
        }
        switch (action) {
            case "case": _matchCase = !_matchCase; break;
            case "word": _wholeWord = !_wholeWord; break;
            case "regex": _regularExpression = !_regularExpression; break;
            case "toggle": _replace = !_replace; _replaceRow.ToggleClass("hidden", !_replace); break;
        }
        return true;
    }

    internal void Refresh(Text document)
    {
        if (_root is null) return;
        Document = document;
        Selected = -1;
        _queryText = _query.Value(); _replacementText = _replacement.Value();
        Result = EditorSearchResult.Find(document.SliceDoc(), _queryText, _replacementText, _matchCase, _wholeWord, _regularExpression);
        _root.Attr("data-editor-find-revision", (++_revision).ToString());
        _case.Attr("aria-pressed", _matchCase ? "true" : "false");
        _word.Attr("aria-pressed", _wholeWord ? "true" : "false");
        _regex.Attr("aria-pressed", _regularExpression ? "true" : "false");
        _query.Attr("aria-invalid", Result.Error is not null ? "true" : "false");
        ShowSummary();
    }

    internal void ShowSummary()
        => _summary.Text(Result.Error ?? (Result.Matches.Count == 0 ? "No results" : Selected < 0
            ? $"{Result.Matches.Count} matches" : $"{Selected + 1} of {Result.Matches.Count}"));

    internal void ResultsChanged()
    {
        if (Result.Error is not null) ShowSummary();
        else _summary.Text("Results changed. Review the matches before replacing.");
    }

    internal void Close()
    {
        _generation++;
        container.ToggleClass("editor-find-open", false);
        _root?.Remove();
        foreach (var element in _elements) element.Dispose();
        _elements.Clear(); _root = null; Document = null; Result = new([]); Selected = -1;
    }

    public void Dispose() => Close();
}
