#if !BROWSER
using Sia.Examples.Dom;

namespace Sia.Examples.Console;

internal sealed partial class ConsoleDomBackend : IDomBackend
{
    private readonly IConsoleTerminal _terminal;
    private readonly ConsoleDomNode _document;
    private readonly ConsoleDomNode _sidebar;
    private readonly ConsoleDomNode _content;
    private bool _disposed;

    public bool SupportsNativeInput => false;

    public ConsoleDomBackend(IConsoleTerminal terminal)
    {
        _terminal = terminal;
        _document = CreateDocument();
        _sidebar = FindNode("sidebar");
        _content = FindNode("content");
    }

    public IDomNode Find(string id)
        => TryFind(id) ?? throw new InvalidOperationException(
            $"The Console DOM does not contain an element with id '{id}'.");

    public IDomNode? TryFind(string id)
    {
        ThrowIfDisposed();
        return _document.DescendantsAndSelf()
            .FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));
    }

    public IDomNode Create(string tagName)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        return NewNode(tagName);
    }

    public IDomNode CreateText(string value)
    {
        ThrowIfDisposed();
        return new ConsoleDomNode(this, "#text") { Text = value };
    }

    public void SetText(IDomNode node, string value)
    {
        var target = AsNode(node);
        if (target.Children.Count == 0 && target.Text == value) {
            return;
        }
        foreach (var child in target.Children) {
            child.Parent = null;
        }
        target.Children.Clear();
        target.Text = value;
        MarkChanged();
    }

    public string GetText(IDomNode node) => AsNode(node).TextContent();

    public void SetTextRuns(IDomNode node, IReadOnlyList<DomTextRun> runs)
    {
        var target = AsNode(node);
        foreach (var child in target.Children) {
            child.Parent = null;
        }
        target.Children.Clear();
        target.Text = string.Empty;
        foreach (var run in runs) {
            var child = run.ClassName is null ? NewText(run.Text) : NewNode("span");
            if (run.ClassName is { } className) {
                child.Text = run.Text;
                child.Classes.Add(className);
            }
            AppendRaw(target, child);
        }
        MarkChanged();
    }

    public string GetValue(IDomNode node)
    {
        var target = AsNode(node);
        return target.Attributes.GetValueOrDefault("value", target.TextContent());
    }

    public void SetId(IDomNode node, string id) => SetAttribute(node, "id", id);

    public void SetAttribute(IDomNode node, string name, string value)
    {
        var target = AsNode(node);
        if (target.Attributes.TryGetValue(name, out var current) && current == value) {
            return;
        }
        target.Attributes[name] = value;
        MarkChanged();
    }

    public void ToggleClass(IDomNode node, string name, bool enabled)
    {
        var target = AsNode(node);
        var changed = enabled ? target.Classes.Add(name) : target.Classes.Remove(name);
        if (changed) {
            MarkChanged();
        }
    }

    public void Listen(IDomNode node, string eventName, string payload)
    {
        var target = AsNode(node);
        if (target.Listeners.TryGetValue(eventName, out var current) && current == payload) {
            return;
        }
        target.Listeners[eventName] = payload;
        MarkChanged();
    }

    public void InsertBefore(IDomNode parent, IDomNode child, IDomNode? before)
    {
        var targetParent = AsNode(parent);
        var targetChild = AsNode(child);
        var targetBefore = before is null ? null : AsNode(before);
        if (ReferenceEquals(targetChild, targetBefore)) {
            return;
        }
        if (targetBefore is not null && !ReferenceEquals(targetBefore.Parent, targetParent)) {
            throw new ArgumentException(
                "The reference node is not a child of the target parent.",
                nameof(before));
        }
        if (targetChild.Contains(targetParent)) {
            throw new InvalidOperationException("A DOM node cannot contain itself.");
        }

        targetChild.Parent?.Children.Remove(targetChild);
        targetChild.Parent = targetParent;
        var index = targetBefore is null
            ? targetParent.Children.Count
            : targetParent.Children.IndexOf(targetBefore);
        targetParent.Children.Insert(index, targetChild);
        MarkChanged();
    }

    public void Remove(IDomNode node)
    {
        var target = AsNode(node);
        if (target.Parent is not { } parent) {
            return;
        }
        parent.Children.Remove(target);
        target.Parent = null;
        if (ReferenceEquals(_focused, target) || target.Contains(_focused!)) {
            _focused = null;
        }
        MarkChanged();
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        DisposeEvents();
        _terminal.Dispose();
    }

    private ConsoleDomNode FindNode(string id)
        => _document.DescendantsAndSelf()
            .First(node => string.Equals(node.Id, id, StringComparison.Ordinal));

    private ConsoleDomNode NewNode(string tagName, string? id = null)
    {
        var node = new ConsoleDomNode(this, tagName);
        if (id is not null) {
            node.Attributes.Add("id", id);
        }
        return node;
    }

    private ConsoleDomNode NewText(string value)
        => new(this, "#text") { Text = value };

    private static void AppendRaw(ConsoleDomNode parent, ConsoleDomNode child)
    {
        parent.Children.Add(child);
        child.Parent = parent;
    }

    private ConsoleDomNode AsNode(IDomNode node)
    {
        ThrowIfDisposed();
        return node is ConsoleDomNode consoleNode
            && ReferenceEquals(consoleNode.Owner, this)
            ? consoleNode
            : throw new ArgumentException(
                "The DOM node belongs to a different backend.",
                nameof(node));
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
#endif
