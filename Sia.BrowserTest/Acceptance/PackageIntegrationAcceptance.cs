#if !BROWSER
using Sia_Examples.Notebook;

namespace Sia_BrowserTest.Acceptance;

public sealed class PackageIntegrationAcceptance : IAcceptanceStage
{
    public string Name => "5. NuGet integration";

    public async Task RunAsync(AcceptanceContext context)
    {
        await context.CaseAsync(
            "exact package fetch extracts and caches references",
            TestExactPackageAsync,
            TimeSpan.FromSeconds(90));
    }

    private static async Task TestExactPackageAsync(CancellationToken cancellationToken)
    {
        using var resources = new HttpResourceLoader();
        var loader = new PackageReferenceLoader(resources);
        var assemblies = await loader.LoadReferencesAsync(
            "CommunityToolkit.HighPerformance",
            "8.4.2",
            cancellationToken);
        AcceptanceAssert.True(
            assemblies.Any(static assembly =>
                assembly.Name == "CommunityToolkit.HighPerformance"
                && assembly.Image.Length > 0),
            "The package's primary assembly was not extracted.");

        var cached = await loader.LoadReferencesAsync(
            "CommunityToolkit.HighPerformance",
            "8.4.2",
            cancellationToken);
        AcceptanceAssert.True(
            ReferenceEquals(assemblies, cached),
            "The exact package request bypassed the loader cache.");
    }
}
#endif
