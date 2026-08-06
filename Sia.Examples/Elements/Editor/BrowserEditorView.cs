#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class BrowserEditorView : IEditorRenderer, IDisposable
{
    private readonly string _cellId;
    private readonly JSObject _container;
    private readonly ViewportTracker _viewportTracker = new(int.MaxValue);

    private JSObject _gutter = null!;
    private JSObject _scroll = null!;
    private JSObject _lines = null!;
    private JSObject _status = null!;

    private readonly List<JSObject> _lineNodes = [];
    private readonly List<string> _lineSignature = [];
    private readonly List<JSObject> _gutterNodes = [];
    private readonly List<string> _gutterSignature = [];
    private int _lineCount;
    private string? _statusSignature;
    private bool _suppressSelectionSync;
    private int _nativeEditLine = -1;

    public int VisibleLines => int.MaxValue;

    public JSObject Surface => _lines;

    public BrowserEditorView(string cellId, JSObject container)
    {
        _cellId = cellId;
        _container = container;
    }

    public void Build()
    {
        BrowserDomBridge.SetCssText(_container, "");
        BrowserDomBridge.ToggleClass(_container, "editor-container", true);

        _gutter = BrowserDomBridge.Create("div");
        BrowserDomBridge.ToggleClass(_gutter, "editor-gutter", true);

        _scroll = BrowserDomBridge.Create("div");
        BrowserDomBridge.ToggleClass(_scroll, "editor-scroll", true);

        _lines = BrowserDomBridge.Create("div");
        BrowserDomBridge.ToggleClass(_lines, "editor-lines", true);
        BrowserDomBridge.SetAttr(_lines, "contenteditable", "true");
        BrowserDomBridge.SetAttr(_lines, "spellcheck", "false");
        BrowserDomBridge.SetAttr(_lines, "autocorrect", "off");
        BrowserDomBridge.SetAttr(_lines, "autocapitalize", "off");

        _status = BrowserDomBridge.Create("div");
        BrowserDomBridge.ToggleClass(_status, "editor-status", true);

        BrowserDomBridge.InsertBefore(_scroll, _lines, null);
        BrowserDomBridge.InsertBefore(_container, _gutter, null);
        BrowserDomBridge.InsertBefore(_container, _scroll, null);
        BrowserDomBridge.InsertBefore(_container, _status, null);

        BrowserDomBridge.SyncGutterScroll(_scroll, _gutter);
        BrowserDomBridge.AttachEditorSurface(_cellId, _lines);
    }

    public void SuppressNextSelectionSync() => _suppressSelectionSync = true;

    public void NoteNativeLineEdit(int lineIndex) => _nativeEditLine = lineIndex;

    public ViewportState AdvanceViewport(int cursorLine, int totalLines) => _viewportTracker.Advance(cursorLine, totalLines);

    public void PrepareChanges(ChangeDesc changes, Text oldDoc, Text newDoc)
    {
        if (changes.IsEmpty || _lineNodes.Count == 0) return;

        int[] map;
        try {
            map = LineReuseMap.Compute(changes, oldDoc, newDoc);
        }
        catch {
            ResetLineCache();
            return;
        }

        var firstTouched = 0;
        while (firstTouched < map.Length && firstTouched < _lineNodes.Count && map[firstTouched] == firstTouched) firstTouched++;
        if (firstTouched >= _lineNodes.Count) return;

        var newCount = newDoc.Lines;
        var slotLine = new JSObject?[newCount];
        var slotLineSig = new string?[newCount];
        var slotGutter = new JSObject?[newCount];
        var slotGutterSig = new string?[newCount];
        for (var oldIdx = 0; oldIdx < map.Length && oldIdx < _lineNodes.Count; oldIdx++) {
            var newIdx = map[oldIdx];
            if (newIdx < 0 || newIdx >= newCount) continue;
            slotLine[newIdx] = _lineNodes[oldIdx];
            slotLineSig[newIdx] = _lineSignature[oldIdx];
            slotGutter[newIdx] = _gutterNodes[oldIdx];
            slotGutterSig[newIdx] = _gutterSignature[oldIdx];
        }

        for (var i = _lineNodes.Count - 1; i >= firstTouched; i--) {
            if (Array.IndexOf(slotLine, _lineNodes[i]) < 0) BrowserDomBridge.Remove(_lineNodes[i]);
            if (Array.IndexOf(slotGutter, _gutterNodes[i]) < 0) BrowserDomBridge.Remove(_gutterNodes[i]);
        }
        _lineNodes.RemoveRange(firstTouched, _lineNodes.Count - firstTouched);
        _lineSignature.RemoveRange(firstTouched, _lineSignature.Count - firstTouched);
        _gutterNodes.RemoveRange(firstTouched, _gutterNodes.Count - firstTouched);
        _gutterSignature.RemoveRange(firstTouched, _gutterSignature.Count - firstTouched);

        for (var i = firstTouched; i < newCount; i++) {
            var ln = slotLine[i];
            if (ln is null) { ln = BrowserDomBridge.Create("div"); BrowserDomBridge.ToggleClass(ln, "cm-line", true); }
            BrowserDomBridge.SetAttr(ln, "data-ln", i.ToString());
            BrowserDomBridge.InsertBefore(_lines, ln, null);
            _lineNodes.Add(ln);
            _lineSignature.Add(slotLineSig[i] ?? "unset");

            var gn = slotGutter[i];
            if (gn is null) { gn = BrowserDomBridge.Create("div"); BrowserDomBridge.ToggleClass(gn, "editor-gutter-line", true); }
            BrowserDomBridge.InsertBefore(_gutter, gn, null);
            _gutterNodes.Add(gn);
            _gutterSignature.Add(slotGutterSig[i] ?? "unset");
        }
    }

    private void ResetLineCache()
    {
        foreach (var n in _lineNodes) BrowserDomBridge.Remove(n);
        foreach (var n in _gutterNodes) BrowserDomBridge.Remove(n);
        _lineNodes.Clear(); _lineSignature.Clear();
        _gutterNodes.Clear(); _gutterSignature.Clear();
        _lineCount = 0;
    }

    public void BeginRender()
    {
        _lineCount = 0;
    }

    public void RenderGutter(int screenRow, int lineIndex, bool isCursorLine)
    {
        _lineCount = Math.Max(_lineCount, lineIndex + 1);
        EnsureGutterSlot(lineIndex);

        var sig = isCursorLine ? "*" : "";
        if (_gutterSignature[lineIndex] == sig) return;
        _gutterSignature[lineIndex] = sig;

        var node = _gutterNodes[lineIndex];
        BrowserDomBridge.ToggleClass(node, "active", isCursorLine);
        BrowserDomBridge.SetText(node, (lineIndex + 1).ToString());
    }

    public void RenderLine(int screenRow, int lineIndex, string text, IReadOnlyList<StyledRun>? runs, string? lineClass)
    {
        _lineCount = Math.Max(_lineCount, lineIndex + 1);
        EnsureLineSlot(lineIndex);

        var sig = text + "" + (lineClass ?? "") + "" + SignatureOf(runs);
        if (_lineSignature[lineIndex] == sig) return;

        var node = _lineNodes[lineIndex];
        var wasNativeEdit = _nativeEditLine == lineIndex;
        if (wasNativeEdit) _nativeEditLine = -1;

        if (wasNativeEdit && runs is null && BrowserDomBridge.GetText(node) == text) {
            _lineSignature[lineIndex] = sig;
            BrowserDomBridge.SetAttr(node, "class", lineClass is null ? "cm-line" : "cm-line " + lineClass);
            return;
        }

        _lineSignature[lineIndex] = sig;
        BrowserDomBridge.SetAttr(node, "class", lineClass is null ? "cm-line" : "cm-line " + lineClass);
        if (runs is null) {
            BrowserDomBridge.SetText(node, text);
        } else {
            BuildSpans(node, runs);
        }
    }

    public void RenderSelection(int anchorLineIndex, int anchorCol, int headLineIndex, int headCol, bool empty)
    {
        if (_suppressSelectionSync) {
            _suppressSelectionSync = false;
            return;
        }
        BrowserDomBridge.SetEditorSelection(_lines, anchorLineIndex, anchorCol, headLineIndex, headCol);
    }

    public void RenderStatus(string left, string right)
    {
        var sig = left + "" + right;
        if (_statusSignature == sig) return;
        _statusSignature = sig;
        BrowserDomBridge.SetInnerHtml(_status, $"<span>{EscapeHtml(left)}</span><span>{EscapeHtml(right)}</span>");
    }

    public void EndRender()
    {
        TrimTail(_lineNodes, _lineSignature, _lineCount);
        TrimTail(_gutterNodes, _gutterSignature, _lineCount);
    }

    private void EnsureLineSlot(int lineIndex)
    {
        while (_lineNodes.Count <= lineIndex) {
            var node = BrowserDomBridge.Create("div");
            BrowserDomBridge.ToggleClass(node, "cm-line", true);
            BrowserDomBridge.SetAttr(node, "data-ln", _lineNodes.Count.ToString());
            BrowserDomBridge.InsertBefore(_lines, node, null);
            _lineNodes.Add(node);
            _lineSignature.Add("unset");
        }
    }

    private void EnsureGutterSlot(int lineIndex)
    {
        while (_gutterNodes.Count <= lineIndex) {
            var node = BrowserDomBridge.Create("div");
            BrowserDomBridge.ToggleClass(node, "editor-gutter-line", true);
            BrowserDomBridge.InsertBefore(_gutter, node, null);
            _gutterNodes.Add(node);
            _gutterSignature.Add("unset");
        }
    }

    private static void TrimTail(List<JSObject> nodes, List<string> signatures, int keep)
    {
        while (nodes.Count > keep) {
            var last = nodes.Count - 1;
            BrowserDomBridge.Remove(nodes[last]);
            nodes.RemoveAt(last);
            signatures.RemoveAt(last);
        }
    }

    private static void BuildSpans(JSObject lineNode, IReadOnlyList<StyledRun> runs)
    {
        BrowserDomBridge.SetText(lineNode, "");
        foreach (var run in runs) {
            if (run.Class is null) {
                BrowserDomBridge.InsertBefore(lineNode, BrowserDomBridge.CreateText(run.Text), null);
                continue;
            }
            var span = BrowserDomBridge.Create("span");
            BrowserDomBridge.ToggleClass(span, CSharpHighlighter.CssClass(run.Class), true);
            BrowserDomBridge.SetText(span, run.Text);
            BrowserDomBridge.InsertBefore(lineNode, span, null);
        }
    }

    private static string SignatureOf(IReadOnlyList<StyledRun>? runs)
    {
        if (runs is null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var r in runs) sb.Append(r.Class).Append(':').Append(r.Text.Length).Append(';');
        return sb.ToString();
    }

    private static string EscapeHtml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public void Dispose()
    {
        BrowserDomBridge.DetachEditorSurface(_cellId, _lines);
        BrowserDomBridge.Remove(_gutter);
        BrowserDomBridge.Remove(_scroll);
        BrowserDomBridge.Remove(_status);
        BrowserDomBridge.ToggleClass(_container, "editor-container", false);
    }
}
#endif
