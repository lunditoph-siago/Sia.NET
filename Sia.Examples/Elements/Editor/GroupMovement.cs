namespace Sia_Examples.Editor;

internal static class GroupMovement
{
    public static SelectionRange Move(
        EditorState state,
        SelectionRange range,
        bool forward)
    {
        var position = range.Head;
        var line = state.Doc.LineAt(position);
        if (position == (forward ? line.To : line.From)) {
            var documentBoundary = forward ? state.Doc.Length : 0;
            return EditorSelection.Cursor(
                position == documentBoundary ? position : position + (forward ? 1 : -1),
                forward ? -1 : 1);
        }

        var categorize = CharUtil.MakeCategorizer(string.Empty);
        var (next, text) = MoveCluster(line, position, forward);
        var category = categorize(text);
        position = next;
        while (position != (forward ? line.To : line.From)) {
            (next, text) = MoveCluster(line, position, forward);
            var nextCategory = categorize(text);
            if (category == CharCategory.Space) {
                category = nextCategory;
            }
            if (category != nextCategory) {
                break;
            }
            position = next;
        }
        return EditorSelection.Cursor(position, forward ? -1 : 1);
    }

    public static int FindDeleteBoundary(
        EditorState state,
        SelectionRange range,
        bool forward)
    {
        var position = range.Head;
        var line = state.Doc.LineAt(position);
        var categorize = CharUtil.MakeCategorizer(string.Empty);
        CharCategory? category = null;
        while (true) {
            if (position == (forward ? line.To : line.From)) {
                var documentBoundary = forward ? state.Doc.Length : 0;
                if (position == range.Head && position != documentBoundary) {
                    position += forward ? 1 : -1;
                }
                break;
            }

            var (next, text) = MoveCluster(line, position, forward);
            var nextCategory = categorize(text);
            if (category is not null && nextCategory != category) {
                break;
            }
            if (text != " " || position != range.Head) {
                category = nextCategory;
            }
            position = next;
        }
        return position;
    }

    private static (int Position, string Text) MoveCluster(
        Line line,
        int position,
        bool forward)
    {
        var next = line.From + CharUtil.FindClusterBreak(
            line.Text,
            position - line.From,
            forward);
        var from = Math.Min(position, next) - line.From;
        var to = Math.Max(position, next) - line.From;
        return (next, line.Text[from..to]);
    }
}
