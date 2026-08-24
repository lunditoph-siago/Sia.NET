namespace Sia_Examples.Editor;

public static class LineCommands
{
    public static bool InsertNewlineAndIndent(CommandTarget target)
    {
        var state = target.State;
        var (changes, selection) = ChangeHelpers.InsertPerRange(
            state.Selection.Ranges,
            range => "\n" + new string(
                ' ',
                state.Doc.LineAt(range.From).Text.TakeWhile(char.IsWhiteSpace).Count()));
        target.Apply(state.Apply(new() {
            Changes = changes,
            Selection = selection,
        }));
        return true;
    }

    public static bool InsertTab(CommandTarget target)
    {
        var state = target.State;
        if (state.Selection.Ranges.Any(static range => !range.Empty)) {
            return IndentMore(target);
        }

        var selection = state.Selection.Main;
        target.Apply(state.Apply(new() {
            Changes = [new(selection.From, selection.To, "\t")],
            Selection = EditorSelection.Single(selection.From + 1),
        }));
        return true;
    }

    public static bool IndentLess(CommandTarget target)
    {
        var changes = VisitSelectedLines(target.State, static line => {
            var spaces = 0;
            while (spaces < EditorState.TabSize
                && spaces < line.Text.Length
                && line.Text[spaces] == ' ') {
                spaces++;
            }
            return spaces == 0
                ? null
                : new ChangeSpec(line.From, line.From + spaces, string.Empty);
        });
        return ApplyChanges(target, changes);
    }

    private static bool IndentMore(CommandTarget target)
    {
        var indentation = new string(' ', EditorState.TabSize);
        var changes = VisitSelectedLines(
            target.State,
            line => new ChangeSpec(line.From, line.From, indentation));
        return ApplyChanges(target, changes);
    }

    private static List<ChangeSpec> VisitSelectedLines(
        EditorState state,
        Func<Line, ChangeSpec?> createChange)
    {
        var changes = new List<ChangeSpec>();
        var lastLineNumber = -1;
        foreach (var range in state.Selection.Ranges) {
            for (var position = range.From; position <= range.To;) {
                var line = state.Doc.LineAt(position);
                if (line.Number > lastLineNumber
                    && (range.Empty || range.To > line.From)
                    && createChange(line) is { } change) {
                    changes.Add(change);
                }
                lastLineNumber = Math.Max(lastLineNumber, line.Number);
                position = line.To + 1;
            }
        }
        return changes;
    }

    private static bool ApplyChanges(CommandTarget target, List<ChangeSpec> changes)
    {
        if (changes.Count == 0) {
            return false;
        }
        target.Apply(target.State.Apply(new() { Changes = [.. changes] }));
        return true;
    }
}
