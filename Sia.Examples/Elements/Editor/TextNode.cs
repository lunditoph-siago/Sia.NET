using System.Text;

namespace Sia_Examples.Editor;

internal sealed class TextNode : Text
{
    private readonly Text[] _children;
    private readonly int _length;
    private readonly int _lines;

    public TextNode(Text[] children, int length)
    {
        _children = children;
        _length = length;
        _lines = children.Sum(static child => child.Lines);
    }

    public override int Length => _length;

    public override int Lines => _lines;

    internal override Line LineInner(int target, bool isLine, int line, int offset)
    {
        foreach (var child in _children) {
            var end = offset + child.Length;
            var endLine = line + child.Lines - 1;
            if ((isLine ? endLine : end) >= target) {
                return child.LineInner(target, isLine, line, offset);
            }
            offset = end + 1;
            line = endLine + 1;
        }
        throw new ArgumentOutOfRangeException(nameof(target));
    }

    internal override void Decompose(
        int from,
        int to,
        List<Text> target,
        TextOpen open)
    {
        var position = 0;
        foreach (var child in _children) {
            if (position > to) {
                break;
            }
            var end = position + child.Length;
            if (from <= end && to >= position) {
                var childOpen = open & (
                    (position <= from ? TextOpen.From : TextOpen.None)
                    | (end >= to ? TextOpen.To : TextOpen.None));
                if (position >= from && end <= to && childOpen == TextOpen.None) {
                    target.Add(child);
                } else {
                    child.Decompose(from - position, to - position, target, childOpen);
                }
            }
            position = end + 1;
        }
    }

    public override string SliceString(
        int from,
        int to = int.MaxValue,
        string lineSeparator = "\n")
    {
        (from, to) = TextMath.Clip(Length, from, to);
        var builder = new StringBuilder();
        var position = 0;
        for (var index = 0; index < _children.Length && position <= to; index++) {
            var child = _children[index];
            var end = position + child.Length;
            if (position > from && index > 0) {
                builder.Append(lineSeparator);
            }
            if (from < end && to > position) {
                builder.Append(child.SliceString(
                    from - position,
                    to - position,
                    lineSeparator));
            }
            position = end + 1;
        }
        return builder.ToString();
    }

    internal override void Flatten(List<string> target)
    {
        foreach (var child in _children) {
            child.Flatten(target);
        }
    }

    internal static Text From(List<Text> children, int length = -1)
    {
        if (length < 0) {
            length = children.Sum(static child => child.Length + 1) - 1;
        }
        var lines = children.Sum(static child => child.Lines);
        if (lines < TextConstants.Branch) {
            var flat = new List<string>();
            foreach (var child in children) {
                child.Flatten(flat);
            }
            return new TextLeaf([.. flat], length);
        }

        var chunk = Math.Max(TextConstants.Branch, lines >> TextConstants.BranchShift);
        var maximumChunk = chunk << 1;
        var minimumChunk = chunk >> 1;
        var chunked = new List<Text>();
        var currentLines = 0;
        var currentLength = -1;
        var currentChunk = new List<Text>();

        void Flush()
        {
            if (currentLines == 0) {
                return;
            }
            chunked.Add(currentChunk.Count == 1
                ? currentChunk[0]
                : From(currentChunk, currentLength));
            currentLength = -1;
            currentLines = 0;
            currentChunk.Clear();
        }

        void Add(Text child)
        {
            if (child.Lines > maximumChunk && child is TextNode node) {
                foreach (var nested in node._children) {
                    Add(nested);
                }
                return;
            }
            if (child.Lines > minimumChunk
                && (currentLines > minimumChunk || currentLines == 0)) {
                Flush();
                chunked.Add(child);
                return;
            }
            if (child is TextLeaf leaf
                && currentLines > 0
                && currentChunk[^1] is TextLeaf last
                && leaf.Lines + last.Lines <= TextConstants.Branch) {
                currentLines += leaf.Lines;
                currentLength += leaf.Length + 1;
                currentChunk[^1] = new TextLeaf(
                    [.. last.LineContent, .. leaf.LineContent],
                    last.Length + 1 + leaf.Length);
                return;
            }
            if (currentLines + child.Lines > chunk) {
                Flush();
            }
            currentLines += child.Lines;
            currentLength += child.Length + 1;
            currentChunk.Add(child);
        }

        foreach (var child in children) {
            Add(child);
        }
        Flush();
        return chunked.Count == 1
            ? chunked[0]
            : new TextNode([.. chunked], length);
    }
}
