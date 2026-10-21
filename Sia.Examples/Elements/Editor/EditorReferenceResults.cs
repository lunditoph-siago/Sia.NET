using Sia.Examples.Dom;

namespace Sia.Examples.Editor;

internal sealed class EditorReferenceResults : IDisposable
{
    private const int k_PageSize = 50;
    private readonly string _event;
    private readonly string _instance = Guid.NewGuid().ToString("N");
    private readonly Action<SourcePosition> _navigate;
    private readonly Action _returnFocus;
    private readonly List<DomElement> _rows = [];
    private DomElement _close = null!;
    private DomElement _body = null!;
    private SymbolReferences? _references;
    private SourcePosition? _preview;
    private int _offset;
    private int _generation;
    private bool _disposed;

    internal EditorReferenceResults(string eventName, Action<SourcePosition> navigate, Action returnFocus)
    {
        _event = eventName; _navigate = navigate; _returnFocus = returnFocus;
        Element = DomElement.Create("section").Class("editor-reference-results").Class("hidden").Attr("aria-label", "C# symbol references");
    }

    internal DomElement Element { get; }

    internal void Show(SymbolReferences? references)
    {
        _references = references; _preview = null; _offset = 0;
        Render();
    }

    internal void Focus() => _close.Focus();

    internal void ShowSource(SourcePosition source)
    {
        _preview = source;
        Render();
    }

    internal bool Route(string argument)
    {
        var parts = argument.Split(':');
        if (_disposed || parts.Length != 3 || parts[0] != _instance || !int.TryParse(parts[1], out var generation) || generation != _generation
            || !int.TryParse(parts[2], out var index)) return false;
        if (index == -1) { Clear(); _returnFocus(); return true; }
        if (index == -4) { _preview = null; Render(); return true; }
        if (_references is not { } references) return false;
        if (index is -2 or -3) {
            if (index == -2 && _offset == 0 || index == -3 && _offset + k_PageSize >= references.Occurrences.Count) return true;
            _offset = System.Math.Clamp(_offset + (index == -2 ? -k_PageSize : k_PageSize), 0, System.Math.Max(0, (references.Occurrences.Count - 1) / k_PageSize * k_PageSize));
            Render(); return true;
        }
        if (index < 0 || index >= references.Occurrences.Count) return false;
        var source = references.Occurrences[index].Location;
        if (source.Generated) ShowSource(source);
        else _navigate(source);
        return true;
    }

    internal void Clear()
    {
        _generation++; _references = null; _preview = null;
        if (_rows.Count == 0) return;
        DomRuntime.BeginUpdate(Element);
        try {
            ClearRows();
            Element.ToggleClass("hidden", true);
        }
        finally { DomRuntime.EndUpdate(Element); }
    }

    private void Render()
    {
        DomRuntime.BeginUpdate(Element);
        try { RenderContents(); }
        finally { DomRuntime.EndUpdate(Element); }
    }

    private void RenderContents()
    {
        _generation++;
        ClearRows();
        Element.ToggleClass("hidden", false);
        var header = Row(DomElement.Create("header"));
        var title = Row(DomElement.Create("strong"));
        var summary = Row(DomElement.Create("span").Class("editor-reference-summary"));
        _close = Button("Close references", "×").On("click", Payload(-1));
        header.Append(title).Append(summary);
        _body = Row(DomElement.Create("div").Class("editor-reference-body"));
        var total = _references?.Occurrences.Count ?? 0;
        var preview = _preview;
        if (preview is null && total > k_PageSize) {
            header.Append(Button("Previous page", "‹").Attr("aria-disabled", _offset == 0 ? "true" : "false").On("click", Payload(-2)))
                .Append(Button("Next page", "›").Attr("aria-disabled", _offset + k_PageSize >= total ? "true" : "false").On("click", Payload(-3)));
        }
        if (preview is not null && _references is not null) header.Append(Button("Back to references", "Back").On("click", Payload(-4)));
        Element.Append(header.Append(_close)).Append(_body);
        if (preview is not null) {
            title.Text($"{preview.Path}:{preview.Line}:{preview.Column}"); summary.Text("read only");
            var source = Row(DomElement.Create("pre").Attr("tabindex", "0").Attr("aria-label", "Source preview, read only"));
            var before = Row(DomElement.Create("span").Text(preview.Source[..preview.Start]));
            var selected = Row(DomElement.Create("mark").Text(preview.Source.Substring(preview.Start, preview.Length)));
            var after = Row(DomElement.Create("span").Text(preview.Source[(preview.Start + preview.Length)..]));
            _body.Append(source.Append(before).Append(selected).Append(after));
            source.Focus();
            DomRuntime.EnsureVisible(_body, selected);
            return;
        }
        title.Text(_references is null ? "No symbol at the cursor" : _references.Display);
        var definitions = _references?.Occurrences.Count(static occurrence => occurrence.Definition) ?? 0;
        summary.Text($"{total - definitions} references · {definitions} declarations"
            + (total > k_PageSize ? $" · {_offset + 1}–{System.Math.Min(_offset + k_PageSize, total)} / {total}" : ""));
        if (_references is null) return;
        for (var index = _offset; index < System.Math.Min(_offset + k_PageSize, total); index++) {
            var occurrence = _references.Occurrences[index];
            var location = occurrence.Location;
            var kind = occurrence.Definition ? "declaration" : occurrence.Implicit ? "implicit" : occurrence.Candidate ? "possible" : "reference";
            var button = Row(DomElement.Create("button").Attr("type", "button").Attr("title", $"{occurrence.Symbol} · {kind}")
                .On("click", Payload(index)));
            button.Append(Row(DomElement.Create("span").Class("editor-reference-location").Text($"{location.Path}:{location.Line}:{location.Column} · {kind}")));
            button.Append(Row(DomElement.Create("code").Text(LineText(location))));
            _body.Append(button);
        }
    }

    private string Payload(int index) => $"{_event}:{_instance}:{_generation}:{index}";
    private DomElement Button(string label, string text)
        => Row(DomElement.Create("button").Attr("type", "button").Attr("aria-label", label).Attr("title", label).Text(text));
    private DomElement Row(DomElement element) { _rows.Add(element); return element; }
    private void ClearRows()
    {
        Element.Text("");
        foreach (var row in _rows) row.Dispose();
        _rows.Clear();
    }
    private static string LineText(SourcePosition location)
    {
        var start = location.Start == 0 ? 0 : location.Source.LastIndexOf('\n', location.Start - 1) + 1;
        var end = location.Source.IndexOf('\n', location.Start);
        var text = location.Source[start..(end < 0 ? location.Source.Length : end)].Trim();
        return text.Length <= 200 ? text : text[..200] + "…";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear(); Element.Remove();
        Element.Dispose();
    }
}
