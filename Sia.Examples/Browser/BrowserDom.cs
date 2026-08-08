using System.Runtime.InteropServices.JavaScript;
using Sia_Examples.Browser;

namespace Sia_Examples;

internal static partial class BrowserDom
{
    private static BrowserMainThread? _mainThread;

    public static void Initialize(BrowserMainThread mainThread)
    {
        if (_mainThread is not null) {
            throw new InvalidOperationException("The browser DOM bridge is already initialized.");
        }
        _mainThread = mainThread;
    }

    public static void VerifyAccess()
        => (_mainThread
            ?? throw new InvalidOperationException("The browser DOM bridge is not initialized."))
            .VerifyAccess();

    public static JSObject Find(string id)
    {
        VerifyAccess();
        return FindCore(id);
    }

    public static JSObject? TryFind(string id)
    {
        VerifyAccess();
        return TryFindCore(id);
    }

    public static JSObject Create(string tagName)
    {
        VerifyAccess();
        return CreateCore(tagName);
    }

    public static JSObject CreateText(string value)
    {
        VerifyAccess();
        return CreateTextCore(value);
    }

    public static void SetText(JSObject element, string value)
    {
        VerifyAccess();
        SetTextCore(element, value);
    }

    public static string GetText(JSObject element)
    {
        VerifyAccess();
        return GetTextCore(element);
    }

    public static string GetValue(JSObject element)
    {
        VerifyAccess();
        return GetValueCore(element);
    }

    public static void SetId(JSObject element, string id)
    {
        VerifyAccess();
        SetIdCore(element, id);
    }

    public static void SetAttribute(JSObject element, string name, string value)
    {
        VerifyAccess();
        SetAttributeCore(element, name, value);
    }

    public static void ToggleClass(JSObject element, string name, bool enabled)
    {
        VerifyAccess();
        ToggleClassCore(element, name, enabled);
    }

    public static void Listen(JSObject element, string eventName, string payload)
    {
        VerifyAccess();
        ListenCore(element, eventName, payload);
    }

    public static void InsertBefore(JSObject parent, JSObject child, JSObject? before)
    {
        VerifyAccess();
        InsertBeforeCore(parent, child, before);
    }

    public static void Remove(JSObject element)
    {
        VerifyAccess();
        RemoveCore(element);
    }

    public static Task<string> WaitForEventAsync()
    {
        VerifyAccess();
        return WaitForEventCoreAsync();
    }

    public static void AttachEditorSurface(string cellId, JSObject element)
    {
        VerifyAccess();
        AttachEditorSurfaceCore(cellId, element);
    }

    public static void DetachEditorSurface(string cellId, JSObject element)
    {
        VerifyAccess();
        DetachEditorSurfaceCore(cellId, element);
    }

    public static void SetEditorSelection(
        JSObject element,
        int anchorLine,
        int anchorColumn,
        int headLine,
        int headColumn)
    {
        VerifyAccess();
        SetEditorSelectionCore(
            element,
            anchorLine,
            anchorColumn,
            headLine,
            headColumn);
    }

    public static void SyncGutterScroll(JSObject scroll, JSObject gutter)
    {
        VerifyAccess();
        SyncGutterScrollCore(scroll, gutter);
    }

    public static void ReportError(string message)
    {
        VerifyAccess();
        ReportErrorCore(message);
    }

    [JSImport("find", "main.js")]
    private static partial JSObject FindCore(string id);

    [JSImport("tryFind", "main.js")]
    private static partial JSObject? TryFindCore(string id);

    [JSImport("create", "main.js")]
    private static partial JSObject CreateCore(string tagName);

    [JSImport("createText", "main.js")]
    private static partial JSObject CreateTextCore(string value);

    [JSImport("setText", "main.js")]
    private static partial void SetTextCore(JSObject element, string value);

    [JSImport("getText", "main.js")]
    private static partial string GetTextCore(JSObject element);

    [JSImport("getValue", "main.js")]
    private static partial string GetValueCore(JSObject element);

    [JSImport("setId", "main.js")]
    private static partial void SetIdCore(JSObject element, string id);

    [JSImport("setAttr", "main.js")]
    private static partial void SetAttributeCore(JSObject element, string name, string value);

    [JSImport("toggleClass", "main.js")]
    private static partial void ToggleClassCore(JSObject element, string name, bool enabled);

    [JSImport("listen", "main.js")]
    private static partial void ListenCore(JSObject element, string eventName, string payload);

    [JSImport("insertBefore", "main.js")]
    private static partial void InsertBeforeCore(
        JSObject parent,
        JSObject child,
        JSObject? before);

    [JSImport("remove", "main.js")]
    private static partial void RemoveCore(JSObject element);

    [JSImport("waitForEvent", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> WaitForEventCoreAsync();

    [JSImport("attachEditorSurface", "main.js")]
    private static partial void AttachEditorSurfaceCore(string cellId, JSObject element);

    [JSImport("detachEditorSurface", "main.js")]
    private static partial void DetachEditorSurfaceCore(string cellId, JSObject element);

    [JSImport("setEditorSelection", "main.js")]
    private static partial void SetEditorSelectionCore(
        JSObject element,
        int anchorLine,
        int anchorColumn,
        int headLine,
        int headColumn);

    [JSImport("syncGutterScroll", "main.js")]
    private static partial void SyncGutterScrollCore(JSObject scroll, JSObject gutter);

    [JSImport("reportError", "main.js")]
    private static partial void ReportErrorCore(string message);
}
