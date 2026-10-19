namespace Sia.Examples.Editor;

public readonly record struct CompletionCandidate(
    string Label,
    string InsertText,
    int ReplaceStart,
    int ReplaceEnd)
{
    internal Microsoft.CodeAnalysis.Completion.CompletionItem? Item { get; init; }
    internal CompletionEdit? Edit { get; init; }
    internal IReadOnlyDictionary<char, CompletionEdit>? CommitEdits { get; init; }
    internal bool CommitEditsComplete { get; init; }
    internal CompletionQuery? PreparedQuery { get; init; }
    internal string DisplayLabel => Item?.InlineDescription is { Length: > 0 } detail ? $"{Label} — {detail}" : Label;

    internal bool SameIdentity(CompletionCandidate other)
    {
        if (Item is not { } item || other.Item is not { } next) return Label == other.Label && InsertText == other.InsertText;
        return item.DisplayText == next.DisplayText && item.FilterText == next.FilterText && item.SortText == next.SortText
            && item.DisplayTextPrefix == next.DisplayTextPrefix && item.DisplayTextSuffix == next.DisplayTextSuffix
            && item.InlineDescription == next.InlineDescription && item.Tags.SequenceEqual(next.Tags);
    }
}
