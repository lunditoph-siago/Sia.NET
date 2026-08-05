using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class LightweightCompletionProvider(IMetadataReferenceProvider referenceProvider)
    : IEditorCompletionProvider
{
    public async Task<CompletionQueryResult> QueryAsync(
        string source, int position, CancellationToken cancellationToken = default)
    {
        position = Math.Clamp(position, 0, source.Length);

        var prefixStart = position;
        while (prefixStart > 0 && IsIdentifierChar(source[prefixStart - 1])) prefixStart--;
        var prefix = source[prefixStart..position];
        var afterDot = prefixStart > 0 && source[prefixStart - 1] == '.';

        if (prefix.Length == 0 && !afterDot) {
            return CompletionQueryResult.Empty;
        }

        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var references = await referenceProvider.GetReferencesAsync().ConfigureAwait(false);
        var compilation = CSharpCompilation.Create(
            "Completion", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);

        INamespaceOrTypeSymbol? container = null;
        if (afterDot) {
            var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            var dotToken = root.FindToken(prefixStart - 1);
            var expression = (dotToken.Parent as MemberAccessExpressionSyntax)?.Expression;
            if (expression is null) {
                return CompletionQueryResult.Empty;
            }
            var typeInfo = model.GetTypeInfo(expression, cancellationToken);
            container = typeInfo.Type ?? typeInfo.ConvertedType;
            if (container is null) {
                container = model.GetSymbolInfo(expression, cancellationToken).Symbol as INamespaceOrTypeSymbol;
            }
            if (container is null) {
                return CompletionQueryResult.Empty;
            }
        }

        var symbols = model.LookupSymbols(position, container, includeReducedExtensionMethods: true);

        var ranked = new List<(ISymbol Symbol, int Tier)>();
        foreach (var s in symbols) {
            if (s.IsImplicitlyDeclared || string.IsNullOrEmpty(s.Name)) continue;
            var tier = MatchTier(s.Name, prefix);
            if (tier is { } t) ranked.Add((s, t));
        }

        var items = ranked
            .GroupBy(c => c.Symbol.Name, StringComparer.Ordinal)
            .Select(g => g.OrderBy(c => c.Tier).ThenBy(c => KindRank(c.Symbol)).First())
            .OrderBy(c => c.Tier)
            .ThenBy(c => KindRank(c.Symbol))
            .ThenBy(c => c.Symbol.Name, StringComparer.Ordinal)
            .Take(20)
            .Select(c => new CompletionCandidate(c.Symbol.Name, c.Symbol.Name, prefixStart, position))
            .ToList();

        return new CompletionQueryResult(items);
    }

    private static int? MatchTier(string name, string prefix)
    {
        if (prefix.Length == 0) return 0;
        if (name.StartsWith(prefix, StringComparison.Ordinal)) return 0;
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 1;
        return IsSubsequenceMatch(name, prefix) ? 2 : null;
    }

    private static bool IsSubsequenceMatch(string name, string pattern)
    {
        var ni = 0;
        foreach (var pc in pattern) {
            var matched = false;
            while (ni < name.Length) {
                if (char.ToLowerInvariant(name[ni]) == char.ToLowerInvariant(pc)) {
                    matched = true;
                    ni++;
                    break;
                }
                ni++;
            }
            if (!matched) return false;
        }
        return true;
    }

    private static int KindRank(ISymbol symbol) => symbol.Kind switch
    {
        SymbolKind.Local or SymbolKind.Parameter or SymbolKind.RangeVariable => 0,
        SymbolKind.Field or SymbolKind.Property or SymbolKind.Event => 1,
        SymbolKind.Method => 2,
        SymbolKind.NamedType => 3,
        SymbolKind.Namespace => 4,
        _ => 3,
    };

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
