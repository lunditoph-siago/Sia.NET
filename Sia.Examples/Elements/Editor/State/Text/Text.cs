using System.Collections;

namespace Sia_Examples.Editor;

public abstract class Text : IEquatable<Text>, IEnumerable<string>
{
    public static Text Empty { get; } = new TextLeaf([""], 0);

    public abstract int Length { get; }

    public abstract int Lines { get; }

    public Line LineAt(int position)
    {
        if (position < 0 || position > Length) {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
        return LineInner(position, isLine: false, line: 1, offset: 0);
    }

    public Line Line(int number)
    {
        if (number < 1 || number > Lines) {
            throw new ArgumentOutOfRangeException(nameof(number));
        }
        return LineInner(number, isLine: true, line: 1, offset: 0);
    }

    public Text Replace(int from, int to, Text text)
    {
        (from, to) = TextMath.Clip(Length, from, to);
        var parts = new List<Text>();
        Decompose(0, from, parts, TextOpen.To);
        if (text.Length > 0) {
            text.Decompose(0, text.Length, parts, TextOpen.From | TextOpen.To);
        }
        Decompose(to, Length, parts, TextOpen.From);
        return TextNode.From(parts, Length - (to - from) + text.Length);
    }

    public Text Append(Text other) => Replace(Length, Length, other);

    public Text Slice(int from, int to = int.MaxValue)
    {
        (from, to) = TextMath.Clip(Length, from, Math.Min(to, Length));
        var parts = new List<Text>();
        Decompose(from, to, parts, TextOpen.None);
        return TextNode.From(parts, to - from);
    }

    public abstract string SliceString(
        int from,
        int to = int.MaxValue,
        string lineSeparator = "\n");

    public string SliceDoc(int from = 0, int to = int.MaxValue)
        => SliceString(from, to, "\n");

    public override string ToString() => SliceString(0);

    public bool Equals(Text? other)
    {
        if (other is null) {
            return false;
        }
        if (ReferenceEquals(this, other)) {
            return true;
        }
        return other.Length == Length
            && other.Lines == Lines
            && SliceDoc() == other.SliceDoc();
    }

    public override bool Equals(object? obj) => obj is Text text && Equals(text);

    public override int GetHashCode() => HashCode.Combine(Length, Lines);

    public static Text Of(string[] lines)
    {
        if (lines.Length == 0) {
            throw new ArgumentException(
                "A document must have at least one line",
                nameof(lines));
        }
        if (lines.Length == 1 && lines[0].Length == 0) {
            return Empty;
        }
        return lines.Length <= TextConstants.Branch
            ? new TextLeaf(lines)
            : TextNode.From([.. TextLeaf.Split(lines, [])]);
    }

    public static Text OfString(string text)
        => Of(text.Replace("\r\n", "\n").Split('\n'));

    internal abstract Line LineInner(int target, bool isLine, int line, int offset);

    internal abstract void Flatten(List<string> target);

    internal abstract void Decompose(
        int from,
        int to,
        List<Text> target,
        TextOpen open);

    IEnumerator<string> IEnumerable<string>.GetEnumerator() => new TextCursor(this);

    IEnumerator IEnumerable.GetEnumerator() => new TextCursor(this);
}
