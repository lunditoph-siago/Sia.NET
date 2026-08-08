namespace Sia_Examples.Editor;

public static class CursorCommands
{
    internal static bool MoveSel(CommandTarget t, Func<SelectionRange, SelectionRange> f)
    {
        var sel = EditorSelection.Create([.. t.State.Selection.Ranges.Select(f)], t.State.Selection.MainIndex);
        if (sel.Eq(t.State.Selection, true)) return false;
        t.Apply(t.State.Apply(new TransactionSpec { Selection = sel, ScrollIntoView = true, UserEvent = "select" }));
        return true;
    }

    internal static SelectionRange RangeEnd(SelectionRange r, bool fwd)
        => EditorSelection.Cursor(fwd ? r.To : r.From);

    internal static SelectionRange ByChar(EditorState s, SelectionRange r, bool fwd)
    {
        var pos = r.Head; var line = s.Doc.LineAt(pos);
        if (pos == (fwd ? line.To : line.From))
            pos = fwd ? Math.Min(s.Doc.Length, line.To + 1) : Math.Max(0, line.From - 1);
        else pos = line.From + CharUtil.FindClusterBreak(line.Text, pos - line.From, fwd);
        return EditorSelection.Cursor(pos, fwd ? -1 : 1);
    }

    internal static SelectionRange Vertical(EditorState s, SelectionRange r, bool down)
    {
        var line = s.Doc.LineAt(r.Head);
        var tl = down ? line.Number + 1 : line.Number - 1;
        if (tl < 1 || tl > s.Doc.Lines) return r;
        var target = s.Doc.Line(tl);
        var col = Math.Min(r.GoalColumn ?? (r.Head - line.From), target.Length);
        return EditorSelection.Cursor(target.From + col, 0, null, col);
    }

    internal static SelectionRange ByGroup(EditorState s, SelectionRange r, bool fwd)
    {
        if (!r.Empty) return RangeEnd(r, fwd);
        var pos = r.Head; var line = s.Doc.LineAt(pos);
        while (true) {
            var next = CharUtil.FindClusterBreak(line.Text, pos - line.From, fwd) + line.From;
            if (next == pos || next <= line.From || next >= line.To) break;
            pos = next;
            if (fwd && CharUtil.IsWordChar(line.Text[pos - line.From])) break;
            if (!fwd && pos > line.From && CharUtil.IsWordChar(line.Text[pos - line.From - 1])) break;
        }
        return EditorSelection.Cursor(pos, fwd ? -1 : 1);
    }

    public static bool CharLeft(CommandTarget t) => MoveSel(t, r => r.Empty ? ByChar(t.State, r, false) : RangeEnd(r, false));
    public static bool CharRight(CommandTarget t) => MoveSel(t, r => r.Empty ? ByChar(t.State, r, true) : RangeEnd(r, true));
    public static bool GroupLeft(CommandTarget t) => MoveSel(t, r => ByGroup(t.State, r, false));
    public static bool GroupRight(CommandTarget t) => MoveSel(t, r => ByGroup(t.State, r, true));
    public static bool LineUp(CommandTarget t) => MoveSel(t, r => r.Empty ? Vertical(t.State, r, false) : RangeEnd(r, false));
    public static bool LineDown(CommandTarget t) => MoveSel(t, r => r.Empty ? Vertical(t.State, r, true) : RangeEnd(r, true));

    public static bool LineStart(CommandTarget t)
        => MoveSel(t, r => EditorSelection.Cursor(t.State.Doc.LineAt(r.Head).From, 1));

    public static bool LineEnd(CommandTarget t)
        => MoveSel(t, r => EditorSelection.Cursor(t.State.Doc.LineAt(r.Head).To, -1));

    public static bool DocStart(CommandTarget t)
    { t.Apply(t.State.Apply(new TransactionSpec { Selection = EditorSelection.Single(0), ScrollIntoView = true })); return true; }

    public static bool DocEnd(CommandTarget t)
    { t.Apply(t.State.Apply(new TransactionSpec { Selection = EditorSelection.Single(t.State.Doc.Length), ScrollIntoView = true })); return true; }

    public static bool PageUp(CommandTarget t, int pageSize = 20) => MoveSel(t, r => {
        if (!r.Empty) return RangeEnd(r, false);
        var line = t.State.Doc.LineAt(r.Head);
        var tl = t.State.Doc.Line(Math.Max(1, line.Number - pageSize));
        var col = Math.Min(r.GoalColumn ?? (r.Head - line.From), tl.Length);
        return EditorSelection.Cursor(tl.From + col, 0, null, col);
    });

    public static bool PageDown(CommandTarget t, int pageSize = 20) => MoveSel(t, r => {
        if (!r.Empty) return RangeEnd(r, true);
        var line = t.State.Doc.LineAt(r.Head);
        var tl = t.State.Doc.Line(Math.Min(t.State.Doc.Lines, line.Number + pageSize));
        var col = Math.Min(r.GoalColumn ?? (r.Head - line.From), tl.Length);
        return EditorSelection.Cursor(tl.From + col, 0, null, col);
    });

    public static bool MatchingBracket(CommandTarget t)
    {
        var s = t.State; var found = false;
        var brackets = new Dictionary<char, char> { ['('] = ')', [')'] = '(', ['['] = ']', [']'] = '[', ['{'] = '}', ['}'] = '{' };
        var sel = EditorSelection.Create([.. s.Selection.Ranges.Select(r =>
        {
            var pos = r.Head;
            var ch = pos < s.Doc.Length ? s.SliceDoc(pos, pos + 1)[0] : '\0';
            if (!brackets.TryGetValue(ch, out var match))
            { if (pos > 0) { ch = s.SliceDoc(pos - 1, pos)[0]; if (!brackets.TryGetValue(ch, out match)) return r; } else return r; }
            int dir = match is ')' or ']' or '}' ? 1 : -1, depth = 1, sp = dir > 0 ? pos + 1 : pos - 1;
            while (depth > 0 && sp >= 0 && sp < s.Doc.Length)
            { var c = s.SliceDoc(sp, sp + 1)[0]; if (c == ch) depth++; else if (c == match) depth--; if (depth > 0) sp += dir; }
            if (depth != 0) return r;
            found = true;
            return EditorSelection.Cursor(sp);
        })], s.Selection.MainIndex);
        if (!found) return false;
        t.Apply(s.Apply(new TransactionSpec { Selection = sel, ScrollIntoView = true }));
        return true;
    }
}
