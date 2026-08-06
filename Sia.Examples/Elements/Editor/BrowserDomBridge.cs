#if BROWSER
using System.Runtime.InteropServices.JavaScript;

namespace Sia_Examples.Editor;

public static partial class BrowserDomBridge
{
    [JSImport("find",              "main.js")] public static partial JSObject Find(string id);
    [JSImport("create",            "main.js")] public static partial JSObject Create(string tag);
    [JSImport("createText",        "main.js")] public static partial JSObject CreateText(string value);
    [JSImport("remove",            "main.js")] public static partial void   Remove(JSObject element);
    [JSImport("insertBefore",      "main.js")] public static partial void   InsertBefore(JSObject parent, JSObject child, JSObject? before);

    [JSImport("setText",           "main.js")] public static partial void   SetText(JSObject element, string value);
    [JSImport("getText",           "main.js")] public static partial string GetText(JSObject element);
    [JSImport("setId",             "main.js")] public static partial void   SetId(JSObject element, string id);
    [JSImport("setCssText",        "main.js")] public static partial void   SetCssText(JSObject element, string css);
    [JSImport("toggleClass",       "main.js")] public static partial void   ToggleClass(JSObject element, string name, bool on);
    [JSImport("setAttr",           "main.js")] public static partial void   SetAttr(JSObject element, string name, string value);
    [JSImport("focus",             "main.js")] public static partial void   Focus(JSObject element);

    [JSImport("syncGutterScroll",   "main.js")] public static partial void SyncGutterScroll(JSObject scrollEl, JSObject gutterEl);

    [JSImport("attachEditorSurface", "main.js")] public static partial void AttachEditorSurface(string cellId, JSObject element);
    [JSImport("detachEditorSurface", "main.js")] public static partial void DetachEditorSurface(string cellId, JSObject element);

    [JSImport("setEditorSelection", "main.js")]
    public static partial void SetEditorSelection(JSObject linesElement, int anchorLine, int anchorCol, int headLine, int headCol);

    [JSImport("setInnerHtml",       "main.js")] public static partial void SetInnerHtml(JSObject element, string html);

}
#endif