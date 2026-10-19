using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

public static class EditorDecorations
{
    public static RangeSet<Decoration> FromHighlights(IEnumerable<HighlightRun> highlights)
        => DecorationSet.Marks(highlights.Select(static highlight => (
            highlight.Start,
            highlight.Start + highlight.Length,
            highlight.Classification)));
}
