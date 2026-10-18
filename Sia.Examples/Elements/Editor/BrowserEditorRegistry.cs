using Sia;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class BrowserEditorRegistry(
    World world,
    ICompilationReferenceResolver references) : IDisposable
{
    private readonly World _world = world;
    private readonly ICompilationReferenceResolver _references = references;
    private readonly Dictionary<string, (DomElement Container, BrowserEditorHost Editor)> _editors = [];

    public BrowserEditorHost Add(
        DomElement container,
        string cellId,
        string source,
        IReadOnlyList<HighlightRun> highlights)
    {
        if (_editors.ContainsKey(cellId)) {
            throw new InvalidOperationException($"Editor '{cellId}' is already mounted.");
        }
        var editor = new BrowserEditorHost(
            _world,
            container,
            cellId,
            source,
            highlights,
            _references);
        _editors.Add(cellId, (container, editor));
        return editor;
    }

    public bool Route(string payload)
    {
        var typeSeparator = payload.IndexOf(':');
        if (typeSeparator < 0) {
            return false;
        }
        var cellSeparator = payload.IndexOf(':', typeSeparator + 1);
        if (cellSeparator < 0) {
            return false;
        }

        var eventType = payload[..typeSeparator];
        var bindingId = payload[(typeSeparator + 1)..cellSeparator];
        return TryResolveCellId(bindingId, out var cellId)
            && _editors[cellId].Editor.Route(eventType, payload[(cellSeparator + 1)..]);
    }

    public bool TryResolveCellId(string bindingId, out string cellId)
    {
        var separator = bindingId.LastIndexOf('~');
        cellId = separator < 0 ? string.Empty : bindingId[..separator];
        return _editors.TryGetValue(cellId, out var entry)
            && entry.Editor.BindingId == bindingId;
    }

    public string GetSource(string cellId)
        => _editors.TryGetValue(cellId, out var entry)
            ? entry.Editor.Source
            : throw new KeyNotFoundException($"Editor '{cellId}' is not mounted.");

    public void SynchronizeNativeInput(string cellId)
    {
        if (_editors.TryGetValue(cellId, out var entry)) {
            entry.Editor.SynchronizeNativeInput();
        }
    }

    public BrowserEditorHost? Update(string cellId, string source, IReadOnlyList<HighlightRun> highlights)
    {
        if (!_editors.TryGetValue(cellId, out var entry)) {
            return null;
        }
        if (entry.Editor.Source == source) {
            entry.Editor.UpdateHighlights(highlights);
            return entry.Editor;
        }
        var hasPosition = entry.Editor.TryGetPosition(out var position);
        var replacement = Replace(cellId, source, highlights);
        if (hasPosition) {
            replacement.RestorePosition(position);
        }
        return replacement;
    }

    public BrowserEditorHost Replace(string cellId, string source, IReadOnlyList<HighlightRun> highlights)
    {
        var container = _editors[cellId].Container;
        Remove(cellId);
        return Add(container, cellId, source, highlights);
    }

    public bool TryGetSource(string cellId, out string source)
    {
        if (_editors.TryGetValue(cellId, out var entry)) {
            source = entry.Editor.Source;
            return true;
        }
        source = string.Empty;
        return false;
    }

    public bool TryGetPosition(string cellId, out EditorMemento memento)
    {
        if (_editors.TryGetValue(cellId, out var entry)) {
            return entry.Editor.TryGetPosition(out memento);
        }
        memento = default!;
        return false;
    }

    internal bool TryGetCursorPosition(string cellId, out (int Line, int Column, int Selected) position)
    {
        if (_editors.TryGetValue(cellId, out var entry)) {
            position = entry.Editor.CursorPosition;
            return true;
        }
        position = default;
        return false;
    }

    public void RestorePosition(string cellId, EditorMemento memento)
    {
        if (_editors.TryGetValue(cellId, out var entry)) {
            entry.Editor.RestorePosition(memento);
        }
    }

    public void Remove(string cellId)
    {
        if (_editors.Remove(cellId, out var entry)) {
            entry.Editor.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var entry in _editors.Values) {
            entry.Editor.Dispose();
        }
        _editors.Clear();
    }
}
