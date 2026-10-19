namespace Sia.Examples.Dom;

internal readonly record struct EditorScrollPosition(
    double Top, double Left, int Line, int Column, double Offset);
