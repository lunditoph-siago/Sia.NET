#if !BROWSER
using Sia_Examples.Dom;

namespace Sia_Examples.Console;

internal sealed class ConsoleDomNode(string tagName, Action changed) : IDomNode
{
    private readonly Action _changed = changed;

    public string TagName { get; } = tagName;

    public string Text { get; set; } = string.Empty;

    public ConsoleDomNode? Parent { get; set; }

    public List<ConsoleDomNode> Children { get; } = [];

    public Dictionary<string, string> Attributes { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Classes { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Listeners { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsText => TagName == "#text";

    public string? Id => Attributes.GetValueOrDefault("id");

    public bool IsVisible
        => !Classes.Contains("hidden") && (Parent?.IsVisible ?? true);

    public string TextContent()
    {
        if (Children.Count == 0) {
            return Text;
        }
        return string.Concat(Children.Select(static child => child.TextContent()));
    }

    public bool HasClass(string className) => Classes.Contains(className);

    public ConsoleDomNode? FirstWithClass(string className)
        => DescendantsAndSelf().FirstOrDefault(node => node.HasClass(className));

    public IEnumerable<ConsoleDomNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children) {
            foreach (var descendant in child.DescendantsAndSelf()) {
                yield return descendant;
            }
        }
    }

    public bool Contains(ConsoleDomNode node)
    {
        for (var current = node; current is not null; current = current.Parent) {
            if (ReferenceEquals(current, this)) {
                return true;
            }
        }
        return false;
    }

    public void Changed() => _changed();

    public void Dispose()
    {
    }
}
#endif
