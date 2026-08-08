namespace Sia_Examples.Editor;

public readonly record struct EditorLinesProps(
    IEditorView View,
    Text Document,
    RangeSet<Decoration> Decorations,
    EditorLineIdentities Identities);
