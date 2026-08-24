namespace Sia_Examples.Editor;

public readonly record struct EditorBlockInfo(int From, int Length, double Top, double Height)
{
    public int To => From + Length;

    public double Bottom => Top + Height;

    internal EditorBlockInfo Join(EditorBlockInfo other)
        => new(From, Length + other.Length, Top, Height + other.Height);
}
