namespace Sia_Examples.Editor;

public static class SelectionCommands
{
    public static bool SelectAll(CommandTarget target)
    {
        target.Apply(target.State.Apply(new() {
            Selection = EditorSelection.Single(0, target.State.Doc.Length),
        }));
        return true;
    }

    public static bool SimplifySelection(CommandTarget target)
    {
        var selection = target.State.Selection;
        EditorSelection simplified;
        if (selection.Ranges.Count > 1) {
            simplified = EditorSelection.Create([selection.Main]);
        } else if (!selection.Main.Empty) {
            simplified = EditorSelection.Single(selection.Main.Head);
        } else {
            return false;
        }

        target.Apply(target.State.Apply(new() { Selection = simplified }));
        return true;
    }
}
