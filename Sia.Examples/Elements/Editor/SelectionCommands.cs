namespace Sia_Examples.Editor;

public static class SelectionCommands
{
    public static bool SelectAll(CommandTarget t)
    {
        t.Apply(t.State.Apply(new TransactionSpec { Selection = EditorSelection.Single(0, t.State.Doc.Length), UserEvent = "select" }));
        return true;
    }

    public static bool SelectLine(CommandTarget t)
    {
        var s = t.State; var lines = LineCommands.SelectedLineBlocks(s);
        var ranges = lines.Select(l => EditorSelection.Range(l.From, Math.Min(l.To + 1, s.Doc.Length))).ToArray();
        t.Apply(s.Apply(new TransactionSpec { Selection = EditorSelection.Create(ranges), UserEvent = "select" }));
        return true;
    }

    public static bool SimplifySelection(CommandTarget t)
    {
        var cur = t.State.Selection;
        if (cur.Ranges.Count > 1)
            t.Apply(t.State.Apply(new TransactionSpec { Selection = EditorSelection.Create([cur.Main]), UserEvent = "select" }));
        else if (!cur.Main.Empty)
            t.Apply(t.State.Apply(new TransactionSpec { Selection = EditorSelection.Create([EditorSelection.Cursor(cur.Main.Head)]), UserEvent = "select" }));
        else return false;
        return true;
    }

    public static bool SelectDocStart(CommandTarget t)
    { t.Apply(t.State.Apply(new TransactionSpec { Selection = EditorSelection.Create([EditorSelection.Range(t.State.Selection.Main.Anchor, 0)]), UserEvent = "select" })); return true; }

    public static bool SelectDocEnd(CommandTarget t)
    { t.Apply(t.State.Apply(new TransactionSpec { Selection = EditorSelection.Create([EditorSelection.Range(t.State.Selection.Main.Anchor, t.State.Doc.Length)]), UserEvent = "select" })); return true; }
}
