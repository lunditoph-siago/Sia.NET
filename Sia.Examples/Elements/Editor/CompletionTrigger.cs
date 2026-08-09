namespace Sia_Examples.Editor;

public static class CompletionTrigger
{
    private const int _minimumIdentifierLength = 2;

    public static bool ShouldActivate(
        EditorState before,
        EditorState after,
        bool isActive)
    {
        var selection = after.Selection.Main;
        if (!selection.Empty
            || selection.Head <= 0
            || selection.Head > after.Doc.Length) {
            return false;
        }

        var lengthChange = after.Doc.Length - before.Doc.Length;
        if (lengthChange > 0) {
            var insertedCharacter = after.SliceDoc(selection.Head - 1, selection.Head)[0];
            if (insertedCharacter == '.') {
                return true;
            }
            return CompletionIdentifier.IsCharacter(insertedCharacter)
                && (isActive || HasCompletionContext(after, selection.Head));
        }
        return lengthChange < 0
            && isActive
            && HasCompletionContext(after, selection.Head);
    }

    private static bool HasCompletionContext(EditorState state, int position)
    {
        var line = state.Doc.LineAt(position);
        var identifierLength = IdentifierLengthBefore(line, position - line.From);
        var identifierStart = position - line.From - identifierLength;
        return identifierLength >= _minimumIdentifierLength
            || identifierStart > 0 && line.Text[identifierStart - 1] == '.';
    }

    private static int IdentifierLengthBefore(Line line, int column)
    {
        var start = column;
        while (start > 0 && CompletionIdentifier.IsCharacter(line.Text[start - 1])) {
            start--;
        }
        return column - start;
    }
}
