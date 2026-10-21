namespace Sia.Examples.Notebook;

public sealed record FetchedAssembly(string Name, byte[] Image, bool Analyzer = false);

public sealed record FetchedPackage(string Version, IReadOnlyList<FetchedAssembly> Assemblies);
