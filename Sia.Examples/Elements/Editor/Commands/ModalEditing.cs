namespace Sia.Examples.Editor;

internal readonly record struct ModalEdit(EditorState State, EditorInputMode Mode);

internal static class ModalEditing
{
    private static readonly Func<string, CharCategory> s_Category = CharUtil.MakeCategorizer(string.Empty);

    internal static ModalEdit? Apply(EditorState state, ModalInvocation invocation, EditorInputMode mode)
    {
        var document = state.Doc;
        var source = document.SliceDoc();
        var range = state.Selection.Main;
        var visual = mode == EditorInputMode.Visual;
        var head = Head(source, range, visual);
        if (mode == EditorInputMode.Normal) head = Clamp(document, head);
        var line = document.LineAt(head);
        var count = System.Math.Clamp(invocation.Count, 1, ModalInput.k_MaximumCount);
        invocation = invocation with { Count = count };
        if (invocation.Id == EditorCommandId.Escape) {
            if (mode == EditorInputMode.Insert && head > line.From) head = Previous(source, head);
            return new(state.Apply(new() { Selection = EditorSelection.Single(Clamp(document, head)) }), EditorInputMode.Normal);
        }
        if (invocation.Id == EditorCommandId.VisualMode) {
            return visual
                ? new(state.Apply(new() { Selection = EditorSelection.Single(head) }), EditorInputMode.Normal)
                : new(state.Apply(new() { Selection = EditorSelection.Single(head, Next(source, head)) }), EditorInputMode.Visual);
        }
        if (invocation.Id == EditorCommandId.SelectAll)
            return new(state.Apply(new() { Selection = EditorSelection.Single(0, document.Length) }), EditorInputMode.Visual);
        if (ModalInput.IsMotion(invocation.Id)) return MoveSelection(state, invocation, mode, head);
        if (invocation.Id is EditorCommandId.InsertMode or EditorCommandId.AppendMode or EditorCommandId.InsertLineStart
            or EditorCommandId.AppendLineEnd or EditorCommandId.OpenBelow or EditorCommandId.OpenAbove)
            return BeginInsertion(state, invocation, head);
        if (invocation.Id == EditorCommandId.Newline && visual) return Apply(state, new(EditorCommandId.Down, count), mode);
        if (invocation.Id == EditorCommandId.Newline) {
            var target = document.Line(System.Math.Min(document.Lines, line.Number + count));
            return new(state.Apply(new() { Selection = EditorSelection.Single(Clamp(document, target.From + target.Text.TakeWhile(char.IsWhiteSpace).Count())) }), mode);
        }
        if (invocation.Id == EditorCommandId.Backspace && !visual)
            return Apply(state, new(EditorCommandId.Left, count), mode);

        return ApplyOperator(state, invocation, mode, head);
    }

    private static ModalEdit? ApplyOperator(EditorState state, ModalInvocation invocation, EditorInputMode mode, int head)
    {
        var document = state.Doc;
        var range = state.Selection.Main;
        var visual = mode == EditorInputMode.Visual;
        var line = document.LineAt(head);
        var count = invocation.Count;
        var operation = invocation.Id switch {
            EditorCommandId.Delete or EditorCommandId.DeleteLineEnd or EditorCommandId.DeleteWordLeft or EditorCommandId.DeleteWordRight => EditorCommandId.DeleteOperator,
            EditorCommandId.Indent => EditorCommandId.IndentOperator,
            EditorCommandId.Outdent => EditorCommandId.OutdentOperator,
            _ => invocation.Id,
        };
        if (!ModalInput.IsOperator(operation)) return null;
        int fromRange, toRange;
        var characterwise = invocation.Id is EditorCommandId.Delete or EditorCommandId.DeleteLineEnd
            or EditorCommandId.DeleteWordLeft or EditorCommandId.DeleteWordRight;
        var lineMotion = invocation.Motion is EditorCommandId.Up or EditorCommandId.Down or EditorCommandId.Start or EditorCommandId.End;
        var linewise = !visual && (lineMotion || invocation.Motion is null && !characterwise);
        if (visual) { fromRange = range.From; toRange = range.To; }
        else if (linewise) {
            var target = invocation.Motion is { } motion ? document.LineAt(Move(state, head, motion, count, invocation.Counted).Head)
                : document.Line(System.Math.Min(document.Lines, line.Number + count - 1));
            fromRange = document.Line(System.Math.Min(line.Number, target.Number)).From;
            var last = document.Line(System.Math.Max(line.Number, target.Number));
            toRange = operation == EditorCommandId.ChangeOperator ? last.To : last.EndIncludingLineBreak;
            if (operation == EditorCommandId.DeleteOperator && last.BreakLength == 0 && fromRange > 0)
                fromRange = document.Line(document.LineAt(fromRange).Number - 1).To;
        }
        else {
            var motion = invocation.Id switch {
                EditorCommandId.DeleteLineEnd => EditorCommandId.LineEnd,
                EditorCommandId.DeleteWordLeft => EditorCommandId.WordLeft,
                EditorCommandId.DeleteWordRight => EditorCommandId.WordRight,
                _ => invocation.Motion ?? EditorCommandId.Right,
            };
            var destination = Move(state, head, motion, count, invocation.Counted, endpoint: true).Head;
            fromRange = System.Math.Min(head, destination);
            toRange = System.Math.Max(head, destination);
        }
        if (operation is EditorCommandId.IndentOperator or EditorCommandId.OutdentOperator) {
            var after = state.Apply(new() { Selection = EditorSelection.Single(fromRange, toRange) });
            LineCommands.IndentLines(new(after, next => after = next), (operation == EditorCommandId.IndentOperator ? 1 : -1) * (visual ? count : 1));
            var first = after.Doc.LineAt(after.Selection.Main.From);
            var caret = first.From + first.Text.TakeWhile(char.IsWhiteSpace).Count();
            return new(after.Apply(new() { Selection = EditorSelection.Single(Clamp(after.Doc, caret)) }), EditorInputMode.Normal);
        }
        var replacement = operation == EditorCommandId.ChangeOperator && linewise
            ? new string(line.Text.TakeWhile(char.IsWhiteSpace).ToArray()) : string.Empty;
        var nextMode = operation == EditorCommandId.ChangeOperator ? EditorInputMode.Insert : EditorInputMode.Normal;
        var edited = state.Apply(new() {
            Changes = fromRange == toRange && replacement.Length == 0 ? [] : [new(fromRange, toRange, replacement)],
            Selection = EditorSelection.Single(fromRange + replacement.Length)
        });
        return new(edited.Apply(new() {
            Selection = EditorSelection.Single(nextMode == EditorInputMode.Insert
            ? edited.Selection.Main.Head : Clamp(edited.Doc, edited.Selection.Main.Head))
        }), nextMode);
    }

    private static ModalEdit MoveSelection(EditorState state, ModalInvocation invocation, EditorInputMode mode, int head)
    {
        var document = state.Doc;
        var source = document.SliceDoc();
        var range = state.Selection.Main;
        var visual = mode == EditorInputMode.Visual;
        var count = invocation.Count;
        var destination = Move(state, head, invocation.Id, count, invocation.Counted);
        var position = Clamp(document, destination.Head);
        EditorSelection selection;
        if (visual) {
            var anchor = range.Anchor <= range.Head ? range.Anchor : Previous(source, range.Anchor);
            selection = EditorSelection.Create([position < anchor
                ? EditorSelection.Range(Next(source, anchor), position, destination.GoalColumn)
                : EditorSelection.Range(anchor, Next(source, position), destination.GoalColumn)]);
        }
        else selection = EditorSelection.Create([EditorSelection.Cursor(position, goalColumn: destination.GoalColumn)]);
        return new(state.Apply(new() { Selection = selection }), mode);
    }

    private static ModalEdit BeginInsertion(EditorState state, ModalInvocation invocation, int head)
    {
        var document = state.Doc;
        var source = document.SliceDoc();
        var line = document.LineAt(head);
        var count = invocation.Count;
        if (invocation.Id is EditorCommandId.OpenBelow or EditorCommandId.OpenAbove) {
            var indentation = new string(line.Text.TakeWhile(char.IsWhiteSpace).ToArray());
            var newline = document.NewlineAt(head);
            var below = invocation.Id == EditorCommandId.OpenBelow;
            var insertion = string.Concat(Enumerable.Repeat(below ? newline + indentation : indentation + newline, count));
            var from = below ? line.To : line.From;
            var caret = below ? from + insertion.Length : from + insertion.Length - newline.Length;
            return new(state.Apply(new() { Changes = [new(from, from, insertion)], Selection = EditorSelection.Single(caret) }), EditorInputMode.Insert);
        }
        var position = invocation.Id switch {
            EditorCommandId.AppendMode => System.Math.Min(line.To, Next(source, head)),
            EditorCommandId.InsertLineStart => line.From + line.Text.TakeWhile(char.IsWhiteSpace).Count(),
            EditorCommandId.AppendLineEnd => line.To,
            _ => head,
        };
        return new(state.Apply(new() { Selection = EditorSelection.Single(position) }), EditorInputMode.Insert);
    }

    internal static int Head(string source, SelectionRange range, bool visual)
        => visual && !range.Empty && range.Head > range.Anchor ? Previous(source, range.Head) : range.Head;

    internal static int Clamp(Text document, int position)
    {
        var line = document.LineAt(System.Math.Clamp(position, 0, document.Length));
        var last = line.Length == 0 ? line.From : line.From + CharUtil.FindClusterBreak(line.Text, line.Length, forward: false);
        return System.Math.Clamp(position, line.From, last);
    }

    private static SelectionRange Move(EditorState state, int head, EditorCommandId motion, int count, bool counted, bool endpoint = false)
    {
        var doc = state.Doc;
        var source = doc.SliceDoc();
        var line = doc.LineAt(head);
        if (motion is EditorCommandId.Up or EditorCommandId.Down) {
            var target = doc.Line(System.Math.Clamp(line.Number + (motion == EditorCommandId.Down ? count : -count), 1, doc.Lines));
            var column = state.Selection.Main.GoalColumn ?? ColumnUtil.CountColumn(line.Text, EditorState.TabSize, head - line.From);
            return EditorSelection.Cursor(target.From + ColumnUtil.FindColumn(target.Text, column, EditorState.TabSize), goalColumn: column);
        }
        if (motion is EditorCommandId.Start or EditorCommandId.End) {
            var target = doc.Line(counted ? System.Math.Min(count, doc.Lines) : motion == EditorCommandId.Start ? 1 : doc.Lines);
            return EditorSelection.Cursor(target.From + target.Text.TakeWhile(char.IsWhiteSpace).Count());
        }
        if (motion == EditorCommandId.LineStart) return EditorSelection.Cursor(line.From);
        if (motion == EditorCommandId.LineEnd) return EditorSelection.Cursor(endpoint ? line.To : Clamp(doc, line.To));
        var position = head;
        for (var repeat = 0; repeat < count; repeat++) {
            var before = position;
            position = motion switch {
                EditorCommandId.Left => System.Math.Max(line.From, Previous(source, position)),
                EditorCommandId.Right => System.Math.Min(endpoint ? line.To : Clamp(doc, line.To), Next(source, position)),
                EditorCommandId.WordRight => Word(source, position, true),
                EditorCommandId.WordLeft => Word(source, position, false),
                _ => position,
            };
            if (position == before) break;
        }
        return EditorSelection.Cursor(position);
    }

    private static int Word(string source, int position, bool forward)
    {
        if (forward) {
            if (position >= source.Length) return source.Length;
            var category = s_Category(source[position..Next(source, position)]);
            while (position < source.Length && s_Category(source[position..Next(source, position)]) == category) position = Next(source, position);
            while (position < source.Length && char.IsWhiteSpace(source[position])) position = Next(source, position);
            return position;
        }
        if (position <= 0) return 0;
        position = Previous(source, position);
        while (position > 0 && char.IsWhiteSpace(source[position])) position = Previous(source, position);
        var kind = s_Category(source[position..Next(source, position)]);
        while (position > 0) {
            var previous = Previous(source, position);
            if (s_Category(source[previous..position]) != kind) break;
            position = previous;
        }
        return position;
    }

    private static int Next(string source, int position) => position >= source.Length ? source.Length : CharUtil.FindClusterBreak(source, position);
    private static int Previous(string source, int position) => CharUtil.FindClusterBreak(source, position, forward: false);
}
