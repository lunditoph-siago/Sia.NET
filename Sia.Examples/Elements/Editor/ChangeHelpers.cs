namespace Sia_Examples.Editor;

public static class ChangeHelpers
{
    public static (ChangeSpec[] Changes, EditorSelection Selection) InsertPerRange(
        IReadOnlyList<SelectionRange> ranges, Func<SelectionRange, string> insert)
    {
        var changes = new ChangeSpec[ranges.Count];
        var newRanges = new SelectionRange[ranges.Count];
        var shift = 0;
        for (var i = 0; i < ranges.Count; i++) {
            var r = ranges[i];
            var text = insert(r);
            changes[i] = new ChangeSpec(r.From, r.To, text);
            newRanges[i] = EditorSelection.Cursor(r.From + shift + text.Length);
            shift += text.Length - (r.To - r.From);
        }
        return (changes, EditorSelection.Create(newRanges));
    }
}
