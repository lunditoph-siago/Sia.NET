namespace Sia_Examples.Editor;

public static class TextCommands
{
    public static bool Insert(CommandTarget target, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var state = target.State;
        var (changes, selection) = ChangeHelpers.InsertPerRange(
            state.Selection.Ranges,
            _ => text);
        target.Apply(state.Apply(new() {
            Changes = changes,
            Selection = selection,
        }));
        return true;
    }
}
