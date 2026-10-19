namespace Sia.Examples.Dom;

internal interface IDomBackend : IDisposable
{
    public bool SupportsNativeInput { get; }

    public IDomNode Find(string id);

    public IDomNode? TryFind(string id);

    public IDomNode Create(string tagName);

    public IDomNode CreateText(string value);

    public void BeginUpdate(IDomNode node) => SetAttribute(node, "data-editor-updating", "true");

    public void EndUpdate(IDomNode node) => SetAttribute(node, "data-editor-updating", "false");

    public void SetText(IDomNode node, string value);

    public void SetTextRuns(IDomNode node, IReadOnlyList<DomTextRun> runs);

    public string GetText(IDomNode node);

    public string GetValue(IDomNode node);

    public void Focus(IDomNode node) { }

    public string? ReadKeymap() => null;

    public void WriteKeymap(string json) => throw new NotSupportedException("This host cannot save keyboard shortcuts.");

    public void SetId(IDomNode node, string id);

    public void SetAttribute(IDomNode node, string name, string value);

    public void ToggleClass(IDomNode node, string name, bool enabled);

    public void Listen(IDomNode node, string eventName, string payload);

    public void InsertBefore(IDomNode parent, IDomNode child, IDomNode? before);

    public void Remove(IDomNode node);

    public Task<string> WaitForEventAsync(CancellationToken cancellationToken);

    public void ScheduleEvent(string key, string payload, int delayMilliseconds);

    public void CancelScheduledEvent(string key);

    public void AttachEditorSurface(string cellId, IDomNode node);

    public void DetachEditorSurface(string cellId, IDomNode node);

    public void AcknowledgeEditorCommand(string cellId, int sequence);

    public void ResolveEditorClipboard(string cellId, int requestId, string? text);

    public void PrepareEditorReconciliation(IDomNode node, int sequence);

    public string TakeEditorInput(IDomNode node, bool sourceBoundary);

    public void AttachGutterHeights(IDomNode gutter, IDomNode lines, string cellId);

    public void DetachGutterHeights(IDomNode lines);

    public void SetDocumentLines(IDomNode lines, int totalLines);

    public EditorScrollPosition? ReadEditorScrollPosition(IDomNode lines);

    public void RestoreEditorScroll(IDomNode lines, int generation, EditorScrollPosition position);

    public void SetEditorSelection(
        IDomNode node,
        int anchorLine,
        int anchorColumn,
        int headLine,
        int headColumn,
        bool reveal);

    public void PlaceOverlay(
        IDomNode container,
        IDomNode surface,
        IDomNode overlay,
        int lineIndex,
        int column);

    public void ClearOverlayPlacement(IDomNode overlay);

    public void EnsureVisible(IDomNode container, IDomNode element);

    public void NotifyAppReady() { }

    public void ReportError(string message);

    public void Flush();
}
