namespace Sia_Examples.Editor;

internal static class TextMath
{
    public static (int From, int To) Clip(int length, int from, int to)
    {
        from = Math.Clamp(from, 0, length);
        return (from, Math.Clamp(to, from, length));
    }

    public static int Length(string[] lines)
    {
        var length = -1;
        foreach (var line in lines) {
            length += line.Length + 1;
        }
        return length;
    }
}
