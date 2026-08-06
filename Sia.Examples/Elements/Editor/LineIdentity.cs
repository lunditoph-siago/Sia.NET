namespace Sia_Examples.Editor;

public readonly record struct LineIds(int[] Ids, int NextId)
{
    public static LineIds Initial(int lineCount)
    {
        var ids = new int[lineCount];
        for (var i = 0; i < lineCount; i++) ids[i] = i;
        return new LineIds(ids, lineCount);
    }
}

public static class LineIdentity
{
    public static readonly StateField<LineIds> Field = StateField<LineIds>.Define(
        create: static state => LineIds.Initial(state.Doc.Lines),
        update: static (old, tr) =>
        {
            if (!tr.DocChanged) return old;

            var map = LineReuseMap.Compute(tr.Changes.Desc, tr.StartState.Doc, tr.NewDoc);
            var newCount = tr.NewDoc.Lines;
            var newIds = new int[newCount];
            var claimed = new bool[newCount];
            var nextId = old.NextId;

            for (var oldIndex = 0; oldIndex < map.Length; oldIndex++) {
                var newIndex = map[oldIndex];
                if (newIndex < 0 || newIndex >= newCount) continue;
                newIds[newIndex] = old.Ids[oldIndex];
                claimed[newIndex] = true;
            }
            for (var i = 0; i < newCount; i++) {
                if (!claimed[i]) newIds[i] = nextId++;
            }
            return new LineIds(newIds, nextId);
        });
}
