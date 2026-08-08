using System.Runtime.InteropServices.JavaScript;

namespace Sia_Examples;

public sealed class BrowserElement(JSObject handle) : IDisposable
{
    internal JSObject Handle { get; } = handle;

    public static BrowserElement Find(string id) => new(BrowserDom.Find(id));

    public static BrowserElement? TryFind(string id)
        => BrowserDom.TryFind(id) is { } handle ? new(handle) : null;

    public static BrowserElement Create(string tagName) => new(BrowserDom.Create(tagName));

    public static BrowserElement CreateText(string value) => new(BrowserDom.CreateText(value));

    public BrowserElement Id(string id)
    {
        BrowserDom.SetId(Handle, id);
        return this;
    }

    public BrowserElement Class(string className) => ToggleClass(className, true);

    public BrowserElement ToggleClass(string className, bool enabled)
    {
        BrowserDom.ToggleClass(Handle, className, enabled);
        return this;
    }

    public BrowserElement Text(string value)
    {
        BrowserDom.SetText(Handle, value);
        return this;
    }

    public string TextContent() => BrowserDom.GetText(Handle);

    public string Value() => BrowserDom.GetValue(Handle);

    public BrowserElement Attr(string name, string value)
    {
        BrowserDom.SetAttribute(Handle, name, value);
        return this;
    }

    public BrowserElement On(string eventName, string payload)
    {
        BrowserDom.Listen(Handle, eventName, payload);
        return this;
    }

    public BrowserElement Append(BrowserElement child)
    {
        InsertBefore(child, null);
        return this;
    }

    public void InsertBefore(BrowserElement child, BrowserElement? before)
        => BrowserDom.InsertBefore(Handle, child.Handle, before?.Handle);

    public void Remove() => BrowserDom.Remove(Handle);

    public void Dispose()
    {
        BrowserDom.VerifyAccess();
        Handle.Dispose();
    }
}
