using Microsoft.CodeAnalysis.CSharp;

namespace Sia_Examples.Notebook;

public static class CSharpHighlighter
{
    public static IReadOnlyList<HighlightRun> Classify(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        List<HighlightRun> result = [];

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true)) {
            if (trivia.Kind() is SyntaxKind.SingleLineCommentTrivia
                or SyntaxKind.MultiLineCommentTrivia
                or SyntaxKind.SingleLineDocumentationCommentTrivia
                or SyntaxKind.MultiLineDocumentationCommentTrivia) {
                result.Add(new(
                    trivia.SpanStart,
                    trivia.Span.Length,
                    CSharpHighlightClass.Comment));
            }
        }

        foreach (var token in root.DescendantTokens()) {
            if (token.Span.Length == 0 || Classify(token.Kind(), token.ValueText) is not { } classification) {
                continue;
            }
            result.Add(new(token.SpanStart, token.Span.Length, classification));
        }

        result.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return result;
    }

    public static string CssClass(string classification) => "tok-" + classification
        .Replace(" ", "-", StringComparison.Ordinal)
        .ToLowerInvariant();

    private static string? Classify(SyntaxKind kind, string valueText)
    {
        if (SyntaxFacts.IsKeywordKind(kind)
            || kind == SyntaxKind.IdentifierToken
                && SyntaxFacts.GetContextualKeywordKind(valueText) != SyntaxKind.None) {
            return CSharpHighlightClass.Keyword;
        }
        if (kind == SyntaxKind.NumericLiteralToken) {
            return CSharpHighlightClass.NumericLiteral;
        }
        if (kind == SyntaxKind.IdentifierToken) {
            return CSharpHighlightClass.Identifier;
        }
        if (kind == SyntaxKind.CharacterLiteralToken
            || kind.ToString().Contains("String", StringComparison.Ordinal)) {
            return CSharpHighlightClass.StringLiteral;
        }
        if (IsOperator(valueText)) {
            return CSharpHighlightClass.Operator;
        }
        return valueText.Length == 0 ? null : CSharpHighlightClass.Punctuation;
    }

    private static bool IsOperator(string text)
    {
        if (text.Length == 0) {
            return false;
        }
        foreach (var character in text) {
            if (character is not ('+' or '-' or '*' or '/' or '%'
                or '&' or '|' or '^' or '!' or '~' or '=' or '<' or '>' or '?')) {
                return false;
            }
        }
        return true;
    }
}
