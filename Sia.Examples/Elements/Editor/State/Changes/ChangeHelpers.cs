namespace Sia_Examples.Editor;

public static class ChangeHelpers
{
    public static (ChangeSpec[] Changes, EditorSelection Selection) InsertPerRange(
        IReadOnlyList<SelectionRange> ranges, Func<SelectionRange, string> insert)
    {
        var changes = new ChangeSpec[ranges.Count];
        var newRanges = new SelectionRange[ranges.Count];
        var shift = 0;
        for (var index = 0; index < ranges.Count; index++) {
            var range = ranges[index];
            var text = insert(range);
            changes[index] = new(range.From, range.To, text);
            newRanges[index] = EditorSelection.Cursor(range.From + shift + text.Length);
            shift += text.Length - (range.To - range.From);
        }
        return (changes, EditorSelection.Create(newRanges));
    }
}
