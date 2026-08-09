namespace Sia_Examples.Editor;

internal static class ChangeSectionBuilder
{
    public static void Add(
        List<int> sections,
        int length,
        int insertedLength,
        bool force = false)
    {
        if (length == 0 && insertedLength <= 0) {
            return;
        }

        var last = sections.Count - 2;
        if (last >= 0
            && insertedLength <= 0
            && insertedLength == sections[last + 1]) {
            sections[last] += length;
        } else if (last >= 0 && length == 0 && sections[last] == 0) {
            sections[last + 1] += insertedLength;
        } else if (force) {
            sections[last] += length;
            sections[last + 1] += insertedLength;
        } else {
            sections.Add(length);
            sections.Add(insertedLength);
        }
    }

    public static void AddInserted(
        List<Text> inserted,
        List<int> sections,
        Text value)
    {
        if (value.Length == 0) {
            return;
        }

        var index = (sections.Count - 2) >> 1;
        if (index < inserted.Count) {
            inserted[^1] = inserted[^1].Append(value);
            return;
        }
        while (inserted.Count < index) {
            inserted.Add(Text.Empty);
        }
        inserted.Add(value);
    }
}
