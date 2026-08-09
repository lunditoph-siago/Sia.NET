#if !BROWSER
using Sia_Examples.Editor;
using Sia_Examples.Notebook;

namespace Sia_BrowserTest.Acceptance;

public sealed class RoslynAcceptance : IAcceptanceStage
{
    public string Name => "4. Roslyn composition";

    public async Task RunAsync(AcceptanceContext context)
    {
        await context.CaseAsync("classifier produces syntax token spans", TestHighlighterAsync);
        await context.CaseAsync("completion composes semantic model and references", TestCompletionAsync);
        await context.CaseAsync(
            "completion shares notebook global usings",
            TestCompletionGlobalUsingsAsync);
        await context.CaseAsync(
            "compiler generates and executes a Sia notebook",
            TestCompilerAsync,
            TimeSpan.FromSeconds(60));
    }

    private static Task TestHighlighterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string source = "var value = 42;";
        var highlights = CSharpHighlighter.Classify(source);
        AcceptanceAssert.True(
            highlights.Any(run => run.Start == 0
                && run.Length == 3
                && run.Classification == CSharpHighlightClass.Keyword),
            "The 'var' keyword was not classified semantically.");
        return Task.CompletedTask;
    }

    private static async Task TestCompletionAsync(CancellationToken cancellationToken)
    {
        var references = new StaticCompilationReferenceResolver();
        const string source = "using System;\nConsole.W";
        var provider = new CSharpCompletionProvider(references);
        var result = await provider.QueryAsync(source, source.Length, cancellationToken);
        AcceptanceAssert.True(
            result.Items.Any(item => item.Label == "WriteLine" && item.InsertText == "WriteLine"),
            "Console.WriteLine was not offered by Roslyn completion. Received: "
                + string.Join(", ", result.Items.Select(static item =>
                    $"{item.Label} -> {item.InsertText}")));
    }

    private static async Task TestCompletionGlobalUsingsAsync(
        CancellationToken cancellationToken)
    {
        var references = new StaticCompilationReferenceResolver();
        const string source = "Console.W";
        var provider = new CSharpCompletionProvider(references);
        var result = await provider.QueryAsync(source, source.Length, cancellationToken);
        AcceptanceAssert.True(
            result.Items.Any(static item => item.Label == "WriteLine"),
            "Notebook global usings were absent from the completion compilation.");
    }

    private static async Task TestCompilerAsync(CancellationToken cancellationToken)
    {
        const string source = """
            var entity = world.Create(HList.From(new Counter(41)));
            Console.WriteLine(entity.Get<Counter>().Value);

            public partial record struct Counter([Sia] int Value);
            """;
        var program = NotebookProgramBuilder.Build([("compiler", source)]);
        var compiler = new NotebookCompiler(
            program,
            new StaticCompilationReferenceResolver());

        var compilation = await compiler.CompileAsync(cancellationToken);
        AcceptanceAssert.True(
            compilation.Success,
            FormatDiagnostics(compilation.Diagnostics));
        var execution = await NotebookCompiler.ExecuteAsync(compilation.AssemblyImage!);
        AcceptanceAssert.True(execution.Success, execution.StdErr);
        var output = NotebookProgramBuilder.SliceOutput(execution.StdOut, program);
        AcceptanceAssert.Contains("41", output["compiler"]);
    }

    private static string FormatDiagnostics(IReadOnlyList<NotebookDiagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            $"{diagnostic.Id}: {diagnostic.Message}"));
}
#endif
