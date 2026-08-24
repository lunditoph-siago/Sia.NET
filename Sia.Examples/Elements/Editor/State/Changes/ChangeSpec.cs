namespace Sia_Examples.Editor;

public readonly record struct ChangeSpec(int From, int? To, string? Insert)
{
    public ChangeSpec(int from, int to, string insert)
        : this(from, (int?)to, insert)
    {
    }
}
