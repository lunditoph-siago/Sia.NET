using System.Runtime.CompilerServices;

namespace Sia_Examples.Editor;

public readonly record struct EditorLinesProps(
    IEditorView View,
    Text Document,
    RangeSet<Decoration> Decorations,
    EditorLineIdentities Identities,
    EditorLineUpdate? Update)
{
    public bool Equals(EditorLinesProps other)
        => ReferenceEquals(View, other.View)
            && ReferenceEquals(Document, other.Document)
            && ReferenceEquals(Decorations, other.Decorations)
            && ReferenceEquals(Identities.Values, other.Identities.Values)
            && ReferenceEquals(Update, other.Update);

    public override int GetHashCode()
        => HashCode.Combine(
            RuntimeHelpers.GetHashCode(View),
            RuntimeHelpers.GetHashCode(Document),
            RuntimeHelpers.GetHashCode(Decorations),
            RuntimeHelpers.GetHashCode(Identities.Values),
            Update is null ? 0 : RuntimeHelpers.GetHashCode(Update));
}
