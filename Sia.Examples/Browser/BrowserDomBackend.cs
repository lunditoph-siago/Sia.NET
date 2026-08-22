#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using Sia_Examples.Dom;

namespace Sia_Examples.Browser;

internal sealed class BrowserDomBackend(BrowserMainThread mainThread) : IDomBackend
{
    private readonly BrowserMainThread _mainThread = mainThread;

    public IDomNode Find(string id)
    {
        VerifyAccess();
        return new BrowserDomNode(BrowserInterop.Find(id));
    }

    public IDomNode? TryFind(string id)
    {
        VerifyAccess();
        return BrowserInterop.TryFind(id) is { } handle
            ? new BrowserDomNode(handle)
            : null;
    }

    public IDomNode Create(string tagName)
    {
        VerifyAccess();
        return new BrowserDomNode(BrowserInterop.Create(tagName));
    }

    public IDomNode CreateText(string value)
    {
        VerifyAccess();
        return new BrowserDomNode(BrowserInterop.CreateText(value));
    }

    public void SetText(IDomNode node, string value)
    {
        VerifyAccess();
        BrowserInterop.SetText(Handle(node), value);
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

    public void SetId(IDomNode node, string id)
    {
        VerifyAccess();
        BrowserInterop.SetId(Handle(node), id);
    }

    public void SetAttribute(IDomNode node, string name, string value)
    {
        VerifyAccess();
        BrowserInterop.SetAttribute(Handle(node), name, value);
    }

    public void ToggleClass(IDomNode node, string name, bool enabled)
    {
        VerifyAccess();
        BrowserInterop.ToggleClass(Handle(node), name, enabled);
    }

    public void Listen(IDomNode node, string eventName, string payload)
    {
        VerifyAccess();
        BrowserInterop.Listen(Handle(node), eventName, payload);
    }

    public void InsertBefore(IDomNode parent, IDomNode child, IDomNode? before)
    {
        VerifyAccess();
        BrowserInterop.InsertBefore(
            Handle(parent),
            Handle(child),
            before is null ? null : Handle(before));
    }

    public void Remove(IDomNode node)
    {
        VerifyAccess();
        BrowserInterop.Remove(Handle(node));
    }

    public Task<string> WaitForEventAsync(CancellationToken cancellationToken)
    {
        VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        return BrowserInterop.WaitForEventAsync();
    }

    public void ScheduleEvent(string key, string payload, int delayMilliseconds)
    {
        VerifyAccess();
        BrowserInterop.ScheduleEvent(key, payload, delayMilliseconds);
    }

    public void CancelScheduledEvent(string key)
    {
        VerifyAccess();
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
        BrowserInterop.AcknowledgeEditorCommand(cellId, sequence);
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

    public void ScrollLineIntoView(IDomNode lines, double targetTop)
    {
        VerifyAccess();
        BrowserInterop.ScrollLineIntoView(Handle(lines), targetTop);
    }

    public void SetEditorSelection(
        IDomNode node,
        int anchorLine,
        int anchorColumn,
        int headLine,
        int headColumn)
    {
        VerifyAccess();
        BrowserInterop.SetEditorSelection(
            Handle(node),
            anchorLine,
            anchorColumn,
            headLine,
            headColumn);
    }

    public void PlaceOverlay(
        IDomNode container,
        IDomNode surface,
        IDomNode overlay,
        int lineIndex,
        int column)
    {
        VerifyAccess();
        BrowserInterop.PlaceOverlay(
            Handle(container),
            Handle(surface),
            Handle(overlay),
            lineIndex,
            column);
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

    public void ReportError(string message)
    {
        VerifyAccess();
        BrowserInterop.ReportError(message);
    }

    public void Flush() => VerifyAccess();

    public void Dispose() => VerifyAccess();

    private void VerifyAccess() => _mainThread.VerifyAccess();

    private static JSObject Handle(IDomNode node)
        => node is BrowserDomNode browserNode
            ? browserNode.Handle
            : throw new ArgumentException("The DOM node belongs to a different backend.", nameof(node));

    private sealed class BrowserDomNode(JSObject handle) : IDomNode
    {
        public JSObject Handle { get; } = handle;

        public void Dispose() => Handle.Dispose();
    }
}

internal static partial class BrowserInterop
{
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

    [JSImport("getText", "main.js")]
    public static partial string GetText(JSObject element);

    [JSImport("getValue", "main.js")]
    public static partial string GetValue(JSObject element);

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

    [JSImport("attachGutterHeights", "main.js")]
    public static partial void AttachGutterHeights(JSObject gutter, JSObject lines, string cellId);

    [JSImport("detachGutterHeights", "main.js")]
    public static partial void DetachGutterHeights(JSObject lines);

    [JSImport("setDocumentLines", "main.js")]
    public static partial void SetDocumentLines(JSObject lines, int totalLines);

    [JSImport("scrollLineIntoView", "main.js")]
    public static partial void ScrollLineIntoView(JSObject lines, double targetTop);

    [JSImport("setEditorSelection", "main.js")]
    public static partial void SetEditorSelection(
        JSObject element,
        int anchorLine,
        int anchorColumn,
        int headLine,
        int headColumn);

    [JSImport("placeOverlay", "main.js")]
    public static partial void PlaceOverlay(
        JSObject container,
        JSObject surface,
        JSObject overlay,
        int lineIndex,
        int column);

    [JSImport("clearOverlayPlacement", "main.js")]
    public static partial void ClearOverlayPlacement(JSObject overlay);

    [JSImport("ensureVisible", "main.js")]
    public static partial void EnsureVisible(JSObject container, JSObject element);

    [JSImport("reportError", "main.js")]
    public static partial void ReportError(string message);
}
#endif
