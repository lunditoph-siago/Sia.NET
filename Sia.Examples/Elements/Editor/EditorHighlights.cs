namespace Sia_Examples.Editor;

public static class EditorHighlights
{
    public static readonly StateEffectType<RangeSet<Decoration>> SetHighlights = StateEffectType<RangeSet<Decoration>>.Define();

    public static readonly StateField<RangeSet<Decoration>> Field = StateField<RangeSet<Decoration>>.Define(
        create: static _ => DecorationSet.Empty,
        update: static (value, tr) =>
        {
            var next = StateEffects.FindLast(tr.Effects, SetHighlights);
            if (next is not null) return next;
            return tr.DocChanged ? value.Map(tr.Changes.Desc) : value;
        });

    public static RangeSet<Decoration> FromRuns(IEnumerable<(int Start, int Length, string CssClass)> runs)
        => DecorationSet.Marks(runs.Select(r => (r.Start, r.Start + r.Length, r.CssClass)));
}
