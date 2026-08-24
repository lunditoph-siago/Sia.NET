using System.Text;

namespace Sia_Examples.Editor;

internal sealed class TextLeaf : Text
{
    private readonly int _length;

    public TextLeaf(string[] lines, int length = -1)
    {
        LineContent = lines;
        _length = length >= 0 ? length : TextMath.Length(lines);
    }

    public override int Length => _length;

    public override int Lines => LineContent.Length;

    internal string[] LineContent { get; }

    internal override Line LineInner(int target, bool isLine, int line, int offset)
    {
        for (var index = 0; ; index++) {
            var text = LineContent[index];
            var end = offset + text.Length;
            if ((isLine ? line : end) >= target) {
                return new(offset, end, line, text);
            }
            offset = end + 1;
            line++;
        }
    }

    internal override void Decompose(
        int from,
        int to,
        List<Text> target,
        TextOpen open)
    {
        var text = from <= 0 && to >= Length
            ? this
            : new TextLeaf(
                Slice(LineContent, from, to),
                Math.Min(to, Length) - Math.Max(0, from));
        if ((open & TextOpen.From) == 0) {
            target.Add(text);
            return;
        }

        var previous = (TextLeaf)target[^1];
        target.RemoveAt(target.Count - 1);
        var joined = Append(text.LineContent, [.. previous.LineContent], 0, text.Length);
        if (joined.Length <= TextConstants.Branch) {
            target.Add(new TextLeaf(joined, previous.Length + text.Length));
            return;
        }

        var middle = joined.Length >> 1;
        target.Add(new TextLeaf(joined[..middle]));
        target.Add(new TextLeaf(joined[middle..]));
    }

    public override string SliceString(
        int from,
        int to = int.MaxValue,
        string lineSeparator = "\n")
    {
        (from, to) = TextMath.Clip(Length, from, to);
        var builder = new StringBuilder();
        for (int index = 0, position = 0;
            position <= to && index < LineContent.Length;
            index++) {
            var line = LineContent[index];
            var end = position + line.Length;
            if (position > from && index > 0) {
                builder.Append(lineSeparator);
            }
            if (from < end && to > position) {
                var start = Math.Max(0, from - position);
                builder.Append(
                    line.AsSpan(start, Math.Min(line.Length, to - position) - start));
            }
            position = end + 1;
        }
        return builder.ToString();
    }

    internal override void Flatten(List<string> target)
    {
        foreach (var line in LineContent) {
            target.Add(line);
        }
    }

    internal static Text[] Split(string[] text, List<Text> target)
    {
        var part = new List<string>();
        var length = -1;
        foreach (var line in text) {
            part.Add(line);
            length += line.Length + 1;
            if (part.Count != TextConstants.Branch) {
                continue;
            }
            target.Add(new TextLeaf([.. part], length));
            part.Clear();
            length = -1;
        }
        if (length > -1) {
            target.Add(new TextLeaf([.. part], length));
        }
        return [.. target];
    }

    internal static string[] Slice(string[] text, int from, int to)
        => Append(text, [""], from, to);

    internal static string[] Append(string[] text, string[] target, int from, int to)
    {
        var first = true;
        for (int index = 0, position = 0;
            index < text.Length && position <= to;
            index++) {
            var line = text[index];
            var end = position + line.Length;
            if (end >= from) {
                if (end > to) {
                    line = line[..(to - position)];
                }
                if (position < from) {
                    line = line[(from - position)..];
                }
                if (first) {
                    target[^1] += line;
                    first = false;
                } else {
                    target = [.. target, line];
                }
            }
            position = end + 1;
        }
        return target;
    }

}
