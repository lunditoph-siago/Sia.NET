namespace Sia_Examples.Editor;

public readonly record struct EditorState(
    Text Doc,
    EditorSelection Selection,
    RangeSet<Decoration> Decorations,
    EditorLineIdentities LineIdentities,
    EditorLineUpdate? LineUpdate,
    int Version) : IEquatable<EditorState>
{
    public const int TabSize = 4;

    public bool Equals(EditorState other) => Version == other.Version;

    public override int GetHashCode() => Version;

    public string SliceDoc(int from = 0, int to = int.MaxValue) => Doc.SliceDoc(from, to);

    public Line LineAt(int position) => Doc.LineAt(position);

    public EditorState Apply(EditorUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var changes = update.Changes is { Length: > 0 }
            ? ChangeSet.Of(update.Changes, Doc.Length)
            : ChangeSet.Empty(Doc.Length);
        var document = changes.Apply(Doc);
        return new(
            document,
            update.Selection ?? Selection.Map(changes),
            update.Decorations ?? Decorations.Map(changes),
            LineIdentities.Map(changes, Doc, document),
            changes.IsEmpty ? LineUpdate : new(Doc, changes),
            Version + 1);
    }

    public EditorState WithDecorations(RangeSet<Decoration> decorations)
        => ReferenceEquals(Decorations, decorations)
            ? this
            : this with { Decorations = decorations, Version = Version + 1 };

    public static EditorState Create(string source, RangeSet<Decoration> decorations)
    {
        var document = Text.OfString(source);
        return new(
            document,
            EditorSelection.Single(0),
            decorations,
            EditorLineIdentities.Create(document.Lines),
            null,
            1);
    }
}
