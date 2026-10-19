namespace Sia_Examples.Editor;

internal sealed class EditorLinesCache
{
    private Text? _document;
    private RangeSet<Decoration>? _decorations;
    private EditorLineIdentities _identities;
    private EditorLineView[] _lines = [];

    public EditorLinesView Update(in EditorLinesProps props)
    {
        var (from, to) = LineIndexRange(props.Document, props.Viewport);
        var sameDocument = ReferenceEquals(_document, props.Document);
        var canReuse = _document is not null
            && ReferenceEquals(_decorations, props.Decorations)
            && ReferenceEquals(_identities.Values, props.Identities.Values)
            && (sameDocument || props.Update is { } update
                && ReferenceEquals(update.BeforeDocument, _document));
        var changed = canReuse && !sameDocument && props.Update is { } lineUpdate
            ? ChangedLines(props.Document, lineUpdate.Changes, from, to)
            : null;
        var oldFrom = _lines.Length == 0 ? 0 : _lines[0].Index;
        var needsBuild = !canReuse || from < oldFrom || to > oldFrom + _lines.Length || changed is { Count: > 0 };
        var decorations = needsBuild ? WindowDecorations(props, from, to) : [];
        var ordered = OrderedDecorations(decorations);
        var cursor = 0;
        var lines = new EditorLineView[to - from];
        for (var index = from; index < to; index++) {
            var oldIndex = index - oldFrom;
            lines[index - from] = canReuse && oldIndex >= 0 && oldIndex < _lines.Length
                && changed?.Contains(index) != true
                ? _lines[oldIndex]
                : BuildLine(props, index, decorations, ordered, ref cursor);
        }

        _document = props.Document;
        _decorations = props.Decorations;
        _identities = props.Identities;
        _lines = lines;
        return new(lines);
    }

    private static HashSet<int> ChangedLines(Text document, ChangeSet changes, int from, int to)
    {
        var changed = new HashSet<int>();
        changes.IterateChangedRanges((_, _, start, end) => {
            var first = document.LineAt(start).Number - 1;
            var lastLine = document.LineAt(end);
            var last = lastLine.Number - 1;
            if (end > start && end == lastLine.From) last--;
            last = Math.Max(first, last);
            for (var index = Math.Max(first, from); index <= Math.Min(last, to - 1); index++) {
                changed.Add(index);
            }
        }, individual: true);
        return changed;
    }

    private static (int From, int To) LineIndexRange(Text document, EditorViewport viewport)
    {
        if (document.Lines == 0) return (0, 0);
        var clamped = viewport.Clamp(document.Length);
        return (document.LineAt(clamped.From).Number - 1, document.LineAt(clamped.To).Number);
    }

    private static Range<Decoration>[] WindowDecorations(in EditorLinesProps props, int from, int to)
    {
        if (from >= to || props.Decorations.IsEmpty) return [];
        return props.Decorations.Between(
            props.Document.Line(from + 1).From, props.Document.Line(to).EndIncludingLineBreak).ToArray();
    }

    private static bool OrderedDecorations(Range<Decoration>[] decorations)
    {
        var previousTo = int.MinValue;
        foreach (var range in decorations) {
            if (range.From < previousTo || range.To < range.From) return false;
            previousTo = range.To;
        }
        return true;
    }

    private static EditorLineView BuildLine(
        in EditorLinesProps props, int index, Range<Decoration>[] decorations, bool ordered, ref int cursor)
    {
        var line = props.Document.Line(index + 1);
        var ranges = decorations.AsSpan();
        if (ordered) {
            while (cursor < decorations.Length && decorations[cursor].To < line.From) cursor++;
            var end = cursor;
            while (end < decorations.Length && decorations[end].From <= line.EndIncludingLineBreak) end++;
            ranges = ranges[cursor..end];
        }
        List<Range<Decoration>>? marks = null;
        string? className = null;
        foreach (var range in ranges) {
            var end = range.Value.Kind == DecorationKind.Line ? line.EndIncludingLineBreak : line.To;
            var overlaps = range.From == range.To
                ? range.From >= line.From && range.From <= end
                : range.From < end && range.To > line.From;
            if (!overlaps) continue;
            if (range.Value.Kind == DecorationKind.Mark) {
                (marks ??= []).Add(range);
            }
            else if (range.Value.Kind == DecorationKind.Line) {
                className = className is null ? range.Value.Class : $"{className} {range.Value.Class}";
            }
        }
        var styledRuns = marks is null ? null : LineDecorator.Segment(line.Text, line.From, marks);
        return new(props.Identities.Values[index], index, line.Text, styledRuns, className);
    }
}
