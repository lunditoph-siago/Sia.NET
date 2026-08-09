#if BROWSER
using Sia_Examples.Browser;
using Sia_Examples.Editor;
using Sia_Examples.Notebook;

namespace Sia_BrowserTest.Acceptance;

public sealed class BrowserInfrastructureAcceptance : IAcceptanceStage
{
    public string Name => "4. Browser main-thread composition";

    public async Task RunAsync(AcceptanceContext context)
    {
        var mainThread = BrowserMainThread.Capture();
        var resources = new BrowserResourceLoader(mainThread);
        var assemblies = new AssemblyLoader(mainThread, resources);
        var packages = new PackageReferenceLoader(resources);
        var references = new MetadataReferenceProvider(assemblies, packages);

        await context.CaseAsync(
            "runtime manifest loads framework metadata serially",
            cancellationToken => TestAssemblyManifestAsync(assemblies, cancellationToken));
        await context.CaseAsync(
            "concurrent assembly requests share one main-thread fetch",
            cancellationToken => TestConcurrentAssemblyLoadAsync(
                mainThread,
                resources,
                cancellationToken));
        await context.CaseAsync(
            "classifier stays on the browser main thread",
            cancellationToken => TestHighlighter(mainThread, cancellationToken));
        await context.CaseAsync(
            "reference provider loads the notebook dependency set",
            cancellationToken => TestReferencesAsync(references, cancellationToken),
            TimeSpan.FromSeconds(90));
        await context.CaseAsync(
            "browser completion resolves Console members",
            cancellationToken => TestCompletionAsync(references, cancellationToken),
            TimeSpan.FromSeconds(90));

        const string executionProbeKey = "Sia.BrowserTest.ExactCompilation";
        var cell = new CodeCellBlock(
            "browser",
            $"AppContext.SetData(\"{executionProbeKey}\", \"first\");\n"
                + "Console.WriteLine(7);",
            true,
            "acceptance");
        var laterCell = new CodeCellBlock(
            "browser-later",
            $"AppContext.SetData(\"{executionProbeKey}\", \"later\");\n"
                + "Console.WriteLine(9);",
            true,
            "acceptance");
        var document = new NotebookDocument(
            "Browser acceptance",
            [new(PackageSource.Framework, "System.Runtime", null)],
            [new NotebookSection("Composition", [cell, laterCell])]);
        using var session = new NotebookSession(mainThread, document, references);
        await context.CaseAsync(
            "notebook session loads packages and highlights",
            cancellationToken => TestNotebookPreparationAsync(
                session,
                cell.Id,
                cancellationToken),
            TimeSpan.FromSeconds(90));
        await context.CaseAsync(
            "notebook session compiles through the target cell",
            cancellationToken => TestNotebookCompilationAsync(
                session,
                cell.Id,
                cancellationToken),
            TimeSpan.FromSeconds(90));
        await context.CaseAsync(
            "notebook session executes the compiled cell",
            cancellationToken => TestNotebookExecutionAsync(
                session,
                cell.Id,
                cancellationToken),
            TimeSpan.FromSeconds(90));
        await context.CaseAsync(
            "exact compilation cache excludes later cells",
            cancellationToken => TestExactCompilationCacheAsync(
                session,
                cell.Id,
                laterCell.Id,
                executionProbeKey,
                cancellationToken),
            TimeSpan.FromSeconds(90));
        await context.CaseAsync(
            "notebook add-package loads an exact NuGet version",
            cancellationToken => TestNuGetAsync(
                mainThread,
                references,
                cancellationToken),
            TimeSpan.FromSeconds(90));
    }

    private static async Task TestAssemblyManifestAsync(
        AssemblyLoader assemblies,
        CancellationToken cancellationToken)
    {
        AcceptanceAssert.True(
            assemblies.KnownAssemblyNames.Contains("System.Runtime"),
            "System.Runtime is missing from the browser runtime manifest.");
        await assemblies.LoadAsync("System.Runtime", cancellationToken);
        AcceptanceAssert.True(
            assemblies.TryGetLoaded("System.Runtime", out _),
            "The loaded framework reference was not cached.");
    }

    private static async Task TestConcurrentAssemblyLoadAsync(
        BrowserMainThread mainThread,
        BrowserResourceLoader resources,
        CancellationToken cancellationToken)
    {
        var assemblies = new AssemblyLoader(mainThread, resources);
        using var firstCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var first = assemblies.LoadAsync("System.Console", firstCancellation.Token).AsTask();
        var second = assemblies.LoadAsync("System.Console", cancellationToken).AsTask();
        firstCancellation.Cancel();

        try {
            await first;
        }
        catch (OperationCanceledException) when (firstCancellation.IsCancellationRequested) {
        }

        var reference = await second;
        AcceptanceAssert.True(
            assemblies.TryGetLoaded("System.Console", out var cached)
                && ReferenceEquals(reference, cached),
            "The shared assembly load did not publish one cached reference.");
    }

    private static Task TestHighlighter(
        BrowserMainThread mainThread,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var highlights = CSharpHighlighter.Classify("var answer = 42;");
        mainThread.VerifyAccess();
        AcceptanceAssert.True(
            highlights.Any(run => run.Classification == CSharpHighlightClass.Keyword),
            "Roslyn returned no keyword classification.");
        return Task.CompletedTask;
    }

    private static async Task TestReferencesAsync(
        MetadataReferenceProvider references,
        CancellationToken cancellationToken)
    {
        var loaded = await references.GetReferencesAsync(
            "using System; using System.Collections.Generic; using Sia;",
            cancellationToken);
        AcceptanceAssert.True(loaded.Count > 0, "No notebook references were loaded.");
    }

    private static async Task TestCompletionAsync(
        MetadataReferenceProvider references,
        CancellationToken cancellationToken)
    {
        const string source = "Console.W";
        var provider = new CSharpCompletionProvider(references);
        var result = await provider.QueryAsync(source, source.Length, cancellationToken);
        AcceptanceAssert.True(
            result.Items.Any(static item => item.Label == "WriteLine"),
            "Console.WriteLine was absent from browser completion. Received: "
                + string.Join(", ", result.Items.Select(static item => item.Label)));
    }

    private static async Task TestNotebookPreparationAsync(
        NotebookSession session,
        string cellId,
        CancellationToken cancellationToken)
    {
        await session.EnsurePackagesAsync(cancellationToken);
        AcceptanceAssert.Equal(PackageLoadState.Loaded, session.Snapshot.Packages[0].State);
        AcceptanceAssert.True(
            session.GetState(cellId).Highlights.Length > 0,
            "The cell received no syntax highlights.");
    }

    private static async Task TestNotebookCompilationAsync(
        NotebookSession session,
        string cellId,
        CancellationToken cancellationToken)
    {
        await session.CompileThroughAsync(cellId, cancellationToken);
        AcceptanceAssert.Equal(CellPhase.Compiled, session.GetState(cellId).Phase);
    }

    private static async Task TestNotebookExecutionAsync(
        NotebookSession session,
        string cellId,
        CancellationToken cancellationToken)
    {
        await session.RunThroughAsync(cellId, cancellationToken);
        var state = session.GetState(cellId);
        AcceptanceAssert.Equal(CellPhase.RanSuccess, state.Phase, state.StandardError);
        AcceptanceAssert.Contains("7", state.StandardOutput);
    }

    private static async Task TestNuGetAsync(
        BrowserMainThread mainThread,
        MetadataReferenceProvider references,
        CancellationToken cancellationToken)
    {
        var package = new PackageRef(
            PackageSource.NuGet,
            "CommunityToolkit.HighPerformance",
            "8.4.2");
        var document = new NotebookDocument("NuGet acceptance", [], []);
        using var session = new NotebookSession(mainThread, document, references);
        var status = await session.AddPackageAsync(package, cancellationToken);

        AcceptanceAssert.Equal(PackageLoadState.Loaded, status.State, status.Error);
        AcceptanceAssert.Equal(1, session.Snapshot.Packages.Length);
        AcceptanceAssert.Equal(PackageLoadState.Loaded, session.Snapshot.Packages[0].State);
    }

    private static async Task TestExactCompilationCacheAsync(
        NotebookSession session,
        string firstCellId,
        string laterCellId,
        string probeKey,
        CancellationToken cancellationToken)
    {
        await session.CompileThroughAsync(laterCellId, cancellationToken);
        AppContext.SetData(probeKey, null);
        await session.RunThroughAsync(firstCellId, cancellationToken);
        AcceptanceAssert.Equal("first", AppContext.GetData(probeKey) as string);
    }
}
#endif
