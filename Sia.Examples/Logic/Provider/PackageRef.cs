namespace Sia_Examples.Notebook;

public enum PackageSource
{
    Framework,

    NuGet,
}

public sealed record PackageRef(PackageSource Source, string Id, string? Version);

public enum PackageLoadState { Loading, Loaded, Failed }

public sealed record PackageStatus(PackageRef Package, PackageLoadState State, string? Error);
