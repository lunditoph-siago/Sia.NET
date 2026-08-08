namespace Sia_Examples.Editor;

public static class CursorCommands
{
    public static bool CharLeft(CommandTarget target)
        => Move(target, range => range.Empty
            ? ByCharacter(target.State, range, false)
            : Collapse(range, false));

    public static bool CharRight(CommandTarget target)
        => Move(target, range => range.Empty
            ? ByCharacter(target.State, range, true)
            : Collapse(range, true));

    public static bool GroupLeft(CommandTarget target)
        => Move(target, range => ByGroup(target.State, range, false));

    public static bool GroupRight(CommandTarget target)
        => Move(target, range => ByGroup(target.State, range, true));

    public static bool LineUp(CommandTarget target)
        => Move(target, range => range.Empty
            ? Vertically(target.State, range, false)
            : Collapse(range, false));

    public static bool LineDown(CommandTarget target)
        => Move(target, range => range.Empty
            ? Vertically(target.State, range, true)
            : Collapse(range, true));

    public static bool LineStart(CommandTarget target)
        => Move(target, range => EditorSelection.Cursor(
            target.State.Doc.LineAt(range.Head).From,
            1));

    public static bool LineEnd(CommandTarget target)
        => Move(target, range => EditorSelection.Cursor(
            target.State.Doc.LineAt(range.Head).To,
            -1));

    public static bool DocumentStart(CommandTarget target)
        => SetSelection(target, EditorSelection.Single(0));

    public static bool DocumentEnd(CommandTarget target)
        => SetSelection(target, EditorSelection.Single(target.State.Doc.Length));

    public static bool PageUp(CommandTarget target)
        => MovePage(target, -20);

    public static bool PageDown(CommandTarget target)
        => MovePage(target, 20);

    private static bool Move(
        CommandTarget target,
        Func<SelectionRange, SelectionRange> transform)
    {
        var selection = EditorSelection.Create(
            [.. target.State.Selection.Ranges.Select(transform)],
            target.State.Selection.MainIndex);
        return selection.Eq(target.State.Selection, true)
            ? false
            : SetSelection(target, selection);
    }

    private static bool MovePage(CommandTarget target, int lineOffset)
        => Move(target, range => {
            if (!range.Empty) {
                return Collapse(range, lineOffset > 0);
            }

            var document = target.State.Doc;
            var currentLine = document.LineAt(range.Head);
            var targetLine = document.Line(Math.Clamp(
                currentLine.Number + lineOffset,
                1,
                document.Lines));
            var column = Math.Min(
                range.GoalColumn ?? (range.Head - currentLine.From),
                targetLine.Length);
            return EditorSelection.Cursor(targetLine.From + column, 0, null, column);
        });

    private static bool SetSelection(CommandTarget target, EditorSelection selection)
    {
        target.Apply(target.State.Apply(new() { Selection = selection }));
        return true;
    }

    private static SelectionRange Collapse(SelectionRange range, bool forward)
        => EditorSelection.Cursor(forward ? range.To : range.From);

    private static SelectionRange ByCharacter(
        EditorState state,
        SelectionRange range,
        bool forward)
    {
        var position = range.Head;
        var line = state.Doc.LineAt(position);
        if (position == (forward ? line.To : line.From)) {
            position = forward
                ? Math.Min(state.Doc.Length, line.To + 1)
                : Math.Max(0, line.From - 1);
        } else {
            position = line.From + CharUtil.FindClusterBreak(
                line.Text,
                position - line.From,
                forward);
        }
        return EditorSelection.Cursor(position, forward ? -1 : 1);
    }

    private static SelectionRange Vertically(
        EditorState state,
        SelectionRange range,
        bool down)
    {
        var line = state.Doc.LineAt(range.Head);
        var lineNumber = line.Number + (down ? 1 : -1);
        if (lineNumber < 1 || lineNumber > state.Doc.Lines) {
            return range;
        }

        var targetLine = state.Doc.Line(lineNumber);
        var column = Math.Min(
            range.GoalColumn ?? (range.Head - line.From),
            targetLine.Length);
        return EditorSelection.Cursor(targetLine.From + column, 0, null, column);
    }

    private static SelectionRange ByGroup(
        EditorState state,
        SelectionRange range,
        bool forward)
    {
        if (!range.Empty) {
            return Collapse(range, forward);
        }

        var position = range.Head;
        var line = state.Doc.LineAt(position);
        while (true) {
            var next = line.From + CharUtil.FindClusterBreak(
                line.Text,
                position - line.From,
                forward);
            if (next == position || next <= line.From || next >= line.To) {
                break;
            }
            position = next;
            if (forward && CharUtil.IsWordChar(line.Text[position - line.From])) {
                break;
            }
            if (!forward
                && position > line.From
                && CharUtil.IsWordChar(line.Text[position - line.From - 1])) {
                break;
            }
        }
        return EditorSelection.Cursor(position, forward ? -1 : 1);
    }
}
