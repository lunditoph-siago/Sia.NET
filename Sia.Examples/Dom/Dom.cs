namespace Sia_Examples.Dom;

internal static class DomRuntime
{
    private static IDomBackend? _backend;

    public static void Initialize(IDomBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (_backend is not null) {
            throw new InvalidOperationException("The DOM backend is already initialized.");
        }
        _backend = backend;
    }

    public static IDomBackend Backend
        => _backend ?? throw new InvalidOperationException(
            "The DOM backend is not initialized.");

    public static Task<string> WaitForEventAsync(
        CancellationToken cancellationToken = default)
        => Backend.WaitForEventAsync(cancellationToken);

    public static void ScheduleEvent(
        string key,
        string payload,
        int delayMilliseconds)
        => Backend.ScheduleEvent(key, payload, delayMilliseconds);

    public static void CancelScheduledEvent(string key)
        => Backend.CancelScheduledEvent(key);

    public static void AttachEditorSurface(string cellId, DomElement element)
        => Backend.AttachEditorSurface(cellId, element.Node);

    public static void DetachEditorSurface(string cellId, DomElement element)
        => Backend.DetachEditorSurface(cellId, element.Node);

    public static void AcknowledgeEditorCommand(string cellId, int sequence)
        => Backend.AcknowledgeEditorCommand(cellId, sequence);

    public static void AttachGutterHeights(DomElement gutter, DomElement lines, string cellId)
        => Backend.AttachGutterHeights(gutter.Node, lines.Node, cellId);

    public static void DetachGutterHeights(DomElement lines)
        => Backend.DetachGutterHeights(lines.Node);

    public static void SetDocumentLines(DomElement lines, int totalLines)
        => Backend.SetDocumentLines(lines.Node, totalLines);

    public static void ScrollLineIntoView(DomElement lines, double targetTop)
        => Backend.ScrollLineIntoView(lines.Node, targetTop);

    public static void SetEditorSelection(
        DomElement element,
        int anchorLine,
        int anchorColumn,
        int headLine,
        int headColumn)
        => Backend.SetEditorSelection(
            element.Node,
            anchorLine,
            anchorColumn,
            headLine,
            headColumn);

    public static void PlaceOverlay(
        DomElement container,
        DomElement surface,
        DomElement overlay,
        int lineIndex,
        int column)
        => Backend.PlaceOverlay(
            container.Node,
            surface.Node,
            overlay.Node,
            lineIndex,
            column);

    public static void ClearOverlayPlacement(DomElement overlay)
        => Backend.ClearOverlayPlacement(overlay.Node);

    public static void EnsureVisible(DomElement container, DomElement element)
        => Backend.EnsureVisible(container.Node, element.Node);

    public static void ReportError(string message) => Backend.ReportError(message);

    public static void Flush() => Backend.Flush();

    public static void Dispose()
    {
        _backend?.Dispose();
        _backend = null;
    }
}
