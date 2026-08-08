namespace Sia_Examples.Notebook;

public readonly record struct PackageStatus(
    PackageRef Package,
    PackageLoadState State,
    string? Error);
