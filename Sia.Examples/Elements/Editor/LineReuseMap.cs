namespace Sia_Examples.Editor;

public static class LineReuseMap
{
    public static int[] Compute(ChangeDesc changes, Text oldDoc, Text newDoc)
    {
        var oldLines = oldDoc.Lines;
        var map = new int[oldLines];
        if (changes.IsEmpty) {
            for (var i = 0; i < oldLines; i++) map[i] = i;
            return map;
        }

        var oldPos = 0;
        var delta = 0;

        changes.IterChangedRanges((fromA, toA, fromB, toB) => {
            var startLineA = oldDoc.LineAt(fromA);
            var startLine = startLineA.Number - 1;
            var endLine = LastTouchedLine(oldDoc, fromA, toA, startLineA.To);
            var startLineB = newDoc.LineAt(fromB);
            var newStartLine = startLineB.Number - 1;
            var newEndLine = LastTouchedLine(newDoc, fromB, toB, startLineB.To);

            for (; oldPos < startLine && oldPos < oldLines; oldPos++) map[oldPos] = oldPos + delta;

            var oldCount = Math.Max(0, endLine - startLine + 1);
            var newCount = Math.Max(0, newEndLine - newStartLine + 1);
            var shared = Math.Min(oldCount, newCount);
            for (var k = 0; k < shared && oldPos < oldLines; k++, oldPos++) map[oldPos] = newStartLine + k;
            for (; oldPos <= endLine && oldPos < oldLines; oldPos++) map[oldPos] = -1;

            delta = (newEndLine + 1) - (endLine + 1);
        }, individual: true);

        for (; oldPos < oldLines; oldPos++) map[oldPos] = oldPos + delta;
        return map;
    }

    private static int LastTouchedLine(Text doc, int from, int to, int startLineEnd)
    {
        var line = doc.LineAt(to);
        if (to != line.From) return line.Number - 1;
        return from == startLineEnd ? line.Number - 1 : line.Number - 2;
    }
}
