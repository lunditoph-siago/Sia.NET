#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using Sia;
using Sia.Reactive;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class BrowserEditorHost : IDisposable
{
    private readonly string _cellId;
    private readonly BrowserElement _container;
    private readonly BrowserEditorView _view;
    private readonly World _world;
    private readonly string _initialSource;
    private readonly IEditorCompletionProvider _completionProvider;

    private ReactiveMount<CellEditorProps> _mount;
    private State<EditorDoc> _docState;
    private State<CursorState> _cursorState;
    private string _previousText;
    private bool _attached;

    private CompletionQueryResult? _completion;
    private int _completionIndex;
    private int _completionGeneration;

    public string CellId => _cellId;
    public IEditorView View => _view;

    public BrowserEditorHost(
        BrowserElement container, string cellId, string initialSource, IMetadataReferenceProvider references)
    {
        _cellId = cellId;
        _container = container;
        _initialSource = initialSource;
        _previousText = initialSource;
        _completionProvider = new LightweightCompletionProvider(references);

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

    public static Task OnEditorChanged(string cellId, string newValue)
    {
        var host = FindHost(cellId);
        if (host == null || !host._attached) return Task.CompletedTask;

        var oldText = host._previousText;
        if (oldText == newValue) return Task.CompletedTask;

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

        var looksLikeTyping = IsIdentifierInsertion(inserted) || (deleted > 0 && inserted.Length == 0);

        if (looksLikeTyping)
        {
            _ = host.RequestCompletionAsync(doc, cursor);
        }
        else
        {
            host.ClosePopupIfOpen();
        }

        return Task.CompletedTask;
    }

    public static async Task OnEditorKeyDown(string cellId, string key, bool ctrl, bool shift, bool alt)
    {
        var host = FindHost(cellId);
        if (host == null || !host._attached) return;

        if (host._completion is { } open)
        {
            var consumed = await host.HandleCompletionKeyAsync(key, open).ConfigureAwait(false);
            if (consumed) return;
        }

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

        host.SyncTextArea(newDoc.Value, newCursor);

        host.ClosePopupIfOpen();
    }

    private async Task<bool> HandleCompletionKeyAsync(string key, CompletionQueryResult open)
    {
        switch (key)
        {
            case "ArrowDown":
                _completionIndex = (_completionIndex + 1) % open.Items.Count;
                RenderCompletionPopup();
                return true;
            case "ArrowUp":
                _completionIndex = (_completionIndex - 1 + open.Items.Count) % open.Items.Count;
                RenderCompletionPopup();
                return true;
            case "Enter":
            case "Tab":
                await AcceptCompletionAsync(open).ConfigureAwait(false);
                return true;
            case "Escape":
                ClosePopupIfOpen();
                return true;
            default:
                return false;
        }
    }

    private async Task RequestCompletionAsync(EditorDoc doc, CursorState cursor)
    {
        var generation = ++_completionGeneration;

        CompletionQueryResult result;
        try
        {
            var offset = ToOffset(doc.Value, cursor.Line, cursor.Column);
            result = await _completionProvider.QueryAsync(doc.Value.FullText, offset)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Completion query failed: {ex}");
            if (generation == _completionGeneration) ClosePopupIfOpen();
            return;
        }

        if (generation != _completionGeneration) return;

        _completionIndex = 0;
        if (result.IsOpen)
        {
            _completion = result;
            RenderCompletionPopup();
        }
        else
        {
            ClosePopupIfOpen();
        }
    }

    private Task AcceptCompletionAsync(CompletionQueryResult completion)
    {
        var item = completion.Items[_completionIndex];
        ClosePopupIfOpen();

        var doc = _docState.Value;
        doc.Mutate(d =>
        {
            var (sl, sc) = OffsetToPosition(d, item.ReplaceStart);
            var (el, ec) = OffsetToPosition(d, item.ReplaceEnd);
            d.DeleteRange(sl, sc, el, ec);
            d.Insert(sl, sc, item.InsertText);
        });

        var (nl, nc) = OffsetToPosition(doc.Value, item.ReplaceStart + item.InsertText.Length);
        var newCursor = _cursorState.Value with { Line = nl, Column = nc, PreferredColumn = nc };

        _docState.Set(doc);
        _cursorState.Set(newCursor);
        _previousText = doc.Value.FullText;
        _world.FlushReactive();

        SyncTextArea(doc.Value, newCursor);
        return Task.CompletedTask;
    }

    private void ClosePopupIfOpen()
    {
        if (_completion is null) return;
        _completion = null;
        HideCompletionPopup();
    }

    private void RenderCompletionPopup()
    {
        if (_completion is not { } q) return;
        HideCompletionPopup();

        var lineEl = BrowserElement.TryFind(_view.CursorLineElementId);
        if (lineEl is null) return;
        using var line = lineEl;

        var popup = BrowserElement.Create("div").Class("completion-popup").Id(CompletionPopupId);
        for (var i = 0; i < q.Items.Count; i++)
        {
            var item = BrowserElement.Create("div").Class("completion-item");
            item.ToggleClass("selected", i == _completionIndex);
            item.Text(q.Items[i].Label);
            popup.Append(item);
        }
        line.Append(popup);
    }

    private void HideCompletionPopup()
    {
        var popup = BrowserElement.TryFind(CompletionPopupId);
        if (popup is null) return;
        popup.Remove();
        popup.Dispose();
    }

    private string CompletionPopupId => "editor-" + _cellId + "-completion-popup";

    private static bool IsIdentifierInsertion(string inserted)
        => inserted.Length > 0 && (inserted == "." || inserted.All(c => char.IsLetterOrDigit(c) || c == '_'));

    private static int ToOffset(EditorDocument doc, int line, int col)
    {
        var offset = 0;
        for (var i = 0; i < line; i++) offset += doc.LineLength(i) + 1;
        return offset + col;
    }

    private void SyncTextArea(EditorDocument doc, CursorState cursor)
    {
        int start, end;
        if (cursor.HasSelection)
        {
            var (sl, sc) = cursor.SelectionStart;
            var (el, ec) = cursor.SelectionEnd;
            start = ToOffset(doc, sl, sc);
            end = ToOffset(doc, el, ec);
        }
        else
        {
            start = end = ToOffset(doc, cursor.Line, cursor.Column);
        }
        BrowserDom.SetEditorText(_container.Handle, doc.FullText, start, end);
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
        foreach (var host in _hosts.Values)
        {
            host.AttachToDom();
        }
    }

    public static void DisposeAll()
    {
        foreach (var host in _hosts.Values.ToArray())
        {
            host.Dispose();
        }
        _hosts.Clear();
    }

    private static readonly Dictionary<string, BrowserEditorHost> _hosts = [];

    private static BrowserEditorHost? FindHost(string cellId)
        => _hosts.GetValueOrDefault(cellId);

    public static BrowserEditorHost Create(
        BrowserElement container, string cellId, string source, IMetadataReferenceProvider references)
    {
        var host = new BrowserEditorHost(container, cellId, source, references);
        _hosts[cellId] = host;
        return host;
    }

    public static BrowserEditorHost GetOrCreate(
        BrowserElement container, string cellId, string source, IMetadataReferenceProvider references)
    {
        if (_hosts.TryGetValue(cellId, out var existing))
        {
            existing.Dispose();
            _hosts.Remove(cellId);
        }
        return Create(container, cellId, source, references);
    }

    public static string? ReadSource(string cellId)
        => FindHost(cellId)?.GetSource();


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
    internal static partial void SetEditorText(JSObject container, string text, int selectionStart, int selectionEnd);
}
#endif
