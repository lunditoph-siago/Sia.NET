namespace Sia_Examples.Editor;

public static class LineCommands
{
    internal static List<(int From, int To)> SelectedLineBlocks(EditorState s)
    {
        var blocks = new List<(int From, int To)>(); var upto = -1;
        foreach (var r in s.Selection.Ranges)
        {
            var sl = s.Doc.LineAt(r.From); var el = s.Doc.LineAt(r.To);
            if (!r.Empty && r.To == el.From) el = s.Doc.LineAt(r.To - 1);
            if (upto >= sl.Number) blocks[^1] = (blocks[^1].From, el.To);
            else blocks.Add((sl.From, el.To));
            upto = el.Number + 1;
        }
        return blocks;
    }

    public static bool SplitLine(CommandTarget t)
    {
        if (t.State.ReadOnly) return false;
        var (changes, selection) = ChangeHelpers.InsertPerRange(t.State.Selection.Ranges, _ => "\n");
        t.Apply(t.State.Apply(new TransactionSpec { Changes = changes, Selection = selection, ScrollIntoView = true, UserEvent = "input" }));
        return true;
    }

    public static bool InsertNewline(CommandTarget t)
    {
        var s = t.State;
        var main = s.Selection.Main;
        t.Apply(s.Apply(new TransactionSpec {
            Changes = [new ChangeSpec(main.From, main.To, "\n")],
            Selection = EditorSelection.Single(main.From + 1),
            ScrollIntoView = true, UserEvent = "input"
        }));
        return true;
    }

    public static bool InsertNewlineAndIndent(CommandTarget t)
    {
        if (t.State.ReadOnly) return false;
        var s = t.State;
        var (changes, selection) = ChangeHelpers.InsertPerRange(s.Selection.Ranges, r =>
            "\n" + new string(' ', s.Doc.LineAt(r.From).Text.TakeWhile(char.IsWhiteSpace).Count()));
        t.Apply(s.Apply(new TransactionSpec { Changes = changes, Selection = selection, ScrollIntoView = true, UserEvent = "input" }));
        return true;
    }

    public static bool InsertTab(CommandTarget t)
    {
        var s = t.State;
        if (s.Selection.Ranges.Any(r => !r.Empty)) return IndentMore(t);
        var main = s.Selection.Main;
        t.Apply(s.Apply(new TransactionSpec {
            Changes = [new ChangeSpec(main.From, main.To, "\t")],
            Selection = EditorSelection.Single(main.From + 1),
            ScrollIntoView = true, UserEvent = "input"
        }));
        return true;
    }

    public static bool TransposeChars(CommandTarget t)
    {
        if (t.State.ReadOnly) return false;
        var s = t.State; var cl = new List<ChangeSpec>();
        foreach (var r in s.Selection.Ranges)
        {
            if (r.Empty && r.From > 0 && r.From < s.Doc.Length)
            {
                var pos = r.From; var line = s.Doc.LineAt(pos);
                var from = pos == line.From ? pos - 1 : CharUtil.FindClusterBreak(line.Text, pos - line.From, false) + line.From;
                var to = pos == line.To ? pos + 1 : CharUtil.FindClusterBreak(line.Text, pos - line.From, true) + line.From;
                cl.Add(new ChangeSpec(from, to, s.SliceDoc(pos, to) + s.SliceDoc(from, pos)));
            }
        }
        if (cl.Count == 0) return false;
        t.Apply(s.Apply(new TransactionSpec { Changes = [.. cl], ScrollIntoView = true, UserEvent = "move.character" }));
        return true;
    }

    public static bool DeleteLine(CommandTarget t)
    {
        if (t.State.ReadOnly) return false;
        var s = t.State; var blocks = SelectedLineBlocks(s);
        var cl = blocks.Select(b =>
        {
            int f = b.From, to = b.To;
            if (f > 0) f--; else if (to < s.Doc.Length) to++;
            return new ChangeSpec(f, to, "");
        }).ToList();
        if (cl.Count == 0) return false;
        t.Apply(s.Apply(new TransactionSpec { Changes = [.. cl], ScrollIntoView = true, UserEvent = "delete.line" }));
        return true;
    }

    public static bool IndentMore(CommandTarget t)
    {
        if (t.State.ReadOnly) return false;
        var s = t.State; var indent = "    "; var changes = new List<ChangeSpec>(); var atLine = -1;
        foreach (var r in s.Selection.Ranges)
            for (var pos = r.From; pos <= r.To;)
            {
                var line = s.Doc.LineAt(pos);
                if (line.Number > atLine && (r.Empty || r.To > line.From))
                { changes.Add(new ChangeSpec(line.From, line.From, indent)); atLine = line.Number; }
                pos = line.To + 1;
            }
        t.Apply(s.Apply(new TransactionSpec { Changes = [.. changes], UserEvent = "input.indent" }));
        return true;
    }

    public static bool IndentLess(CommandTarget t)
    {
        if (t.State.ReadOnly) return false;
        var s = t.State; var changes = new List<ChangeSpec>(); var atLine = -1;
        foreach (var r in s.Selection.Ranges)
            for (var pos = r.From; pos <= r.To;)
            {
                var line = s.Doc.LineAt(pos);
                if (line.Number > atLine && (r.Empty || r.To > line.From))
                {
                    var text = line.Text; var spaces = 0;
                    while (spaces < 4 && spaces < text.Length && text[spaces] == ' ') spaces++;
                    if (spaces > 0) changes.Add(new ChangeSpec(line.From, line.From + spaces, ""));
                    atLine = line.Number;
                }
                pos = line.To + 1;
            }
        if (changes.Count == 0) return false;
        t.Apply(s.Apply(new TransactionSpec { Changes = [.. changes], UserEvent = "delete.dedent" }));
        return true;
    }
}
