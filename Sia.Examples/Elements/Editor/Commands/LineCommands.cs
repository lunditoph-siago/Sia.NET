using System.Buffers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public static class LineCommands
{
    private static readonly SearchValues<string> _contextStarts = SearchValues.Create(
        ["\"", "'", "/", "@", "$", "#", "<<<<<<<", "|||||||", "======="], StringComparison.Ordinal);

    public static bool InsertNewlineAndIndent(CommandTarget target)
    {
        var state = target.State;
        var ranges = state.Selection.Ranges;
        var changes = new ChangeSpec[ranges.Count];
        var selection = new SelectionRange[ranges.Count];
        var shift = 0;
        for (var index = 0; index < ranges.Count; index++) {
            var range = ranges[index];
            var nextRangeStart = index + 1 < ranges.Count ? ranges[index + 1].From : state.Doc.Length;
            var (change, caret) = PlanNewline(state.Doc, range, nextRangeStart);
            changes[index] = change;
            selection[index] = EditorSelection.Cursor(caret + shift);
            shift += (change.Insert?.Length ?? 0) - ((change.To ?? change.From) - change.From);
        }
        target.Apply(state.Apply(new() {
            Changes = changes,
            Selection = EditorSelection.Create(selection, state.Selection.MainIndex),
        }));
        return true;
    }

    private static (ChangeSpec Change, int Caret) PlanNewline(Text document, SelectionRange range, int nextRangeStart)
    {
        var line = document.LineAt(range.From);
        var prefix = line.Text.AsSpan(0, range.From - line.From);
        var indentationLength = 0;
        while (indentationLength < prefix.Length && prefix[indentationLength] is ' ' or '\t') indentationLength++;
        var indentation = prefix[..indentationLength].ToString();
        var closing = ClosingDelimiter(document, line.From, range.From, prefix);
        var innerIndentation = closing == '\0' ? indentation : indentation + new string(' ', EditorState.TabSize);
        var newline = document.NewlineAt(range.From);
        var insertion = newline + innerIndentation;
        var end = range.To;

        if (closing != '\0') {
            var rightLine = document.LineAt(end);
            var suffix = rightLine.Text.AsSpan(end - rightLine.From);
            var whitespace = 0;
            while (whitespace < suffix.Length && suffix[whitespace] is ' ' or '\t') whitespace++;
            var closingPosition = end + whitespace;
            if (whitespace < suffix.Length && suffix[whitespace] == closing && closingPosition <= nextRangeStart) {
                end = closingPosition;
                insertion += newline + indentation;
            }
        }

        return (new(range.From, end, insertion), range.From + newline.Length + innerIndentation.Length);
    }

    private static char ClosingDelimiter(Text document, int lineFrom, int position, ReadOnlySpan<char> prefix)
    {
        var trimmed = prefix.TrimEnd();
        var endsWithOpener = trimmed.Length > 0 && trimmed[^1] is '(' or '[' or '{';
        var containsOpenerAndComment = prefix.IndexOfAny('(', '[', '{') >= 0
            && (prefix.IndexOf("//", StringComparison.Ordinal) >= 0 || prefix.IndexOf("/*", StringComparison.Ordinal) >= 0);
        if (!endsWithOpener && !containsOpenerAndComment) return '\0';

        var token = ReadTokenAtCaret(document.SliceDoc(), lineFrom, position);
        var tokenOffset = 0;
        SyntaxTrivia trivia;
        if (token.IsKind(SyntaxKind.InterpolatedStringToken) && token.Span.Contains(position - 1)) {
            tokenOffset = token.SpanStart;
            var expression = SyntaxFactory.ParseExpression(token.Text, options: CSharpLanguageOptions.Parse);
            token = expression.FindToken(position - tokenOffset - 1);
            trivia = expression.FindTrivia(position - tokenOffset - 1);
        }
        else {
            trivia = token.TrailingTrivia.FirstOrDefault(item => item.FullSpan.Contains(position - 1));
        }

        var tokenStart = token.SpanStart + tokenOffset;
        if (tokenStart < lineFrom || tokenStart >= position) return '\0';
        var isBlockComment = trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
        if (isBlockComment && (position < trivia.Span.End + tokenOffset
            || !trivia.ToFullString().EndsWith("*/", StringComparison.Ordinal))) return '\0';

        return token.Kind() switch {
            SyntaxKind.OpenParenToken => ')',
            SyntaxKind.OpenBracketToken => ']',
            SyntaxKind.OpenBraceToken => '}',
            _ => '\0',
        };
    }

    private static SyntaxToken ReadTokenAtCaret(string source, int lineFrom, int position)
    {
        using var parser = SyntaxFactory.CreateTokenParser(SourceText.From(source), CSharpLanguageOptions.Parse);
        SyntaxToken token;
        var offset = 0;
        do {
            if (offset < lineFrom) {
                var marker = source.AsSpan(offset, lineFrom - offset).IndexOfAny(_contextStarts);
                var contextLineStart = marker < 0 ? lineFrom
                    : offset + source.AsSpan(offset, marker).LastIndexOfAny("\r\n\u0085\u2028\u2029") + 1;
                if (contextLineStart > offset) parser.SkipForwardTo(contextLineStart);
            }
            token = parser.ParseNextToken().Token;
            offset = token.FullSpan.End;
        } while (offset < position && !token.IsKind(SyntaxKind.EndOfFileToken));

        return token;
    }

    public static bool InsertTab(CommandTarget target)
    {
        var state = target.State;
        if (state.Selection.Ranges.Any(static range => !range.Empty)) {
            return IndentMore(target);
        }

        var (changes, selection) = ChangeHelpers.InsertPerRange(
            state.Selection.Ranges,
            static _ => new string(' ', EditorState.TabSize));
        target.Apply(state.Apply(new() {
            Changes = changes,
            Selection = selection,
        }));
        return true;
    }

    public static bool IndentLess(CommandTarget target)
    {
        var changes = VisitSelectedLines(target.State, static line => {
            if (line.Text.StartsWith('\t')) return new ChangeSpec(line.From, line.From + 1, string.Empty);
            var spaces = 0;
            while (spaces < EditorState.TabSize
                && spaces < line.Text.Length
                && line.Text[spaces] == ' ') {
                spaces++;
            }
            return spaces == 0
                ? null
                : new ChangeSpec(line.From, line.From + spaces, string.Empty);
        });
        return ApplyChanges(target, changes);
    }

    private static bool IndentMore(CommandTarget target)
    {
        var indentation = new string(' ', EditorState.TabSize);
        var changes = VisitSelectedLines(
            target.State,
            line => new ChangeSpec(line.From, line.From, indentation));
        return ApplyChanges(target, changes);
    }

    public static bool ToggleLineComment(CommandTarget target) => CommentLines(target, null);
    public static bool AddLineComment(CommandTarget target) => CommentLines(target, true);
    public static bool RemoveLineComment(CommandTarget target) => CommentLines(target, false);

    private static bool CommentLines(CommandTarget target, bool? add)
    {
        var lines = SelectedLines(target.State).Where(static line => !string.IsNullOrWhiteSpace(line.Text)).ToArray();
        var comment = add ?? !lines.All(static line => line.Text.TrimStart(' ', '\t').StartsWith("//", StringComparison.Ordinal));
        var changes = new List<ChangeSpec>();
        foreach (var line in lines) {
            var offset = 0;
            while (offset < line.Length && line.Text[offset] is ' ' or '\t') offset++;
            if (comment) changes.Add(new(line.From + offset, line.From + offset, "// "));
            else if (line.Text.AsSpan(offset).StartsWith("//", StringComparison.Ordinal)) {
                var length = offset + 2 < line.Length && line.Text[offset + 2] == ' ' ? 3 : 2;
                changes.Add(new(line.From + offset, line.From + offset + length, ""));
            }
        }
        return ApplyChanges(target, changes);
    }

    private static List<ChangeSpec> VisitSelectedLines(
        EditorState state,
        Func<Line, ChangeSpec?> createChange)
        => SelectedLines(state).Select(createChange).OfType<ChangeSpec>().ToList();

    private static IEnumerable<Line> SelectedLines(EditorState state)
    {
        var lastLineNumber = -1;
        foreach (var range in state.Selection.Ranges) {
            for (var position = range.From; position <= range.To;) {
                var line = state.Doc.LineAt(position);
                if (line.Number > lastLineNumber
                    && (range.Empty || range.To > line.From)) {
                    lastLineNumber = line.Number;
                    yield return line;
                }
                position = line.EndIncludingLineBreak;
                if (line.BreakLength == 0) break;
            }
        }
    }

    private static bool ApplyChanges(CommandTarget target, List<ChangeSpec> changes)
    {
        if (changes.Count == 0) {
            return false;
        }
        target.Apply(target.State.Apply(new() { Changes = [.. changes] }));
        return true;
    }
}
