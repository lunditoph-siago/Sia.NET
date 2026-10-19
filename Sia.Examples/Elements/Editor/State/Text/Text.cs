using System.Collections;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace Sia_Examples.Editor;

public sealed class Text : IEquatable<Text>, IEnumerable<string>
{
    private readonly SourceText _source;
    private readonly Dictionary<int, Line> _lines = [];
    private string? _content;
    private Text? _layout;
    private string? _newline;

    private Text(SourceText source, string? content = null) { _source = source; _content = content; }

    public static Text Empty { get; } = new(SourceText.From(""), "");
    public int Length => _source.Length;
    public int Lines => _source.Lines.Count;

    public Line LineAt(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Length);
        return Line(LineIndex(position) + 1);
    }

    public Line Line(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, Lines);
        lock (_lines) {
            if (_lines.TryGetValue(number, out var cached)) return cached;
            var line = _source.Lines[number - 1];
            var result = new Line(line.Start, line.End, number, _source.ToString(line.Span), line.EndIncludingLineBreak - line.End);
            if (_lines.Count >= 256) _lines.Clear();
            _lines.Add(number, result);
            return result;
        }
    }

    public Text Replace(int from, int to, Text text)
    {
        (from, to) = Clip(from, to);
        if (from == to && text.Length == 0) return this;
        var changed = new Text(_source.WithChanges(new TextChange(TextSpan.FromBounds(from, to), text.SliceDoc())));
        if (_layout is not { } layout || !CanProjectEdit(from, to, text)) return changed;
        if (ReferenceEquals(layout, this) && ReferenceEquals(text.Layout, text)) changed._layout = changed;
        else {
            var projected = new Text(layout._source.WithChanges(new TextChange(
                TextSpan.FromBounds(ToLayoutPosition(from), ToLayoutPosition(to)), text.Layout.SliceDoc())));
            projected._layout = projected;
            changed._layout = projected;
        }
        return changed;
    }

    private bool CanProjectEdit(int from, int to, Text inserted)
    {
        bool InsidePair(int position) => position > 0 && position < Length && _source[position - 1] == '\r' && _source[position] == '\n';
        if (InsidePair(from) || InsidePair(to)) return false;
        var right = to < Length ? _source[to] : '\0';
        var first = inserted.Length > 0 ? inserted._source[0] : right;
        return !(from > 0 && _source[from - 1] == '\r' && first == '\n')
            && !(inserted.Length > 0 && inserted._source[inserted.Length - 1] == '\r' && right == '\n');
    }

    public Text Append(Text other) => Replace(Length, Length, other);
    public Text Slice(int from, int to = int.MaxValue)
    {
        (from, to) = Clip(from, to);
        return from == 0 && to == Length ? this : new(_source.GetSubText(TextSpan.FromBounds(from, to)));
    }

    public string SliceDoc(int from = 0, int to = int.MaxValue)
    {
        (from, to) = Clip(from, to);
        return from == 0 && to == Length ? _content ??= _source.ToString() : _source.ToString(TextSpan.FromBounds(from, to));
    }

    public string SliceString(int from, int to = int.MaxValue, string? lineSeparator = null)
    {
        var text = Slice(from, to);
        return lineSeparator is null ? text.SliceDoc() : string.Join(lineSeparator, text);
    }

    public override string ToString() => SliceDoc();
    public bool Equals(Text? other) => ReferenceEquals(this, other) || other is not null && _source.ContentEquals(other._source);
    public override bool Equals(object? obj) => obj is Text other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Length, Lines);

    public static Text Of(string[] lines)
        => lines.Length == 0 ? throw new ArgumentException("A document must have at least one line.", nameof(lines)) : OfString(string.Join('\n', lines));
    public static Text OfString(string text) => text.Length == 0 ? Empty : new(SourceText.From(text), text);

    internal Text Layout => _layout ??= CreateLayout();
    internal IReadOnlyList<TextChange> ChangesFrom(Text before) => _source.GetTextChanges(before._source);

    private Text CreateLayout()
    {
        var source = SliceDoc();
        if (source.AsSpan().IndexOfAny("\r\u0085\u2028\u2029") < 0) return this;
        var builder = new StringBuilder(source.Length);
        foreach (var line in _source.Lines) {
            builder.Append(source, line.Start, line.Span.Length);
            if (line.EndIncludingLineBreak > line.End) builder.Append('\n');
        }
        return OfString(builder.ToString());
    }

    internal int ToLayoutPosition(int position)
    {
        position = Math.Clamp(position, 0, Length);
        if (ReferenceEquals(Layout, this)) return position;
        var index = LineIndex(position);
        var line = _source.Lines[index];
        return Layout._source.Lines[index].Start + Math.Min(position - line.Start, line.Span.Length);
    }

    internal int FromLayoutPosition(int position)
    {
        if (ReferenceEquals(Layout, this)) return Math.Clamp(position, 0, Length);
        position = Math.Clamp(position, 0, Layout.Length);
        var index = Layout.LineIndex(position);
        var line = Layout._source.Lines[index];
        return _source.Lines[index].Start + Math.Min(position - line.Start, line.Span.Length);
    }

    internal int NormalizePosition(int position, bool after)
    {
        position = Math.Clamp(position, 0, Length);
        var line = _source.Lines[LineIndex(position)];
        return position > line.End ? after ? line.EndIncludingLineBreak : line.End : position;
    }

    internal string NewlineAt(int position)
    {
        var line = _source.Lines[LineIndex(Math.Clamp(position, 0, Length))];
        return line.EndIncludingLineBreak > line.End ? BreakText(line) : PreferredNewline;
    }

    internal string PreferredNewline => _newline ??= DetectNewline();

    private string DetectNewline()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in _source.Lines) {
            if (line.EndIncludingLineBreak == line.End) continue;
            var value = BreakText(line);
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }
        return counts.Count == 0 ? "\n" : counts.MaxBy(static entry => entry.Value).Key;
    }

    private string BreakText(TextLine line) => _source[line.End] switch {
        '\r' => line.EndIncludingLineBreak - line.End == 2 ? "\r\n" : "\r",
        '\u0085' => "\u0085",
        '\u2028' => "\u2028",
        '\u2029' => "\u2029",
        _ => "\n"
    };

    private int LineIndex(int position)
    {
        var low = 0;
        var high = Lines - 1;
        while (low < high) {
            var middle = low + (high - low + 1) / 2;
            if (_source.Lines[middle].Start <= position) low = middle;
            else high = middle - 1;
        }
        return low;
    }

    private (int From, int To) Clip(int from, int to)
    {
        from = Math.Clamp(from, 0, Length);
        return (from, Math.Clamp(to, from, Length));
    }

    public IEnumerator<string> GetEnumerator()
    {
        for (var index = 1; index <= Lines; index++) yield return Line(index).Text;
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
