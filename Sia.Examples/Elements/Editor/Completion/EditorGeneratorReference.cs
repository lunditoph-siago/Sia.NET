using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed class EditorGeneratorReference(int version) : AnalyzerReference
{
    private readonly ImmutableArray<ISourceGenerator> _generators = GeneratorPipeline.GetGenerators();
    public override string? FullPath => null;
    public override object Id { get; } = (typeof(EditorGeneratorReference), version);
    public override string Display => "Workspace generators";
    public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => [];
    public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [];
    public override ImmutableArray<ISourceGenerator> GetGenerators(string language)
        => language == LanguageNames.CSharp ? _generators : [];
    public override ImmutableArray<ISourceGenerator> GetGeneratorsForAllLanguages() => _generators;
}
