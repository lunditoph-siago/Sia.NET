using Sia_BrowserTest.Acceptance;

namespace Sia_BrowserTest;

public static class Program
{
    public static async Task<int> Main()
    {
        var runner = new AcceptanceRunner();
        await runner.RunAsync(new EditorCoreAcceptance());
        await runner.RunAsync(new ReactiveCompositionAcceptance());
        await runner.RunAsync(new NotebookLogicAcceptance());
#if !BROWSER
        await runner.RunAsync(new RoslynAcceptance());
        await runner.RunAsync(new PackageIntegrationAcceptance());
        await runner.RunAsync(new ConsoleDomAcceptance());
        await runner.RunAsync(new ConsoleLayoutAcceptance());
        await runner.RunAsync(new ConsoleVimEditorAcceptance());
#endif
#if BROWSER
        await runner.RunAsync(new BrowserInfrastructureAcceptance());
#endif
        return runner.Report();
    }
}
