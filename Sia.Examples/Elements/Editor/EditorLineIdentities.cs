namespace Sia_Examples.Editor;

public readonly record struct EditorLineIdentities(int[] Values, int NextValue)
{
    public static EditorLineIdentities Create(int lineCount)
    {
        var values = new int[lineCount];
        for (var index = 0; index < lineCount; index++) {
            values[index] = index;
        }
        return new(values, lineCount);
    }

    public EditorLineIdentities Map(ChangeSet changes, Text oldDocument, Text newDocument)
    {
        if (changes.IsEmpty) {
            return this;
        }

        var mapping = LineReuseMap.Compute(changes, oldDocument, newDocument);
        var values = new int[newDocument.Lines];
        var assigned = new bool[values.Length];
        var nextValue = NextValue;

        for (var oldIndex = 0; oldIndex < mapping.Length; oldIndex++) {
            var newIndex = mapping[oldIndex];
            if (newIndex < 0 || newIndex >= values.Length) {
                continue;
            }
            values[newIndex] = Values[oldIndex];
            assigned[newIndex] = true;
        }
        for (var index = 0; index < values.Length; index++) {
            if (!assigned[index]) {
                values[index] = nextValue++;
            }
        }
        return new(values, nextValue);
    }
}
