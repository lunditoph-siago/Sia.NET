namespace Sia_Examples.Editor;

internal sealed record SymbolOccurrence(SourcePosition Location, string Symbol, bool Definition, bool Implicit, bool Candidate);
internal sealed record SymbolReferences(string Display, IReadOnlyList<SymbolOccurrence> Occurrences);
