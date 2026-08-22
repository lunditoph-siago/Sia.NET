namespace Sia_Examples.Dom;

internal interface IDomBackend : IDisposable
{
    public IDomNode Find(string id);

    public IDomNode? TryFind(string id);

    public IDomNode Create(string tagName);

    public IDomNode CreateText(string value);

    public void SetText(IDomNode node, string value);

    public string GetText(IDomNode node);

    public string GetValue(IDomNode node);

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

    public void AttachGutterHeights(IDomNode gutter, IDomNode lines, string cellId);

    public void DetachGutterHeights(IDomNode lines);

    public void SetDocumentLines(IDomNode lines, int totalLines);

    public void ScrollLineIntoView(IDomNode lines, double targetTop);

    public void SetEditorSelection(
        IDomNode node,
        int anchorLine,
        int anchorColumn,
        int headLine,
        int headColumn);

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
