namespace Sia_Examples.Editor;

public static class LineCommands
{
    public static bool InsertNewlineAndIndent(CommandTarget target)
    {
        var state = target.State;
        var (changes, selection) = ChangeHelpers.InsertPerRange(
            state.Selection.Ranges,
            range => {
                var line = state.Doc.LineAt(range.From);
                var length = 0;
                while (length < range.From - line.From && line.Text[length] is ' ' or '\t') length++;
                return state.Doc.NewlineAt(range.From) + line.Text[..length];
            });
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

        var (changes, selection) = ChangeHelpers.InsertPerRange(
            state.Selection.Ranges,
            static _ => new string(' ', EditorState.TabSize));
        target.Apply(state.Apply(new() {
            Changes = changes,
            Selection = selection,
        }));
        return true;
    }

    public static bool IndentLess(CommandTarget target)
    {
        var changes = VisitSelectedLines(target.State, static line => {
            if (line.Text.StartsWith('\t')) return new ChangeSpec(line.From, line.From + 1, string.Empty);
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

    public static bool ToggleLineComment(CommandTarget target) => CommentLines(target, null);
    public static bool AddLineComment(CommandTarget target) => CommentLines(target, true);
    public static bool RemoveLineComment(CommandTarget target) => CommentLines(target, false);

    private static bool CommentLines(CommandTarget target, bool? add)
    {
        var lines = SelectedLines(target.State).Where(static line => !string.IsNullOrWhiteSpace(line.Text)).ToArray();
        var comment = add ?? !lines.All(static line => line.Text.TrimStart(' ', '\t').StartsWith("//", StringComparison.Ordinal));
        var changes = new List<ChangeSpec>();
        foreach (var line in lines) {
            var offset = 0;
            while (offset < line.Length && line.Text[offset] is ' ' or '\t') offset++;
            if (comment) changes.Add(new(line.From + offset, line.From + offset, "// "));
            else if (line.Text.AsSpan(offset).StartsWith("//", StringComparison.Ordinal)) {
                var length = offset + 2 < line.Length && line.Text[offset + 2] == ' ' ? 3 : 2;
                changes.Add(new(line.From + offset, line.From + offset + length, ""));
            }
        }
        return ApplyChanges(target, changes);
    }

    private static List<ChangeSpec> VisitSelectedLines(
        EditorState state,
        Func<Line, ChangeSpec?> createChange)
        => SelectedLines(state).Select(createChange).OfType<ChangeSpec>().ToList();

    private static IEnumerable<Line> SelectedLines(EditorState state)
    {
        var lastLineNumber = -1;
        foreach (var range in state.Selection.Ranges) {
            for (var position = range.From; position <= range.To;) {
                var line = state.Doc.LineAt(position);
                if (line.Number > lastLineNumber
                    && (range.Empty || range.To > line.From)) {
                    lastLineNumber = line.Number;
                    yield return line;
                }
                position = line.EndIncludingLineBreak;
                if (line.BreakLength == 0) break;
            }
        }
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
