namespace Sia_Examples.Editor;

internal sealed class EditorLinesCache
{
    private Text? _document;
    private RangeSet<Decoration>? _decorations;
    private EditorLineIdentities _identities;
    private EditorViewport _viewport;
    private HashSet<int> _mountedKeys = [];

    public Patch Update(in EditorLinesProps props)
    {
        Patch patch;
        if (_document is null
            || !ReferenceEquals(_decorations, props.Decorations)
            || props.Update is not { } update
            || !ReferenceEquals(update.BeforeDocument, _document)
            || !ReferenceEquals(_identities.Values, props.Identities.Values)
            || !_viewport.Equals(props.Viewport)) {
            patch = Rebuild(props);
        } else {
            patch = UpdateChangedLines(props, update.Changes);
        }

        _document = props.Document;
        _decorations = props.Decorations;
        _identities = props.Identities;
        _viewport = props.Viewport;
        return patch;
    }

    private Patch Rebuild(in EditorLinesProps props)
    {
        var (from, to) = LineIndexRange(props.Document, props.Viewport);
        var length = to - from;
        var upserts = new (int Key, EditorLineItem Value)[length];
        var currentKeys = new HashSet<int>(length);
        for (var index = from; index < to; index++) {
            var built = BuildLine(props, index);
            upserts[index - from] = built;
            currentKeys.Add(built.Key);
        }

        var removals = _mountedKeys.Count == 0
            ? []
            : _mountedKeys.Where(key => !currentKeys.Contains(key)).ToArray();
        _mountedKeys = currentKeys;
        return new(upserts, removals);
    }

    private static Patch UpdateChangedLines(
        in EditorLinesProps props,
        ChangeSet changes)
    {
        var (viewportFrom, viewportTo) = LineIndexRange(props.Document, props.Viewport);
        var changedLines = new HashSet<int>();
        var document = props.Document;
        changes.IterateChangedRanges((_, _, from, to) => {
            var start = document.LineAt(from).Number - 1;
            var endLine = document.LineAt(to);
            var end = endLine.Number - 1;
            if (to > from && to == endLine.From) {
                end--;
            }
            end = Math.Max(start, end);
            start = Math.Max(start, viewportFrom);
            end = Math.Min(end, viewportTo - 1);
            for (var index = start; index <= end; index++) {
                changedLines.Add(index);
            }
        }, individual: true);

        var upserts = new (int Key, EditorLineItem Value)[changedLines.Count];
        var target = 0;
        foreach (var index in changedLines.Order()) {
            upserts[target++] = BuildLine(props, index);
        }
        return new(upserts, []);
    }

    private static (int From, int To) LineIndexRange(Text document, EditorViewport viewport)
    {
        if (document.Lines == 0) {
            return (0, 0);
        }
        var clamped = viewport.Clamp(document.Length);
        var from = document.LineAt(clamped.From).Number - 1;
        var to = document.LineAt(clamped.To).Number;
        return (from, to);
    }

    private static (int Key, EditorLineItem Value) BuildLine(
        in EditorLinesProps props,
        int index)
    {
        var line = props.Document.Line(index + 1);
        var marks = props.Decorations
            .Between(line.From, line.To)
            .Where(static range => range.Value.Kind == DecorationKind.Mark)
            .ToArray();
        var styledRuns = marks.Length == 0
            ? null
            : LineDecorator.Segment(line.Text, line.From, marks);
        var view = new EditorLineView(
            props.Identities.Values[index],
            index,
            line.Text,
            styledRuns,
            DecorationSet.LineClass(props.Decorations, line.From, line.To));
        return (view.Identity, new(props.View, view));
    }

    internal readonly record struct Patch(
        (int Key, EditorLineItem Value)[] Upserts,
        int[] Removals);
}
