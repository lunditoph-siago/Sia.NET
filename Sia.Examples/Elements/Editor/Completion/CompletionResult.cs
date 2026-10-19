namespace Sia.Examples.Editor;

public sealed record CompletionResult(IReadOnlyList<CompletionCandidate> Items)
{
    public static CompletionResult Empty { get; } = new([]);

    internal CompletionQuery? Query { get; init; }
    internal bool SuggestionMode { get; init; }

    public bool HasItems => Items.Count > 0;

    public bool TryFilter(
        Text document,
        int position,
        int maximumItems,
        out CompletionResult result)
    {
        if (!HasItems || maximumItems <= 0) {
            result = Empty;
            return false;
        }

        var replaceStart = Items[0].ReplaceStart;
        if (position < replaceStart
            || position > document.Length
            || Items.Any(item => item.ReplaceStart != replaceStart)) {
            result = Empty;
            return false;
        }

        var prefix = document.SliceDoc(replaceStart, position).TrimStart('@');
        if (prefix.Any(static character => !CompletionIdentifier.IsCharacter(character))) {
            result = Empty;
            return false;
        }

        var replaceEnd = position;
        while (replaceEnd < document.Length && CompletionIdentifier.IsCharacter(document.SliceDoc(replaceEnd, replaceEnd + 1)[0])) replaceEnd++;
        var matches = Items
            .Select(item => (Item: item, Rank: CompletionMatch.Rank((item.Item?.FilterText ?? item.Label).TrimStart('@'), prefix)))
            .Where(static match => match.Rank >= 0)
            .OrderByDescending(static match => match.Rank)
            .ThenByDescending(static match => match.Item.Item?.Rules.MatchPriority ?? 0)
            .ThenBy(static match => match.Item.Item?.SortText ?? match.Item.Label, StringComparer.OrdinalIgnoreCase)
            .Take(maximumItems)
            .ToArray();
        result = matches.Length == 0 ? Empty : new(matches.Select(match => match.Item with { ReplaceEnd = replaceEnd }).ToArray()) {
            Query = Query,
            SuggestionMode = SuggestionMode || prefix.Length > 0 && matches[0].Rank < 500
        };
        return true;
    }

    internal CompletionResult Window(int start, int count)
        => new(Items.Skip(start).Take(count).ToArray()) { Query = Query, SuggestionMode = SuggestionMode };

    internal CompletionResult WithPreparedWindow(int start, int count, CompletionResult prepared)
    {
        var items = Items.ToArray();
        for (var index = start; index < System.Math.Min(items.Length, start + count); index++) {
            foreach (var candidate in prepared.Items) {
                if (items[index].SameIdentity(candidate)) { items[index] = candidate; break; }
            }
        }
        return this with {
            Items = items,
            Query = Query is not null && ReferenceEquals(Query.Document, prepared.Query?.Document) ? prepared.Query : Query,
            SuggestionMode = SuggestionMode || prepared.SuggestionMode
        };
    }
}
