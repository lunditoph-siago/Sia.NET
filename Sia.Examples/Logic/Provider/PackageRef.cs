namespace Sia.Examples.Notebook;

public readonly record struct PackageRef(
    string Id,
    string? Version,
    bool Analyzer = false);
