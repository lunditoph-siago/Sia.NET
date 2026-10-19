using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

public sealed partial class CSharpCompletionProvider
{
    private static async Task<CompletionEdit?> GetChangeAsync(CompletionResult completion, CompletionCandidate candidate,
        string source, string newline, CancellationToken cancellationToken, char? character = null)
    {
        if (completion.Query is not { } query || candidate.Item is not { } item)
            return new([new(candidate.ReplaceStart, candidate.ReplaceEnd, candidate.InsertText)],
                candidate.ReplaceStart + candidate.InsertText.Length);
        var document = query.EditingDocument ?? query.Document;
        var edit = await query.Service.GetChangeAsync(document, item, character, cancellationToken);
        var position = edit.NewPosition ?? edit.TextChange.Span.Start + (edit.TextChange.NewText?.Length ?? 0);
        var (changes, caret) = NormalizeImportNewlines(edit.TextChanges, position, newline, cancellationToken);
        var result = query.Projection is { } projection ? projection.ToSource(query.Document.FilePath!, changes, caret, cancellationToken,
            (CompilationUnitSyntax?)await document.GetSyntaxRootAsync(cancellationToken))
            : new CompletionEdit([.. changes
            .Select(static change => new ChangeSpec(change.Span.Start, change.Span.End, change.NewText ?? ""))],
            caret);
        return result is { } change && character is { } commit
            ? change.WithCharacter(commit, source.Length, edit.IncludesCommitCharacter) : result;
    }

    private static (TextChange[] Changes, int Position) NormalizeImportNewlines(IReadOnlyList<TextChange> edits, int position,
        string newline, CancellationToken cancellation)
    {
        var changes = edits.OrderBy(static change => change.Span.Start).ToArray();
        var delta = 0;
        var caret = position;
        for (var index = 0; index < changes.Length; index++) {
            var change = changes[index];
            var text = change.NewText ?? "";
            var end = change.Span.Start + delta + text.Length;
            delta += text.Length - change.Span.Length;
            if (change.Span.Length != 0 || end > position || !text.Contains('\n') || !text.TrimStart().StartsWith("using ", StringComparison.Ordinal)) continue;
            var root = CSharpSyntaxTree.ParseText(text, CSharpLanguageOptions.Parse, cancellationToken: cancellation).GetCompilationUnitRoot(cancellation);
            if (root.ContainsDiagnostics || root.Usings.Count == 0 || root.Members.Count != 0 || root.Externs.Count != 0
                || root.DescendantTrivia().Any(static trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))) continue;
            var normalized = root.ReplaceTrivia(root.DescendantTrivia().Where(static trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)),
                (_, _) => SyntaxFactory.EndOfLine(newline)).ToFullString();
            changes[index] = new(change.Span, normalized);
            caret += normalized.Length - text.Length;
        }
        return (changes, caret);
    }

    internal static char[] CommitCharacters(CompletionItem item, CompletionRules rules)
    {
        var characters = rules.DefaultCommitCharacters.ToHashSet();
        foreach (var rule in item.Rules.CommitCharacterRules) {
            switch (rule.Kind) {
                case CharacterSetModificationKind.Replace: characters.Clear(); characters.UnionWith(rule.Characters); break;
                case CharacterSetModificationKind.Add: characters.UnionWith(rule.Characters); break;
                case CharacterSetModificationKind.Remove: characters.ExceptWith(rule.Characters); break;
            }
        }
        return characters.Where(character => !char.IsControl(character) && !Filtered(character)).Order().ToArray();

        bool Filtered(char character)
        {
            var filtered = CompletionIdentifier.IsCharacter(character);
            foreach (var rule in item.Rules.FilterCharacterRules) {
                var contains = rule.Characters.Contains(character);
                filtered = rule.Kind switch {
                    CharacterSetModificationKind.Replace => contains,
                    CharacterSetModificationKind.Add => filtered || contains,
                    CharacterSetModificationKind.Remove => filtered && !contains,
                    _ => filtered
                };
            }
            return filtered;
        }
    }

    internal static Task<CompletionResult> PrepareAsync(CompletionResult completion, string source, int position,
        CancellationToken cancellationToken)
        => PrepareAsync(completion, source, position, cancellationToken, static (_, _) => true);

    internal static Task<CompletionResult> PrepareWindowAsync(CompletionResult completion, string source, int position,
        int selectedIndex, CancellationToken cancellationToken)
        => PrepareAsync(completion, source, position, cancellationToken,
            (index, character) => index == selectedIndex && character is '.' or '(' or ';');

    private static async Task<CompletionResult> PrepareAsync(CompletionResult completion, string source, int position,
        CancellationToken cancellationToken, Func<int, char, bool> prepareCharacter)
    {
        if (completion.Query is { } context
            && (context.Projection?.ActiveSource.Source ?? (await context.Document.GetTextAsync(cancellationToken)).ToString()) != source) {
            var fresh = await context.Refresh(source, position, cancellationToken);
            completion = fresh with { Items = completion.Items.SelectMany(original => fresh.Items.Where(original.SameIdentity).Take(1)).ToArray() };
        }
        return await CSharpCompilationEngine.RunRoslynAsync(async () => {
            var newline = Text.OfString(source).PreferredNewline;
            var query = completion.Query;
            if (query is { Projection: not null, EditingDocument: null })
                query = query with { EditingDocument = await CSharpSourceProjection.PrepareEditingDocumentAsync(query.Document, cancellationToken) };
            var prepared = new List<CompletionCandidate>(completion.Items.Count);
            var rules = query?.Service.GetRules();
            for (var index = 0; index < completion.Items.Count; index++) {
                var original = completion.Items[index];
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = original;
                var context = completion with { Query = query };
                var reuse = query is not null && ReferenceEquals(candidate.PreparedQuery, query);
                var edit = reuse ? candidate.Edit : null;
                edit ??= await GetChangeAsync(context, candidate, source, newline, cancellationToken);
                if (edit is null) continue;
                Dictionary<char, CompletionEdit>? commits = null;
                var complete = true;
                if (candidate.Item is { } itemWithRules && rules is not null) {
                    commits = reuse && candidate.CommitEdits is { } previous ? new(previous) : [];
                    var characters = CommitCharacters(itemWithRules, rules);
                    complete = characters.All(character => commits.ContainsKey(character) || prepareCharacter(index, character));
                    foreach (var character in characters.Where(character => !commits.ContainsKey(character) && prepareCharacter(index, character))) {
                        if (await GetChangeAsync(context, candidate, source, newline, cancellationToken, character) is { } committed)
                            commits.Add(character, committed);
                    }
                }
                prepared.Add(candidate with { Edit = edit, CommitEdits = commits, CommitEditsComplete = complete, PreparedQuery = query });
            }
            return new CompletionResult(prepared) { Query = query, SuggestionMode = completion.SuggestionMode };
        }, cancellationToken);
    }

    internal static Task<string> GetDescriptionAsync(CompletionResult completion, CompletionCandidate candidate,
        CancellationToken cancellationToken)
        => (candidate.PreparedQuery ?? completion.Query) is { } query && candidate.Item is { } item
            ? CSharpCompilationEngine.RunRoslynAsync(async () =>
                (await query.Service.GetDescriptionAsync(query.Document, item, cancellationToken))?.Text ?? "", cancellationToken)
            : Task.FromResult("");
}
