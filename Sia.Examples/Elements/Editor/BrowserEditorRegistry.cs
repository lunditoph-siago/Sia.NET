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
    private readonly Dictionary<string, BrowserEditorHost> _editors = [];

    public BrowserEditorHost Add(
        DomElement container,
        string cellId,
        string source,
        IReadOnlyList<HighlightRun> highlights)
    {
        var editor = new BrowserEditorHost(
            _world,
            container,
            cellId,
            source,
            highlights,
            _references);
        _editors.Add(cellId, editor);
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
        var cellId = payload[(typeSeparator + 1)..cellSeparator];
        return _editors.TryGetValue(cellId, out var editor)
            && editor.Route(eventType, payload[(cellSeparator + 1)..]);
    }

    public string GetSource(string cellId)
        => _editors.TryGetValue(cellId, out var editor)
            ? editor.Source
            : throw new KeyNotFoundException($"Editor '{cellId}' is not mounted.");

    public bool TryGetSource(string cellId, out string source)
    {
        if (_editors.TryGetValue(cellId, out var editor)) {
            source = editor.Source;
            return true;
        }
        source = string.Empty;
        return false;
    }

    public void Remove(string cellId)
    {
        if (_editors.Remove(cellId, out var editor)) {
            editor.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var editor in _editors.Values) {
            editor.Dispose();
        }
        _editors.Clear();
    }
}
