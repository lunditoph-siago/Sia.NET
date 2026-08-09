namespace Sia_Examples.Editor;

public sealed class EditorUpdate
{
    public ChangeSpec[]? Changes { get; init; }

    public EditorSelection? Selection { get; init; }

    public RangeSet<Decoration>? Decorations { get; init; }
}
