namespace Sia_Examples.Editor;

internal sealed class EditorLinesCache
{
    private Text? _document;
    private RangeSet<Decoration>? _decorations;
    private EditorLineIdentities _identities;

    public Patch Update(in EditorLinesProps props)
    {
        Patch patch;
        if (_document is null
            || !ReferenceEquals(_decorations, props.Decorations)
            || props.Update is not { } update
            || !ReferenceEquals(update.BeforeDocument, _document)
            || !ReferenceEquals(_identities.Values, props.Identities.Values)) {
            patch = Rebuild(props);
        } else {
            patch = UpdateChangedLines(props, update.Changes);
        }

        _document = props.Document;
        _decorations = props.Decorations;
        _identities = props.Identities;
        return patch;
    }

    private Patch Rebuild(in EditorLinesProps props)
    {
        var upserts = new (int Key, EditorLineItem Value)[props.Document.Lines];
        var currentKeys = new HashSet<int>();
        for (var index = 0; index < props.Document.Lines; index++) {
            upserts[index] = BuildLine(props, index);
            currentKeys.Add(upserts[index].Key);
        }

        if (_document is null) {
            return new(upserts, []);
        }
        var removals = _identities.Values
            .Where(key => !currentKeys.Contains(key))
            .ToArray();
        return new(upserts, removals);
    }

    private static Patch UpdateChangedLines(
        in EditorLinesProps props,
        ChangeSet changes)
    {
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
