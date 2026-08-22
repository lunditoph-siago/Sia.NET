using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sia_Examples.Notebook;

internal static class CSharpLanguageOptions
{
    private static readonly IEnumerable<string> _frameworkSymbols =
        ["NET", "NET7_0_OR_GREATER", "NET8_0_OR_GREATER", "NET9_0_OR_GREATER",
            "NET10_0_OR_GREATER", "NET11_0_OR_GREATER"];

    public static CSharpParseOptions Parse { get; } = CSharpParseOptions.Default
        .WithPreprocessorSymbols(_frameworkSymbols);

    private static readonly IReadOnlyDictionary<string, ReportDiagnostic> RuntimePolicyWarningsSuppressed =
        new Dictionary<string, ReportDiagnostic> {
            ["CS1701"] = ReportDiagnostic.Suppress,
            ["CS1702"] = ReportDiagnostic.Suppress
        };

    public static CSharpCompilationOptions ConsoleApplication { get; } =
        new CSharpCompilationOptions(OutputKind.ConsoleApplication)
            .WithConcurrentBuild(false)
            .WithNullableContextOptions(NullableContextOptions.Enable)
            .WithAllowUnsafe(true)
            .WithSpecificDiagnosticOptions(RuntimePolicyWarningsSuppressed);

    public static CSharpCompilationOptions Library { get; } =
        ConsoleApplication.WithOutputKind(OutputKind.DynamicallyLinkedLibrary);
}
