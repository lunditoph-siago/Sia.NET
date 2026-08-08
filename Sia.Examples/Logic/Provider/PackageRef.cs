namespace Sia_Examples.Notebook;

public readonly record struct PackageRef(
    PackageSource Source,
    string Id,
    string? Version);
