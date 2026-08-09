#if !BROWSER
using System.Text;

namespace Sia_Examples.Console;

internal sealed class ConsoleLine
{
    private readonly List<Span> _spans = [];

    public HashSet<ConsoleDomNode> Nodes { get; } = [];

    public int Length => _spans.Sum(static span => span.Text.Length);

    public void Append(string text, ConsoleStyle style, ConsoleDomNode? node = null)
    {
        if (text.Length == 0) {
            return;
        }
        _spans.Add(new(text, style));
        if (node is not null) {
            Nodes.Add(node);
        }
    }

    public string Render(int width)
    {
        var output = new StringBuilder(width + 32);
        var visible = 0;
        ConsoleStyle? currentStyle = null;
        foreach (var span in _spans) {
            if (visible >= width) {
                break;
            }
            var count = Math.Min(span.Text.Length, width - visible);
            if (currentStyle != span.Style) {
                output.Append("\e[0m").Append(span.Style.EscapeSequence());
                currentStyle = span.Style;
            }
            output.Append(span.Text.AsSpan(0, count));
            visible += count;
        }
        output.Append("\e[0m");
        if (visible < width) {
            output.Append(' ', width - visible);
        }
        return output.ToString();
    }

    private readonly record struct Span(string Text, ConsoleStyle Style);
}
#endif
