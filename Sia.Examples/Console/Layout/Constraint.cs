#if !BROWSER
namespace Sia_Examples.Console.Layout;

internal enum ConstraintKind
{
    Length,
    Percentage,
    Fill,
}

internal readonly record struct Constraint(ConstraintKind Kind, int Value)
{
    public static Constraint Length(int cells) => new(ConstraintKind.Length, cells);

    public static Constraint Percentage(int percent) => new(ConstraintKind.Percentage, percent);

    public static Constraint Fill(int weight = 1) => new(ConstraintKind.Fill, weight);
}
#endif
