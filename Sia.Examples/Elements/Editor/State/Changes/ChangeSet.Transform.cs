namespace Sia.Examples.Editor;

public partial class ChangeSet
{
    internal ChangeSpec[] ToSpecs()
    {
        List<ChangeSpec> changes = [];
        IterateChanges(this, (from, to, _, _, text) => changes.Add(new(from, to, text.SliceDoc())));
        return [..changes];
    }

    internal ChangeSet Invert(Text document)
    {
        List<ChangeSpec> changes = [];
        var length = document.Length;
        IterateChanges(this, (from, to, newFrom, newTo, _) => {
            changes.Add(new(newFrom, newTo, document.SliceDoc(from, to)));
            length += newTo - newFrom - (to - from);
        });
        return Of([..changes], length);
    }

    internal static (ChangeSet Left, ChangeSet Right) Transform(ChangeSet left, ChangeSet right)
    {
        if (left.Length != right.Length) {
            throw new ArgumentException("Concurrent changes must start from the same document length.");
        }
        var leftOps = Operations(left);
        var rightOps = Operations(right);
        List<int> leftSections = [], rightSections = [];
        List<Text> leftInserted = [], rightInserted = [];
        var leftIndex = 0;
        var rightIndex = 0;
        var leftOffset = 0;
        var rightOffset = 0;
        while (leftIndex < leftOps.Count || rightIndex < rightOps.Count) {
            if (rightIndex < rightOps.Count && rightOps[rightIndex].Insert is { } rightText) {
                ChangeSectionBuilder.Add(leftSections, rightText.Length, -1);
                ChangeSectionBuilder.Add(rightSections, 0, rightText.Length);
                ChangeSectionBuilder.AddInserted(rightInserted, rightSections, rightText);
                rightIndex++;
                continue;
            }
            if (leftIndex < leftOps.Count && leftOps[leftIndex].Insert is { } leftText) {
                ChangeSectionBuilder.Add(leftSections, 0, leftText.Length);
                ChangeSectionBuilder.AddInserted(leftInserted, leftSections, leftText);
                ChangeSectionBuilder.Add(rightSections, leftText.Length, -1);
                leftIndex++;
                continue;
            }
            if (leftIndex == leftOps.Count || rightIndex == rightOps.Count) {
                throw new InvalidOperationException("Concurrent changes consumed different documents.");
            }
            var leftOp = leftOps[leftIndex];
            var rightOp = rightOps[rightIndex];
            var length = System.Math.Min(leftOp.Length - leftOffset, rightOp.Length - rightOffset);
            if (!rightOp.Delete) {
                ChangeSectionBuilder.Add(leftSections, length, leftOp.Delete ? 0 : -1);
            }
            if (!leftOp.Delete) {
                ChangeSectionBuilder.Add(rightSections, length, rightOp.Delete ? 0 : -1);
            }
            leftOffset += length;
            rightOffset += length;
            if (leftOffset == leftOp.Length) {
                leftIndex++;
                leftOffset = 0;
            }
            if (rightOffset == rightOp.Length) {
                rightIndex++;
                rightOffset = 0;
            }
        }
        return (new([..leftSections], [..leftInserted]), new([..rightSections], [..rightInserted]));
    }

    private static List<Operation> Operations(ChangeSet changes)
    {
        List<Operation> result = [];
        for (var index = 0; index < changes.Sections.Length; index += 2) {
            var length = changes.Sections[index];
            var insertedLength = changes.Sections[index + 1];
            if (insertedLength > 0) {
                result.Add(new(insertedLength, changes.Inserted[index >> 1], false));
            }
            if (length > 0) {
                result.Add(new(length, null, insertedLength >= 0));
            }
        }
        return result;
    }

    private readonly record struct Operation(int Length, Text? Insert, bool Delete);
}
