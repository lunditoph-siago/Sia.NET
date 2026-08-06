using System.Collections;

namespace Sia_Examples.Editor;

public readonly record struct Line(int From, int To, int Number, string Text)
{
    public int Length => To - From;
}

public abstract class Text : IEquatable<Text>, IEnumerable<string>
{
    public abstract int Length { get; }
    public abstract int Lines { get; }
    internal abstract IReadOnlyList<Text>? Children { get; }

    public Line LineAt(int pos)
    {
        if (pos < 0 || pos > Length)
            throw new ArgumentOutOfRangeException(nameof(pos));
        return LineInner(pos, false, 1, 0);
    }

    public Line Line(int n)
    {
        if (n < 1 || n > Lines)
            throw new ArgumentOutOfRangeException(nameof(n));
        return LineInner(n, true, 1, 0);
    }

    internal abstract Line LineInner(int target, bool isLine, int line, int offset);

    public Text Replace(int from, int to, Text text)
    {
        (from, to) = TextMath.Clip(Length, from, to);
        var parts = new List<Text>();
        Decompose(0, from, parts, TextOpen.To);
        if (text.Length > 0)
            text.Decompose(0, text.Length, parts, TextOpen.From | TextOpen.To);
        Decompose(to, Length, parts, TextOpen.From);
        return TextNode.From(parts, Length - (to - from) + text.Length);
    }

    public Text Append(Text other) => Replace(Length, Length, other);

    public Text Slice(int from, int to = int.MaxValue)
    {
        (from, to) = TextMath.Clip(Length, from, Math.Min(to, Length));
        var parts = new List<Text>();
        Decompose(from, to, parts, 0);
        return TextNode.From(parts, to - from);
    }

    public abstract string SliceString(int from, int to = int.MaxValue, string lineSep = "\n");

    public string SliceDoc(int from = 0, int to = int.MaxValue) => SliceString(from, to, "\n");

    internal abstract void Flatten(List<string> target);

    public override string ToString() => SliceString(0);

    public string[] ToJSON() { var l = new List<string>(); Flatten(l); return [.. l]; }

    internal abstract int ScanIdentical(Text other, int dir);

    public bool Equals(Text? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (other.Length != Length || other.Lines != Lines) return false;
        return SliceDoc() == other.SliceDoc();
    }

    public override bool Equals(object? obj) => obj is Text t && Equals(t);
    public override int GetHashCode() => HashCode.Combine(Length, Lines);

    internal abstract void Decompose(int from, int to, List<Text> target, TextOpen open);

    IEnumerator<string> IEnumerable<string>.GetEnumerator() => new TextCursor(this);
    IEnumerator IEnumerable.GetEnumerator() => new TextCursor(this);

    public static Text Of(string[] lines)
    {
        if (lines.Length == 0) throw new ArgumentException("A document must have at least one line");
        if (lines.Length == 1 && lines[0].Length == 0) return Empty;
        return lines.Length <= TextConst.Branch
            ? new TextLeaf(lines)
            : TextNode.From([.. TextLeaf.Split(lines, [])]);
    }

    public static Text OfString(string text)
    {
        var raw = text.Replace("\r\n", "\n");
        return Of(raw.Split('\n'));
    }

    public static readonly Text Empty = new TextLeaf([""], 0);
}

internal enum TextOpen { From = 1, To = 2 }

internal static class TextConst { public const int BranchShift = 5; public const int Branch = 32; }

internal static class TextMath
{
    public static (int, int) Clip(int length, int from, int to)
    {
        from = Math.Max(0, Math.Min(length, from));
        return (from, Math.Max(from, Math.Min(length, to)));
    }

    public static int TextLen(string[] lines)
    {
        var n = -1;
        foreach (var l in lines) n += l.Length + 1;
        return n;
    }
}

internal sealed class TextLeaf : Text
{
    internal readonly string[] Lines_;
    private readonly int _len;

    public TextLeaf(string[] lines, int len = -1)
    {
        Lines_ = lines;
        _len = len >= 0 ? len : TextMath.TextLen(lines);
    }

    public override int Length => _len;
    public override int Lines => Lines_.Length;
    internal override IReadOnlyList<Text>? Children => null;

    internal override Line LineInner(int target, bool isLine, int line, int offset)
    {
        for (var i = 0; ; i++)
        {
            var s = Lines_[i];
            var end = offset + s.Length;
            if ((isLine ? line : end) >= target) return new Line(offset, end, line, s);
            offset = end + 1; line++;
        }
    }

    internal override void Decompose(int from, int to, List<Text> target, TextOpen open)
    {
        TextLeaf text = from <= 0 && to >= Length ? this
            : new TextLeaf(Slice(Lines_, from, to), Math.Min(to, Length) - Math.Max(0, from));
        if ((open & TextOpen.From) != 0)
        {
            var prev = (TextLeaf)target[^1]; target.RemoveAt(target.Count - 1);

            var joined = Append(text.Lines_, [.. prev.Lines_], 0, text.Length);
            if (joined.Length <= TextConst.Branch)
                target.Add(new TextLeaf(joined, prev.Length + text.Length));
            else { var mid = joined.Length >> 1; target.Add(new TextLeaf(joined[..mid])); target.Add(new TextLeaf(joined[mid..])); }
        }
        else target.Add(text);
    }

    public override string SliceString(int from, int to = int.MaxValue, string lineSep = "\n")
    {
        (from, to) = TextMath.Clip(Length, from, to);
        var sb = new System.Text.StringBuilder();
        for (int pos = 0, i = 0; pos <= to && i < Lines_.Length; i++)
        {
            var line = Lines_[i]; var end = pos + line.Length;
            if (pos > from && i > 0) sb.Append(lineSep);
            if (from < end && to > pos)
                sb.Append(line.AsSpan(Math.Max(0, from - pos),
                    Math.Min(line.Length, to - pos) - Math.Max(0, from - pos)));
            pos = end + 1;
        }
        return sb.ToString();
    }

    internal override void Flatten(List<string> target) { foreach (var l in Lines_) target.Add(l); }
    internal override int ScanIdentical(Text other, int dir) => 0;

    internal static Text[] Split(string[] text, List<Text> target)
    {
        var part = new List<string>(); var len = -1;
        foreach (var line in text)
        {
            part.Add(line); len += line.Length + 1;
            if (part.Count == TextConst.Branch)
            { target.Add(new TextLeaf([.. part], len)); part.Clear(); len = -1; }
        }
        if (len > -1) target.Add(new TextLeaf([.. part], len));
        return [.. target];
    }

    internal static string[] Slice(string[] text, int from, int to) => Append(text, [""], from, to);
    internal static string[] Append(string[] text, string[] target, int from, int to)
    {
        var first = true;
        for (int pos = 0, i = 0; i < text.Length && pos <= to; i++)
        {
            var line = text[i]; var end = pos + line.Length;
            if (end >= from)
            {
                if (end > to) line = line[..(to - pos)];
                if (pos < from) line = line[(from - pos)..];
                if (first) { target[^1] += line; first = false; }
                else target = [.. target, line];
            }
            pos = end + 1;
        }
        return target;
    }
}

internal sealed class TextNode : Text
{
    private readonly Text[] _children;
    private readonly int _lines, _len;

    public TextNode(Text[] children, int len)
    {
        _children = children; _len = len;
        _lines = children.Sum(c => c.Lines);
    }

    public override int Length => _len;
    public override int Lines => _lines;
    internal override IReadOnlyList<Text>? Children => _children;

    internal override Line LineInner(int target, bool isLine, int line, int offset)
    {
        for (var i = 0; ; i++)
        {
            var c = _children[i]; int end = offset + c.Length, endLine = line + c.Lines - 1;
            if ((isLine ? endLine : end) >= target) return c.LineInner(target, isLine, line, offset);
            offset = end + 1; line = endLine + 1;
        }
    }

    internal override void Decompose(int from, int to, List<Text> target, TextOpen open)
    {
        for (int i = 0, pos = 0; pos <= to && i < _children.Length; i++)
        {
            var c = _children[i]; var end = pos + c.Length;
            if (from <= end && to >= pos)
            {
                var co = open & ((pos <= from ? TextOpen.From : 0) | (end >= to ? TextOpen.To : 0));
                if (pos >= from && end <= to && co == 0) target.Add(c);
                else c.Decompose(from - pos, to - pos, target, co);
            }
            pos = end + 1;
        }
    }

    public override string SliceString(int from, int to = int.MaxValue, string lineSep = "\n")
    {
        (from, to) = TextMath.Clip(Length, from, to);
        var sb = new System.Text.StringBuilder();
        for (int i = 0, pos = 0; i < _children.Length && pos <= to; i++)
        {
            var c = _children[i]; var end = pos + c.Length;
            if (pos > from && i > 0) sb.Append(lineSep);
            if (from < end && to > pos) sb.Append(c.SliceString(from - pos, to - pos, lineSep));
            pos = end + 1;
        }
        return sb.ToString();
    }

    internal override void Flatten(List<string> target) { foreach (var c in _children) c.Flatten(target); }
    internal override int ScanIdentical(Text other, int dir) => 0;

    internal static Text From(List<Text> children, int length = -1)
    {
        if (length < 0) length = children.Sum(c => c.Length + 1) - 1;
        var lines = children.Sum(c => c.Lines);
        if (lines < TextConst.Branch)
        {
            var flat = new List<string>();
            foreach (var c in children) c.Flatten(flat);
            return new TextLeaf([.. flat], length);
        }
        var chunk = Math.Max(TextConst.Branch, lines >> TextConst.BranchShift);
        int maxChunk = chunk << 1, minChunk = chunk >> 1;
        var chunked = new List<Text>();
        int curLines = 0, curLen = -1;
        var curChunk = new List<Text>();

        void Flush()
        {
            if (curLines == 0) return;
            chunked.Add(curChunk.Count == 1 ? curChunk[0] : From(curChunk, curLen));
            curLen = -1; curLines = 0; curChunk.Clear();
        }
        void Add(Text child)
        {
            if (child.Lines > maxChunk && child is TextNode n) { foreach (var c in n._children) Add(c); }
            else if (child.Lines > minChunk && (curLines > minChunk || curLines == 0)) { Flush(); chunked.Add(child); }
            else if (child is TextLeaf lf && curLines > 0 && curChunk[^1] is TextLeaf last &&
                     lf.Lines + last.Lines <= TextConst.Branch)
            { curLines += lf.Lines; curLen += lf.Length + 1; curChunk[^1] = new TextLeaf([.. last.Lines_, .. lf.Lines_], last.Length + 1 + lf.Length); }
            else { if (curLines + child.Lines > chunk) Flush(); curLines += child.Lines; curLen += child.Length + 1; curChunk.Add(child); }
        }
        foreach (var c in children) Add(c);
        Flush();
        return chunked.Count == 1 ? chunked[0] : new TextNode([.. chunked], length);
    }
}

internal sealed class TextCursor : IEnumerator<string>
{
    private readonly Text _text;
    private int _pos;
    public string Current { get; private set; } = "";
    object IEnumerator.Current => Current;

    public TextCursor(Text text) { _text = text; _pos = 0; }

    public bool MoveNext()
    {
        if (_pos >= _text.Length) return false;
        var line = _text.LineAt(_pos);
        Current = line.Text;
        _pos = line.To + 1;
        return true;
    }

    public void Reset() => throw new NotSupportedException();
    public void Dispose() { }
}
