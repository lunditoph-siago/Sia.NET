using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public static class EditorTokenizer
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    public static List<HighlightRun> Tokenize(string source)
    {
        var runs = new List<HighlightRun>();
        foreach (var token in SyntaxFactory.ParseTokens(source, options: ParseOptions))
        {
            foreach (var trivia in token.LeadingTrivia)
                AddTrivia(runs, trivia);
            foreach (var trivia in token.TrailingTrivia)
                AddTrivia(runs, trivia);

            var span = token.Span;
            if (span.Length == 0) continue;

            var kind = ClassifyToken(token);
            if (kind == null) continue;

            runs.Add(new HighlightRun(span.Start, span.Length, kind));
        }

        return runs;
    }

    private static string? ClassifyToken(SyntaxToken token)
    {
        if (token.IsKeyword())
        {
            return token.Kind() switch
            {
                SyntaxKind.PragmaKeyword or SyntaxKind.WarningKeyword or SyntaxKind.ChecksumKeyword
                    or SyntaxKind.ReferenceKeyword or SyntaxKind.DefineKeyword or SyntaxKind.UndefKeyword
                    or SyntaxKind.IfKeyword or SyntaxKind.ElseKeyword or SyntaxKind.ElifKeyword
                    or SyntaxKind.EndIfKeyword or SyntaxKind.RegionKeyword or SyntaxKind.EndRegionKeyword
                    or SyntaxKind.ErrorKeyword or SyntaxKind.LineKeyword => "preprocessor keyword",
                SyntaxKind.ReturnKeyword or SyntaxKind.BreakKeyword or SyntaxKind.ContinueKeyword
                    or SyntaxKind.GotoKeyword or SyntaxKind.YieldKeyword => "control keyword",
                _ => "keyword",
            };
        }

        return token.Kind() switch
        {
            SyntaxKind.StringLiteralToken or SyntaxKind.Utf8StringLiteralToken
                or SyntaxKind.InterpolatedStringToken or SyntaxKind.InterpolatedStringTextToken
                or SyntaxKind.MultiLineRawStringLiteralToken or SyntaxKind.SingleLineRawStringLiteralToken
                or SyntaxKind.Utf8MultiLineRawStringLiteralToken or SyntaxKind.Utf8SingleLineRawStringLiteralToken
                => "string literal",
            SyntaxKind.CharacterLiteralToken => "string literal",
            SyntaxKind.InterpolatedStringStartToken or SyntaxKind.InterpolatedStringEndToken
                or SyntaxKind.InterpolatedVerbatimStringStartToken => "string literal",
            SyntaxKind.NumericLiteralToken => "numeric literal",
            SyntaxKind.IdentifierToken => null,
            _ => null,
        };
    }

    private static void AddTrivia(List<HighlightRun> runs, SyntaxTrivia trivia)
    {
        var kind = trivia.Kind() switch
        {
            SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
                or SyntaxKind.SingleLineDocumentationCommentTrivia
                or SyntaxKind.MultiLineDocumentationCommentTrivia => "comment",
            _ => null,
        };
        if (kind != null && trivia.Span.Length > 0)
            runs.Add(new HighlightRun(trivia.Span.Start, trivia.Span.Length, kind));
    }
}
