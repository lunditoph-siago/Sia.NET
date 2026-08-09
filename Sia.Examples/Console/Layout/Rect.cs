#if !BROWSER
namespace Sia_Examples.Console.Layout;

internal readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public Rect Inset(int amount) => Inset(amount, amount);

    public Rect Inset(int horizontal, int vertical)
    {
        var width = Math.Max(0, Width - horizontal * 2);
        var height = Math.Max(0, Height - vertical * 2);
        return new(X + horizontal, Y + vertical, width, height);
    }
}
#endif
