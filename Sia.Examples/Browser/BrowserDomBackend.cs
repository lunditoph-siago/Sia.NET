#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Sia.Examples.Dom;

namespace Sia.Examples.Browser;

internal sealed partial class BrowserDomBackend(BrowserMainThread mainThread) : IDomBackend, Sia.Examples.Editor.IEditorLinesOwnerFactory
{
    private readonly BrowserMainThread _mainThread = mainThread;
    private readonly List<BrowserDomNode> _nodes = [];
    private readonly Dictionary<BrowserDomNode, int> _indexes = [];
    private readonly List<int> _writes = [];
    private readonly List<string> _values = [];
    private readonly Dictionary<string, int> _valueIndexes = new(StringComparer.Ordinal);
    private readonly List<BrowserDomNode> _releases = [];
    private readonly List<BrowserDomNode> _updates = [];
    private readonly Dictionary<JSObject, int> _handles = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public bool SupportsNativeInput => true;

    public IDomNode Find(string id)
    {
        VerifyAccess();
        FlushPending();
        return new BrowserDomNode(this, BrowserInterop.Find(id));
    }

    public IDomNode? TryFind(string id)
    {
        VerifyAccess();
        FlushPending();
        return BrowserInterop.TryFind(id) is { } handle ? new BrowserDomNode(this, handle) : null;
    }

    public IDomNode Create(string tagName)
    {
        VerifyAccess();
        if (_updates.Count == 0) return new BrowserDomNode(this, BrowserInterop.Create(tagName));
        var node = new BrowserDomNode(this, null, tagName);
        Index(node);
        return node;
    }

    public IDomNode CreateText(string value)
    {
        VerifyAccess();
        if (_updates.Count == 0) return new BrowserDomNode(this, BrowserInterop.CreateText(value));
        var node = new BrowserDomNode(this, null, "#text");
        Write(DomWriteKind.Text, Index(node), StringIndex(value));
        return node;
    }

    public void BeginUpdate(IDomNode node)
    {
        VerifyAccess();
        var scope = Owned(node);
        if (scope.Handle is null) _ = Handle(node);
        Write(DomWriteKind.Attribute, Index(scope), StringIndex("data-editor-updating"), StringIndex("true"));
        _updates.Add(scope);
    }

    public void EndUpdate(IDomNode node)
    {
        VerifyAccess();
        var scope = Owned(node, allowDisposed: true);
        var updateIndex = _updates.LastIndexOf(scope);
        if (updateIndex < 0) {
            throw new InvalidOperationException("The DOM node has no active update.");
        }
        var completesScope = _updates.IndexOf(scope) == updateIndex;
        var completed = false;
        try {
            FlushPending(completesScope ? scope : null);
            completed = completesScope;
        }
        finally {
            try {
                _updates.RemoveAt(updateIndex);
                if (completesScope && !completed) {
                    BrowserInterop.SetAttribute(scope.Handle!, "data-editor-updating", "false");
                }
            }
            finally {
                ReleasePending();
            }
        }
    }

    public void SetText(IDomNode node, string value)
    {
        VerifyAccess();
        if (_updates.Count > 0) Write(DomWriteKind.Text, Index(node), StringIndex(value));
        else BrowserInterop.SetText(Handle(node), value);
    }

    public void SetTextRuns(IDomNode node, IReadOnlyList<DomTextRun> runs)
    {
        VerifyAccess();
        if (_updates.Count > 0) {
            Write(DomWriteKind.Runs, Index(node), runs.Count);
            foreach (var run in runs) {
                _writes.Add(StringIndex(run.Text));
                _writes.Add(StringIndex(run.ClassName ?? string.Empty));
            }
            return;
        }
        var text = new string[runs.Count];
        var classes = new string[runs.Count];
        for (var index = 0; index < runs.Count; index++) {
            text[index] = runs[index].Text;
            classes[index] = runs[index].ClassName ?? string.Empty;
        }
        BrowserInterop.SetTextRuns(Handle(node), text, classes);
    }

    public string GetText(IDomNode node)
    {
        VerifyAccess();
        return BrowserInterop.GetText(Handle(node));
    }

    public string GetValue(IDomNode node)
    {
        VerifyAccess();
        return BrowserInterop.GetValue(Handle(node));
    }

    public void Focus(IDomNode node)
    {
        VerifyAccess();
        BrowserInterop.Focus(Handle(node));
    }

    public string? ReadKeymap() { VerifyAccess(); return BrowserInterop.ReadKeymap(); }

    public void WriteKeymap(string json) { VerifyAccess(); BrowserInterop.WriteKeymap(json); }

    public void SetId(IDomNode node, string id)
    {
        VerifyAccess();
        if (_updates.Count > 0) Write(DomWriteKind.Id, Index(node), StringIndex(id));
        else BrowserInterop.SetId(Handle(node), id);
    }

    public void SetAttribute(IDomNode node, string name, string value)
    {
        VerifyAccess();
        if (_updates.Count > 0) Write(DomWriteKind.Attribute, Index(node), StringIndex(name), StringIndex(value));
        else BrowserInterop.SetAttribute(Handle(node), name, value);
    }

    public void ToggleClass(IDomNode node, string name, bool enabled)
    {
        VerifyAccess();
        if (_updates.Count > 0) Write(DomWriteKind.Class, Index(node), StringIndex(name), enabled ? 1 : 0);
        else BrowserInterop.ToggleClass(Handle(node), name, enabled);
    }

    public void Listen(IDomNode node, string eventName, string payload)
    {
        VerifyAccess();
        if (_updates.Count > 0) Write(DomWriteKind.Listen, Index(node), StringIndex(eventName), StringIndex(payload));
        else BrowserInterop.Listen(Handle(node), eventName, payload);
    }

    public void InsertBefore(IDomNode parent, IDomNode child, IDomNode? before)
    {
        VerifyAccess();
        if (_updates.Count > 0) {
            Write(DomWriteKind.Insert, Index(parent), Index(child), before is null ? -1 : Index(before));
        }
        else {
            BrowserInterop.InsertBefore(Handle(parent), Handle(child), before is null ? null : Handle(before));
        }
    }

    public void Remove(IDomNode node)
    {
        VerifyAccess();
        if (_updates.Count > 0) Write(DomWriteKind.Remove, Index(node));
        else BrowserInterop.Remove(Handle(node));
    }

    public Task<string> WaitForEventAsync(CancellationToken cancellationToken)
    {
        VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        FlushPending();
        return BrowserInterop.WaitForEventAsync();
    }

    public void ScheduleEvent(string key, string payload, int delayMilliseconds)
    {
        VerifyAccess();
        FlushPending();
        BrowserInterop.ScheduleEvent(key, payload, delayMilliseconds);
    }

    public void CancelScheduledEvent(string key)
    {
        VerifyAccess();
        FlushPending();
        BrowserInterop.CancelScheduledEvent(key);
    }

    public void AttachEditorSurface(string cellId, IDomNode node)
    {
        VerifyAccess();
        BrowserInterop.AttachEditorSurface(cellId, Handle(node));
    }

    public void DetachEditorSurface(string cellId, IDomNode node)
    {
        VerifyAccess();
        BrowserInterop.DetachEditorSurface(cellId, Handle(node));
    }

    public void AcknowledgeEditorCommand(string cellId, int sequence)
    {
        VerifyAccess();
        FlushPending();
        BrowserInterop.AcknowledgeEditorCommand(cellId, sequence);
    }

    public void ResolveEditorClipboard(string cellId, int requestId, string? text)
    {
        VerifyAccess();
        FlushPending();
        BrowserInterop.ResolveEditorClipboard(cellId, requestId, text);
    }

    public void PrepareEditorReconciliation(IDomNode node, int sequence)
    {
        VerifyAccess();
        BrowserInterop.PrepareEditorReconciliation(Handle(node), sequence);
    }

    public string TakeEditorInput(IDomNode node, bool sourceBoundary)
    {
        VerifyAccess();
        return BrowserInterop.TakeEditorInput(Handle(node), sourceBoundary);
    }

    public void AttachGutterHeights(IDomNode gutter, IDomNode lines, string cellId)
    {
        VerifyAccess();
        BrowserInterop.AttachGutterHeights(Handle(gutter), Handle(lines), cellId);
    }

    public void DetachGutterHeights(IDomNode lines)
    {
        VerifyAccess();
        BrowserInterop.DetachGutterHeights(Handle(lines));
    }

    public void SetDocumentLines(IDomNode lines, int totalLines)
    {
        VerifyAccess();
        BrowserInterop.SetDocumentLines(Handle(lines), totalLines);
    }

    public EditorScrollPosition? ReadEditorScrollPosition(IDomNode lines)
    {
        VerifyAccess();
        var values = JsonSerializer.Deserialize<double[]>(BrowserInterop.ReadEditorScrollPosition(Handle(lines)));
        return values is { Length: 5 } ? new(values[0], values[1], (int)values[2], (int)values[3], values[4]) : null;
    }

    public void RestoreEditorScroll(IDomNode lines, int generation, EditorScrollPosition position)
    {
        VerifyAccess();
        BrowserInterop.RestoreEditorScroll(Handle(lines), generation, position.Top, position.Left, position.Line, position.Column, position.Offset);
    }

    public void SetEditorSelection(IDomNode node, int anchorLine, int anchorColumn, int headLine, int headColumn, bool reveal)
    {
        VerifyAccess();
        if (!_updates.Contains(Owned(node))) {
            BrowserInterop.SetEditorSelection(Handle(node), anchorLine, anchorColumn, headLine, headColumn, reveal);
            return;
        }
        Write(DomWriteKind.Selection, Index(node), anchorLine, anchorColumn);
        _writes.Add(headLine);
        _writes.Add(headColumn);
        _writes.Add(reveal ? 1 : 0);
    }

    public void PlaceOverlay(IDomNode container, IDomNode surface, IDomNode overlay, int lineIndex, int column, int endLineIndex = -1, int endColumn = -1)
    {
        VerifyAccess();
        BrowserInterop.PlaceOverlay(Handle(container), Handle(surface), Handle(overlay), lineIndex, column, endLineIndex, endColumn);
    }

    public void ClearOverlayPlacement(IDomNode overlay)
    {
        VerifyAccess();
        BrowserInterop.ClearOverlayPlacement(Handle(overlay));
    }

    public void EnsureVisible(IDomNode container, IDomNode element)
    {
        VerifyAccess();
        BrowserInterop.EnsureVisible(Handle(container), Handle(element));
    }

    public void NotifyAppReady()
    {
        VerifyAccess();
        FlushPending();
        BrowserInterop.AppReady();
    }

    public void ReportError(string message)
    {
        VerifyAccess();
        FlushPending();
        BrowserInterop.ReportError(message);
    }

    public void Flush()
    {
        VerifyAccess();
        FlushPending();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _mainThread.VerifyAccess();
        List<Exception>? errors = null;
        void Attempt(Action action)
        {
            try { action(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        Attempt(() => FlushPending());
        foreach (var scope in _updates.Distinct()) {
            Attempt(() => BrowserInterop.SetAttribute(scope.Handle!, "data-editor-updating", "false"));
        }
        _updates.Clear();
        Attempt(ReleasePending);
        foreach (var handle in _handles.Keys) Attempt(handle.Dispose);
        _handles.Clear();
        _disposed = true;
        if (errors is not null) throw new AggregateException(errors);
    }

    private void VerifyAccess()
    {
        _mainThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private BrowserDomNode Owned(IDomNode node, bool allowDisposed = false)
    {
        if (node is not BrowserDomNode owned || owned.Owner != this) {
            throw new ArgumentException("The DOM node belongs to a different backend.", nameof(node));
        }
        if (!allowDisposed) ObjectDisposedException.ThrowIf(owned.Disposed, owned);
        return owned;
    }

    private JSObject Handle(IDomNode node)
    {
        var owned = Owned(node);
        FlushPending();
        return owned.Handle ?? throw new InvalidOperationException("The DOM node has not been created.");
    }

    private int Index(IDomNode node)
    {
        var owned = Owned(node);
        if (_indexes.TryGetValue(owned, out var index)) return index;
        index = _nodes.Count;
        _nodes.Add(owned);
        _indexes.Add(owned, index);
        return index;
    }

    private int StringIndex(string value)
    {
        if (_valueIndexes.TryGetValue(value, out var existing)) return existing;
        var index = _values.Count;
        _values.Add(value);
        _valueIndexes.Add(value, index);
        return index;
    }

    private void Write(DomWriteKind kind, int node, int first = -1, int second = -1)
    {
        _writes.Add((int)kind);
        _writes.Add(node);
        _writes.Add(first);
        _writes.Add(second);
    }

    private void FlushPending(BrowserDomNode? completingScope = null)
    {
        if (_nodes.Count == 0) {
            if (completingScope is not null) {
                BrowserInterop.SetAttribute(completingScope.Handle!, "data-editor-updating", "false");
            }
            return;
        }
        var nodes = _nodes.ToArray();
        var handles = nodes.Select(static node => node.Handle).ToArray();
        var tags = nodes.Select(static node => node.Handle is null ? node.Tag! : string.Empty).ToArray();
        var values = _values.ToArray();
        var writes = _writes.ToArray();
        _nodes.Clear();
        _indexes.Clear();
        _writes.Clear();
        _values.Clear();
        _valueIndexes.Clear();
        try {
            var created = BrowserInterop.ApplyDomBatch(handles, tags, values, writes, completingScope?.Handle);
            var pending = nodes.Where(static node => node.Handle is null).ToArray();
            if (created.Length != pending.Length + 1) {
                foreach (var handle in created) handle?.Dispose();
                throw new InvalidOperationException("The DOM batch returned an invalid node count.");
            }
            for (var index = 0; index < pending.Length; index++) pending[index].Handle = Retain(created[index]);
            using var status = created[^1];
            if (status is not null) {
                throw new InvalidOperationException(status.GetPropertyAsString("error") ?? "The DOM batch failed.");
            }
        }
        finally {
            ReleasePending();
        }
    }

    private void Release(BrowserDomNode node)
    {
        _mainThread.VerifyAccess();
        if (node.Disposed) return;
        node.Disposed = true;
        if (_updates.Count > 0 || _nodes.Count > 0) _releases.Add(node);
        else ReleaseHandle(node.Handle);
    }

    private JSObject? Retain(JSObject? handle)
    {
        if (handle is not null) _handles[handle] = _handles.GetValueOrDefault(handle) + 1;
        return handle;
    }

    private void ReleaseHandle(JSObject? handle)
    {
        if (handle is null || !_handles.TryGetValue(handle, out var count)) return;
        if (count > 1) _handles[handle] = count - 1;
        else { _handles.Remove(handle); handle.Dispose(); }
    }

    private void ReleasePending()
    {
        List<Exception>? errors = null;
        for (var index = _releases.Count - 1; index >= 0; index--) {
            var node = _releases[index];
            if (_updates.Contains(node)) continue;
            try { ReleaseHandle(node.Handle); }
            catch (Exception error) { (errors ??= []).Add(error); }
            finally { _releases.RemoveAt(index); }
        }
        if (errors is not null) throw new AggregateException(errors);
    }

    private enum DomWriteKind
    {
        Text,
        Runs,
        Id,
        Attribute,
        Class,
        Listen,
        Insert,
        Remove,
        Selection,
        EditorLines,
        ActiveEditorLine,
        ClearEditorLines,
        DisposeEditorLines
    }

    private sealed class BrowserDomNode(BrowserDomBackend owner, JSObject? handle, string? tag = null) : IDomNode
    {
        public BrowserDomBackend Owner { get; } = owner;
        public JSObject? Handle { get; set; } = owner.Retain(handle);
        public string? Tag { get; } = tag;
        public bool Disposed { get; set; }
        public void Dispose() => Owner.Release(this);
    }
}

internal static partial class BrowserInterop
{
    [JSImport("applyDomBatch", "main.js")]
    [return: JSMarshalAs<JSType.Array<JSType.Object>>]
    public static partial JSObject?[] ApplyDomBatch(
        [JSMarshalAs<JSType.Array<JSType.Object>>] JSObject?[] nodes,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] tags,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] values,
        [JSMarshalAs<JSType.Array<JSType.Number>>] int[] writes,
        JSObject? completingScope);

    [JSImport("find", "main.js")]
    public static partial JSObject Find(string id);

    [JSImport("tryFind", "main.js")]
    public static partial JSObject? TryFind(string id);

    [JSImport("create", "main.js")]
    public static partial JSObject Create(string tagName);

    [JSImport("createText", "main.js")]
    public static partial JSObject CreateText(string value);

    [JSImport("setText", "main.js")]
    public static partial void SetText(JSObject element, string value);

    [JSImport("setTextRuns", "main.js")]
    public static partial void SetTextRuns(
        JSObject element,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] text,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] classes);

    [JSImport("getText", "main.js")]
    public static partial string GetText(JSObject element);

    [JSImport("getValue", "main.js")]
    public static partial string GetValue(JSObject element);

    [JSImport("focus", "main.js")]
    public static partial void Focus(JSObject element);

    [JSImport("readKeymap", "main.js")]
    public static partial string? ReadKeymap();

    [JSImport("writeKeymap", "main.js")]
    public static partial void WriteKeymap(string json);

    [JSImport("setId", "main.js")]
    public static partial void SetId(JSObject element, string id);

    [JSImport("setAttr", "main.js")]
    public static partial void SetAttribute(JSObject element, string name, string value);

    [JSImport("toggleClass", "main.js")]
    public static partial void ToggleClass(JSObject element, string name, bool enabled);

    [JSImport("listen", "main.js")]
    public static partial void Listen(JSObject element, string eventName, string payload);

    [JSImport("insertBefore", "main.js")]
    public static partial void InsertBefore(
        JSObject parent,
        JSObject child,
        JSObject? before);

    [JSImport("remove", "main.js")]
    public static partial void Remove(JSObject element);

    [JSImport("waitForEvent", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string> WaitForEventAsync();

    [JSImport("scheduleEvent", "main.js")]
    public static partial void ScheduleEvent(
        string key,
        string payload,
        int delayMilliseconds);

    [JSImport("cancelScheduledEvent", "main.js")]
    public static partial void CancelScheduledEvent(string key);

    [JSImport("attachEditorSurface", "main.js")]
    public static partial void AttachEditorSurface(string cellId, JSObject element);

    [JSImport("detachEditorSurface", "main.js")]
    public static partial void DetachEditorSurface(string cellId, JSObject element);

    [JSImport("acknowledgeEditorCommand", "main.js")]
    public static partial void AcknowledgeEditorCommand(string cellId, int sequence);

    [JSImport("resolveEditorClipboard", "main.js")]
    public static partial void ResolveEditorClipboard(string cellId, int requestId, string? text);

    [JSImport("prepareEditorReconciliation", "main.js")]
    public static partial void PrepareEditorReconciliation(JSObject element, int sequence);

    [JSImport("takeEditorInput", "main.js")]
    public static partial string TakeEditorInput(JSObject element, bool sourceBoundary);

    [JSImport("attachGutterHeights", "main.js")]
    public static partial void AttachGutterHeights(JSObject gutter, JSObject lines, string cellId);

    [JSImport("detachGutterHeights", "main.js")]
    public static partial void DetachGutterHeights(JSObject lines);

    [JSImport("setDocumentLines", "main.js")]
    public static partial void SetDocumentLines(JSObject lines, int totalLines);

    [JSImport("readEditorScrollPosition", "main.js")]
    public static partial string ReadEditorScrollPosition(JSObject lines);

    [JSImport("restoreEditorScroll", "main.js")]
    public static partial void RestoreEditorScroll(
        JSObject lines, int generation, double top, double left, int line, int column, double offset);

    [JSImport("setEditorSelection", "main.js")]
    public static partial void SetEditorSelection(
        JSObject element,
        int anchorLine,
        int anchorColumn,
        int headLine,
        int headColumn,
        bool reveal);

    [JSImport("placeOverlay", "main.js")]
    public static partial void PlaceOverlay(
        JSObject container,
        JSObject surface,
        JSObject overlay,
        int lineIndex,
        int column,
        int endLineIndex,
        int endColumn);

    [JSImport("clearOverlayPlacement", "main.js")]
    public static partial void ClearOverlayPlacement(JSObject overlay);

    [JSImport("ensureVisible", "main.js")]
    public static partial void EnsureVisible(JSObject container, JSObject element);

    [JSImport("appReady", "main.js")]
    public static partial void AppReady();

    [JSImport("reportError", "main.js")]
    public static partial void ReportError(string message);
}
#endif
