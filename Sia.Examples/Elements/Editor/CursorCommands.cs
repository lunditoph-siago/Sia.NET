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

    public static bool SelectCharLeft(CommandTarget target)
        => Extend(target, range => ByCharacter(target.State, range, false));

    public static bool SelectCharRight(CommandTarget target)
        => Extend(target, range => ByCharacter(target.State, range, true));

    public static bool GroupLeft(CommandTarget target)
        => Move(target, range => range.Empty
            ? GroupMovement.Move(target.State, range, false)
            : Collapse(range, false));

    public static bool GroupRight(CommandTarget target)
        => Move(target, range => range.Empty
            ? GroupMovement.Move(target.State, range, true)
            : Collapse(range, true));

    public static bool SelectGroupLeft(CommandTarget target)
        => Extend(target, range => GroupMovement.Move(
            target.State,
            EditorSelection.Cursor(range.Head),
            false));

    public static bool SelectGroupRight(CommandTarget target)
        => Extend(target, range => GroupMovement.Move(
            target.State,
            EditorSelection.Cursor(range.Head),
            true));

    public static bool LineUp(CommandTarget target)
        => Move(target, range => range.Empty
            ? Vertically(target.State, range, false)
            : Collapse(range, false));

    public static bool LineDown(CommandTarget target)
        => Move(target, range => range.Empty
            ? Vertically(target.State, range, true)
            : Collapse(range, true));

    public static bool SelectLineUp(CommandTarget target)
        => Extend(target, range => Vertically(target.State, range, false));

    public static bool SelectLineDown(CommandTarget target)
        => Extend(target, range => Vertically(target.State, range, true));

    public static bool LineStart(CommandTarget target)
        => Move(target, range => EditorSelection.Cursor(
            target.State.Doc.LineAt(range.Head).From,
            1));

    public static bool LineEnd(CommandTarget target)
        => Move(target, range => EditorSelection.Cursor(
            target.State.Doc.LineAt(range.Head).To,
            -1));

    public static bool SelectLineStart(CommandTarget target)
        => Extend(target, range => EditorSelection.Cursor(
            target.State.Doc.LineAt(range.Head).From,
            1));

    public static bool SelectLineEnd(CommandTarget target)
        => Extend(target, range => EditorSelection.Cursor(
            target.State.Doc.LineAt(range.Head).To,
            -1));

    public static bool DocumentStart(CommandTarget target)
        => SetSelection(target, EditorSelection.Single(0));

    public static bool DocumentEnd(CommandTarget target)
        => SetSelection(target, EditorSelection.Single(target.State.Doc.Length));

    public static bool SelectDocumentStart(CommandTarget target)
        => Extend(target, static _ => EditorSelection.Cursor(0));

    public static bool SelectDocumentEnd(CommandTarget target)
        => Extend(target, _ => EditorSelection.Cursor(target.State.Doc.Length));

    public static bool PageUp(CommandTarget target)
        => MovePage(target, -20);

    public static bool PageDown(CommandTarget target)
        => MovePage(target, 20);

    public static bool SelectPageUp(CommandTarget target)
        => Extend(target, range => ByPage(target.State, range, -20));

    public static bool SelectPageDown(CommandTarget target)
        => Extend(target, range => ByPage(target.State, range, 20));

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

    private static bool Extend(
        CommandTarget target,
        Func<SelectionRange, SelectionRange> moveHead)
        => Move(target, range => {
            var head = moveHead(range);
            return EditorSelection.Range(range.Anchor, head.Head, head.GoalColumn);
        });

    private static bool MovePage(CommandTarget target, int lineOffset)
        => Move(target, range => {
            if (!range.Empty) {
                return Collapse(range, lineOffset > 0);
            }
            return ByPage(target.State, range, lineOffset);
        });

    private static SelectionRange ByPage(
        EditorState state,
        SelectionRange range,
        int lineOffset)
    {
        var currentLine = state.Doc.LineAt(range.Head);
        var targetLine = state.Doc.Line(Math.Clamp(
            currentLine.Number + lineOffset,
            1,
            state.Doc.Lines));
        var column = Math.Min(
            range.GoalColumn ?? (range.Head - currentLine.From),
            targetLine.Length);
        return EditorSelection.Cursor(targetLine.From + column, 0, null, column);
    }

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

}
