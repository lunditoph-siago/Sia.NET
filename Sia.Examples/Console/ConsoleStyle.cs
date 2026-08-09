#if !BROWSER
namespace Sia_Examples.Console;

[Flags]
internal enum ConsoleDecoration
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Underline = 4,
    Reverse = 8,
    Italic = 16,
}

internal enum ConsoleColor
{
    Default,
    Red,
    Green,
    Yellow,
    Blue,
    Magenta,
    Cyan,
    Gray,
}

internal readonly record struct ConsoleStyle(
    ConsoleColor Color = ConsoleColor.Default,
    ConsoleDecoration Decoration = ConsoleDecoration.None)
{
    public ConsoleStyle With(ConsoleDecoration decoration) => this with {
        Decoration = Decoration | decoration,
    };

    public string EscapeSequence()
    {
        List<int> codes = [];
        if (Decoration.HasFlag(ConsoleDecoration.Bold)) {
            codes.Add(1);
        }
        if (Decoration.HasFlag(ConsoleDecoration.Dim)) {
            codes.Add(2);
        }
        if (Decoration.HasFlag(ConsoleDecoration.Italic)) {
            codes.Add(3);
        }
        if (Decoration.HasFlag(ConsoleDecoration.Underline)) {
            codes.Add(4);
        }
        if (Decoration.HasFlag(ConsoleDecoration.Reverse)) {
            codes.Add(7);
        }
        if (Color != ConsoleColor.Default) {
            codes.Add(Color switch {
                ConsoleColor.Red => 31,
                ConsoleColor.Green => 32,
                ConsoleColor.Yellow => 33,
                ConsoleColor.Blue => 34,
                ConsoleColor.Magenta => 35,
                ConsoleColor.Cyan => 36,
                ConsoleColor.Gray => 90,
                _ => 39,
            });
        }
        return codes.Count == 0 ? string.Empty : $"\e[{string.Join(';', codes)}m";
    }
}
#endif
