namespace Sia_Examples.Editor;

public readonly record struct EditorSelectionView(
    int AnchorLineIndex,
    int AnchorColumn,
    int HeadLineIndex,
    int HeadColumn);
