namespace Sia_Examples.Editor;

public static class DeleteCommands
{
    private static bool DeleteBy(CommandTarget t, Func<SelectionRange, int> by)
    {
        if (t.State.ReadOnly) return false;
        var evt = "delete.selection"; var s = t.State;
        var changes = new List<ChangeSpec>();
        foreach (var r in s.Selection.Ranges) {
            int from = r.From, to = r.To;
            if (from == to) {
                var towards = by(r);
                if (towards < from) evt = "delete.backward";
                else if (towards > from) evt = "delete.forward";
                from = Math.Min(from, towards); to = Math.Max(to, towards);
            }
            if (from != to) changes.Add(new ChangeSpec(from, to, ""));
        }
        if (changes.Count == 0) return false;
        t.Apply(s.Apply(new TransactionSpec { Changes = [.. changes], ScrollIntoView = true, UserEvent = evt }));
        return true;
    }

    public static bool CharBackward(CommandTarget t) => DeleteBy(t, r => {
        var pos = r.From; var s = t.State; var line = s.Doc.LineAt(pos);
        if (pos > line.From && pos < line.From + 200) {
            var before = line.Text[..(pos - line.From)];
            if (before.Length > 0 && before.All(c => c == ' ' || c == '\t')) {
                if (before[^1] == '\t') return pos - 1;
                var col = ColumnUtil.CountColumn(before, s.TabSize);
                var drop = col % 4; if (drop == 0) drop = 4;
                var p2 = pos;
                for (var i = 0; i < drop && before.Length - 1 - i >= 0 && before[^(1 + i)] == ' '; i++) p2--;
                return p2;
            }
        }
        var tp = CharUtil.FindClusterBreak(line.Text, pos - line.From, false) + line.From;
        return tp == pos && line.Number > 1 ? tp - 1 : tp;
    });

    public static bool CharForward(CommandTarget t) => DeleteBy(t, r => {
        var pos = r.From; var line = t.State.Doc.LineAt(pos);
        var tp = CharUtil.FindClusterBreak(line.Text, pos - line.From, true) + line.From;
        return tp == pos && line.Number < t.State.Doc.Lines ? tp + 1 : tp;
    });

    public static bool GroupBackward(CommandTarget t) => DeleteBy(t, r => {
        var pos = r.Head; var s = t.State; var line = s.Doc.LineAt(pos);
        for (; ; )
        {
            if (pos == line.From) { if (pos == r.Head && line.Number > 1) pos--; break; }
            var next = CharUtil.FindClusterBreak(line.Text, pos - line.From, false) + line.From;
            if (next == pos) break; pos = next;
            if (CharUtil.IsWordChar(line.Text[pos - line.From])) break;
        }
        return pos;
    });

    public static bool GroupForward(CommandTarget t) => DeleteBy(t, r => {
        var pos = r.Head; var s = t.State; var line = s.Doc.LineAt(pos);
        for (; ; )
        {
            if (pos == line.To) { if (pos == r.Head && line.Number < s.Doc.Lines) pos++; break; }
            var next = CharUtil.FindClusterBreak(line.Text, pos - line.From, true) + line.From;
            if (next == pos) break; pos = next;
            if (CharUtil.IsWordChar(line.Text[pos - line.From])) break;
        }
        return pos;
    });

    public static bool ToLineEnd(CommandTarget t) => DeleteBy(t, r => {
        var line = t.State.Doc.LineAt(r.Head);
        return r.Head < line.To ? line.To : Math.Min(t.State.Doc.Length, r.Head + 1);
    });

    public static bool ToLineStart(CommandTarget t) => DeleteBy(t, r => {
        var line = t.State.Doc.LineAt(r.Head);
        return r.Head > line.From ? line.From : Math.Max(0, r.Head - 1);
    });

    public static bool TrailingWhitespace(CommandTarget t)
    {
        if (t.State.ReadOnly) return false;
        var s = t.State; var changes = new List<ChangeSpec>();
        for (var i = 1; i <= s.Doc.Lines; i++) {
            var line = s.Doc.Line(i); var text = line.Text;
            var end = text.Length;
            while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;
            if (end < text.Length) changes.Add(new ChangeSpec(line.From + end, line.To, ""));
        }
        if (changes.Count == 0) return false;
        t.Apply(s.Apply(new TransactionSpec { Changes = [.. changes], UserEvent = "delete" }));
        return true;
    }
}
