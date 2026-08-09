namespace Sia_Examples.Editor;

public sealed record Decoration(DecorationKind Kind, string Class)
{
    public static Decoration Mark(string cssClass) => new(DecorationKind.Mark, cssClass);

    public static Decoration Line(string cssClass) => new(DecorationKind.Line, cssClass);
}
