using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class CSharpCompletionProvider(
    ICompilationReferenceResolver references)
{
    private readonly ICompilationReferenceResolver _references = references;
    private readonly CSharpCompilationOptions _compilationOptions =
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithConcurrentBuild(false);

    public async Task<CompletionResult> QueryAsync(
        string source,
        int position,
        CancellationToken cancellationToken = default)
    {
        position = Math.Clamp(position, 0, source.Length);
        var replacementStart = FindIdentifierStart(source, position);
        var prefix = source[replacementStart..position];
        var syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(source),
            path: "Completion.cs",
            cancellationToken: cancellationToken);
        var globalUsingsTree = CSharpSyntaxTree.ParseText(
            SourceText.From(NotebookLanguageContext.GlobalUsings),
            path: "GlobalUsings.g.cs",
            cancellationToken: cancellationToken);
        var metadataReferences = await _references.GetReferencesAsync(
            $"{NotebookLanguageContext.GlobalUsings}\n{source}",
            cancellationToken);
        var compilation = CSharpCompilation.Create(
            "Completion",
            [syntaxTree, globalUsingsTree],
            metadataReferences,
            _compilationOptions);
        var root = await syntaxTree.GetRootAsync(cancellationToken);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var symbols = GetCandidates(
            source,
            replacementStart,
            root,
            semanticModel,
            cancellationToken);

        var candidates = symbols
            .Where(static symbol => symbol.CanBeReferencedByName)
            .Select(static symbol => symbol.Name)
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(20)
            .Select(name => new CompletionCandidate(
                name,
                name,
                replacementStart,
                position))
            .ToArray();
        return candidates.Length == 0 ? CompletionResult.Empty : new(candidates);
    }

    private static IEnumerable<ISymbol> GetCandidates(
        string source,
        int replacementStart,
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (replacementStart == 0 || source[replacementStart - 1] != '.') {
            return semanticModel.LookupSymbols(replacementStart);
        }

        var dotPosition = replacementStart - 1;
        var memberAccess = root
            .FindToken(dotPosition)
            .Parent?
            .AncestorsAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .FirstOrDefault(access => access.OperatorToken.SpanStart == dotPosition);
        if (memberAccess is null) {
            return [];
        }

        var receiver = memberAccess.Expression;
        var receiverSymbol = semanticModel
            .GetSymbolInfo(receiver, cancellationToken)
            .Symbol;
        if (receiverSymbol is INamespaceSymbol namespaceSymbol) {
            return namespaceSymbol.GetMembers();
        }

        var receiverType = receiverSymbol switch {
            INamedTypeSymbol namedType => namedType,
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => semanticModel.GetTypeInfo(receiver, cancellationToken).Type,
        };
        if (receiverType is null) {
            return [];
        }

        var staticReceiver = receiverSymbol is INamedTypeSymbol;
        List<ISymbol> members = [];
        for (var type = receiverType as INamedTypeSymbol;
            type is not null;
            type = type.BaseType) {
            members.AddRange(type.GetMembers().Where(member =>
                member.IsStatic == staticReceiver
                && member.DeclaredAccessibility != Accessibility.Private));
        }
        return members;
    }

    private static int FindIdentifierStart(string source, int position)
    {
        while (position > 0 && IsIdentifierCharacter(source[position - 1])) {
            position--;
        }
        return position;
    }

    private static bool IsIdentifierCharacter(char character)
        => char.IsLetterOrDigit(character) || character == '_';
}
