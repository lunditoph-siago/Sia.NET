namespace Sia_Examples.Editor;

public readonly struct EditorLineView : IEquatable<EditorLineView>
{
    public EditorLineView(
        int identity,
        int index,
        string text,
        IReadOnlyList<StyledRun>? styledRuns,
        string? className)
    {
        Identity = identity;
        Index = index;
        Text = text;
        StyledRuns = styledRuns;
        ClassName = className;
    }

    public int Identity { get; }

    public int Index { get; }

    public string Text { get; }

    public IReadOnlyList<StyledRun>? StyledRuns { get; }

    public string? ClassName { get; }

    public bool Equals(EditorLineView other)
        => Identity == other.Identity
            && Index == other.Index
            && Text == other.Text
            && ClassName == other.ClassName
            && StyledRunsEqual(StyledRuns, other.StyledRuns);

    public override bool Equals(object? obj) => obj is EditorLineView other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Identity, Index, Text, ClassName);

    private static bool StyledRunsEqual(
        IReadOnlyList<StyledRun>? left,
        IReadOnlyList<StyledRun>? right)
    {
        if (ReferenceEquals(left, right)) {
            return true;
        }
        if (left is null || right is null || left.Count != right.Count) {
            return false;
        }
        for (var index = 0; index < left.Count; index++) {
            if (left[index] != right[index]) {
                return false;
            }
        }
        return true;
    }
}
