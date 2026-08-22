// This component deliberately lives in the global namespace: source generators
// must build valid hint names for global-namespace types (regression for the
// '<global namespace>.Position.X.g.cs' ArgumentException in SiaPropertyGenerator).
public partial record struct GlobalNamespacePosition([Sia.Sia] float X, [Sia.Sia] float Y)
{
}
