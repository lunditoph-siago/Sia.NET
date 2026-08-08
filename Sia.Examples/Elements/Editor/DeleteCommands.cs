namespace Sia_Examples.Editor;

public static class DeleteCommands
{
    public static bool CharBackward(CommandTarget target)
        => DeleteBy(target, range => FindCharacterBoundary(target.State, range, false));

    public static bool CharForward(CommandTarget target)
        => DeleteBy(target, range => FindCharacterBoundary(target.State, range, true));

    public static bool ToLineEnd(CommandTarget target)
        => DeleteBy(target, range => {
            var line = target.State.Doc.LineAt(range.Head);
            return range.Head < line.To
                ? line.To
                : Math.Min(target.State.Doc.Length, range.Head + 1);
        });

    private static bool DeleteBy(
        CommandTarget target,
        Func<SelectionRange, int> findBoundary)
    {
        var changes = new List<ChangeSpec>();
        foreach (var range in target.State.Selection.Ranges) {
            var from = range.From;
            var to = range.To;
            if (from == to) {
                var boundary = findBoundary(range);
                from = Math.Min(from, boundary);
                to = Math.Max(to, boundary);
            }
            if (from != to) {
                changes.Add(new(from, to, string.Empty));
            }
        }

        if (changes.Count == 0) {
            return false;
        }
        target.Apply(target.State.Apply(new() { Changes = [.. changes] }));
        return true;
    }

    private static int FindCharacterBoundary(
        EditorState state,
        SelectionRange range,
        bool forward)
    {
        var position = range.From;
        var line = state.Doc.LineAt(position);
        if (!forward && position > line.From && position < line.From + 200) {
            var prefix = line.Text[..(position - line.From)];
            if (prefix.Length > 0 && prefix.All(static character => character is ' ' or '\t')) {
                if (prefix[^1] == '\t') {
                    return position - 1;
                }

                var columns = ColumnUtil.CountColumn(prefix, EditorState.TabSize);
                var drop = columns % EditorState.TabSize;
                drop = drop == 0 ? EditorState.TabSize : drop;
                var indentationBoundary = position;
                for (var index = 0;
                    index < drop
                        && prefix.Length - index - 1 >= 0
                        && prefix[^(index + 1)] == ' ';
                    index++) {
                    indentationBoundary--;
                }
                return indentationBoundary;
            }
        }

        var boundaryInLine = CharUtil.FindClusterBreak(
            line.Text,
            position - line.From,
            forward);
        var boundary = line.From + boundaryInLine;
        if (boundary != position) {
            return boundary;
        }
        if (forward && line.Number < state.Doc.Lines) {
            return boundary + 1;
        }
        if (!forward && line.Number > 1) {
            return boundary - 1;
        }
        return boundary;
    }
}
