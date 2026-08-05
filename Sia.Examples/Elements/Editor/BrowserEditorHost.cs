#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using Sia;
using Sia.Reactive;

namespace Sia_Examples.Editor;

public sealed class BrowserEditorHost : IDisposable
{
    private readonly string _cellId;
    private readonly BrowserElement _container;
    private readonly BrowserEditorView _view;
    private readonly World _world;
    private readonly string _initialSource;

    private ReactiveMount<CellEditorProps> _mount;
    private State<EditorDoc> _docState;
    private State<CursorState> _cursorState;
    private string _previousText;
    private bool _attached;

    public string CellId => _cellId;
    public IEditorView View => _view;

    public BrowserEditorHost(BrowserElement container, string cellId, string initialSource)
    {
        _cellId = cellId;
        _container = container;
        _initialSource = initialSource;
        _previousText = initialSource;

        _view = new BrowserEditorView("editor-" + cellId);
        _view.SetVisibleLines(Math.Max(initialSource.Split('\n').Length, 10));

        BrowserDom.AttachEditor(container.Handle, cellId, initialSource);

        _world = new World();
    }

    public void AttachToDom()
    {
        if (_attached) return;
        _attached = true;

        _view.FindElements();

        var prev = Context<World>.Current;
        Context<World>.Current = _world;
        try
        {
            var props = new CellEditorProps(_cellId, _initialSource, _view);
            _mount = _world.Mount(CellEditor.Definition, props);
            _docState = _mount.GetState<EditorDoc>(0);
            _cursorState = _mount.GetState<CursorState>(1);
        }
        finally
        {
            Context<World>.Current = prev;
        }
    }

    public static void OnEditorChanged(string cellId, string newValue)
    {
        var host = FindHost(cellId);
        if (host == null || !host._attached) return;

        var oldText = host._previousText;
        if (oldText == newValue) return;

        var (pos, deleted, inserted) = ComputeDiff(oldText, newValue);

        var doc = host._docState.Value;
        var cursor = host._cursorState.Value;

        doc.Mutate(d =>
        {
            if (deleted > 0)
            {
                var (dl, dc) = OffsetToPosition(d, pos);
                var (el, ec) = OffsetToPosition(d, pos + deleted);
                d.DeleteRange(dl, dc, el, ec);
            }
            if (inserted.Length > 0)
            {
                var (il, ic) = OffsetToPosition(d, pos);
                d.Insert(il, ic, inserted);
            }
        });

        if (deleted > 0 || inserted.Length > 0)
        {
            var (cl, cc) = OffsetToPosition(doc.Value, pos + inserted.Length);
            cursor = cursor with { Line = cl, Column = cc, PreferredColumn = cc };
        }

        host._docState.Set(doc);
        host._cursorState.Set(cursor);
        host._previousText = newValue;
        host._world.FlushReactive();
    }

    public static void OnEditorKeyDown(string cellId, string key, bool ctrl, bool shift, bool alt)
    {
        var host = FindHost(cellId);
        if (host == null || !host._attached) return;

        var cursor = host._cursorState.Value;

        ICommand? cmd = (key, ctrl, shift) switch
        {
            ("Escape", false, _) => new SetModeCommand(
                cursor.Mode == EditorMode.Insert ? EditorMode.Normal : EditorMode.Insert),
            ("Tab", false, false) => new InsertTabCommand(4),
            ("ArrowLeft", false, _) => shift
                ? new ToggleSelectionCommand(cursor.AnchorLine, cursor.AnchorColumn)
                : new MoveLeftCommand(),
            ("ArrowRight", false, _) => shift
                ? new ToggleSelectionCommand(cursor.AnchorLine, cursor.AnchorColumn)
                : new MoveRightCommand(),
            ("ArrowUp", false, _) => new MoveUpCommand(),
            ("ArrowDown", false, _) => new MoveDownCommand(),
            ("Home", false, _) => new MoveLineStartCommand(),
            ("End", false, _) => new MoveLineEndCommand(),
            ("s", true, false) => new SaveCommand(),
            _ => null,
        };

        if (cmd == null) return;

        var (newDoc, newCursor) = CommandBridge.Apply(cmd, host._docState.Value, cursor);
        host._docState.Set(newDoc);
        host._cursorState.Set(newCursor);
        host._previousText = newDoc.Value.FullText;
        host._world.FlushReactive();

        BrowserDom.SetEditorText(host._container.Handle, newDoc.Value.FullText);
    }

    public string GetSource()
    {
        if (!_attached) return _initialSource;
        return _docState.Value.Value.FullText;
    }

    public void Dispose()
    {
        BrowserDom.DetachEditor(_container.Handle, _cellId);
        if (_attached)
        {
            _mount.Unmount();
        }
        _world.Dispose();
    }

    public static void AttachAll()
    {
        foreach (var (_, wr) in _hosts)
        {
            if (wr.TryGetTarget(out var host))
                host.AttachToDom();
        }
    }

    public static void DisposeAll()
    {
        foreach (var (_, wr) in _hosts.ToArray())
        {
            if (wr.TryGetTarget(out var host))
                host.Dispose();
        }
        _hosts.Clear();
    }


    private static readonly Dictionary<string, WeakReference<BrowserEditorHost>> _hosts = [];

    private static BrowserEditorHost? FindHost(string cellId)
    {
        if (_hosts.TryGetValue(cellId, out var wr) && wr.TryGetTarget(out var host))
            return host;
        return null;
    }

    public static BrowserEditorHost Create(BrowserElement container, string cellId, string source)
    {
        var host = new BrowserEditorHost(container, cellId, source);
        _hosts[cellId] = new WeakReference<BrowserEditorHost>(host);
        return host;
    }

    public static BrowserEditorHost GetOrCreate(BrowserElement container, string cellId, string source)
    {
        if (_hosts.TryGetValue(cellId, out var wr) && wr.TryGetTarget(out var existing))
        {
            existing.Dispose();
            _hosts.Remove(cellId);
        }
        return Create(container, cellId, source);
    }

    public static string? ReadSource(string cellId)
    {
        if (_hosts.TryGetValue(cellId, out var wr) && wr.TryGetTarget(out var host))
            return host.GetSource();
        return null;
    }


    private static (int Offset, int Deleted, string Inserted) ComputeDiff(
        string oldText, string newText)
    {
        var prefixLen = 0;
        var minLen = Math.Min(oldText.Length, newText.Length);
        while (prefixLen < minLen && oldText[prefixLen] == newText[prefixLen])
            prefixLen++;

        var oldRemaining = oldText.Length - prefixLen;
        var newRemaining = newText.Length - prefixLen;
        var suffixLen = 0;
        var maxSuffix = Math.Min(oldRemaining, newRemaining);
        while (suffixLen < maxSuffix &&
               oldText[oldText.Length - 1 - suffixLen] == newText[newText.Length - 1 - suffixLen])
            suffixLen++;

        var deleted = oldRemaining - suffixLen;
        var inserted = newText.Substring(prefixLen, newRemaining - suffixLen);

        return (prefixLen, deleted, inserted);
    }

    private static (int Line, int Col) OffsetToPosition(EditorDocument doc, int offset)
    {
        var remaining = offset;
        for (var i = 0; i < doc.LineCount; i++)
        {
            var len = doc.LineLength(i);
            if (remaining <= len) return (i, remaining);
            remaining -= len + 1;
        }
        var lastLine = doc.LineCount - 1;
        return (lastLine, doc.LineLength(lastLine));
    }
}


internal static partial class BrowserDom
{
    [JSImport("attachEditor", "main.js")]
    internal static partial void AttachEditor(JSObject container, string cellId, string initialValue);

    [JSImport("detachEditor", "main.js")]
    internal static partial void DetachEditor(JSObject container, string cellId);

    [JSImport("setEditorText", "main.js")]
    internal static partial void SetEditorText(JSObject container, string text);
}
#endif
