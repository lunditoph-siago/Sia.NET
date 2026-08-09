namespace Sia_Examples.Dom;

public sealed class DomElement : IDisposable
{
    internal IDomNode Node { get; }

    private DomElement(IDomNode node)
    {
        Node = node;
    }

    public static DomElement Find(string id) => new(DomRuntime.Backend.Find(id));

    public static DomElement? TryFind(string id)
        => DomRuntime.Backend.TryFind(id) is { } node ? new(node) : null;

    public static DomElement Create(string tagName) => new(DomRuntime.Backend.Create(tagName));

    public static DomElement CreateText(string value) => new(DomRuntime.Backend.CreateText(value));

    public DomElement Id(string id)
    {
        DomRuntime.Backend.SetId(Node, id);
        return this;
    }

    public DomElement Class(string className) => ToggleClass(className, true);

    public DomElement ToggleClass(string className, bool enabled)
    {
        DomRuntime.Backend.ToggleClass(Node, className, enabled);
        return this;
    }

    public DomElement Text(string value)
    {
        DomRuntime.Backend.SetText(Node, value);
        return this;
    }

    public string TextContent() => DomRuntime.Backend.GetText(Node);

    public string Value() => DomRuntime.Backend.GetValue(Node);

    public DomElement Attr(string name, string value)
    {
        DomRuntime.Backend.SetAttribute(Node, name, value);
        return this;
    }

    public DomElement On(string eventName, string payload)
    {
        DomRuntime.Backend.Listen(Node, eventName, payload);
        return this;
    }

    public DomElement Append(DomElement child)
    {
        InsertBefore(child, null);
        return this;
    }

    public void InsertBefore(DomElement child, DomElement? before)
        => DomRuntime.Backend.InsertBefore(Node, child.Node, before?.Node);

    public void Remove() => DomRuntime.Backend.Remove(Node);

    public void Dispose() => Node.Dispose();
}
