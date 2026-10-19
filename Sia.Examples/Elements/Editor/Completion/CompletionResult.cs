namespace Sia_Examples.Editor;

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
        var items = Items
            .Select(item => (Item: item, Rank: CompletionMatch.Rank(item.Item?.FilterText ?? item.Label.TrimStart('@'), prefix)))
            .Where(static match => match.Rank >= 0)
            .OrderByDescending(static match => match.Rank)
            .ThenByDescending(static match => match.Item.Item?.Rules.MatchPriority ?? 0)
            .ThenBy(static match => match.Item.Item?.SortText ?? match.Item.Label, StringComparer.OrdinalIgnoreCase)
            .Take(maximumItems)
            .Select(match => match.Item with { ReplaceEnd = replaceEnd })
            .ToArray();
        result = items.Length == 0 ? Empty : new(items) { Query = Query, SuggestionMode = SuggestionMode };
        return true;
    }

    internal CompletionResult Window(int start, int count)
        => new(Items.Skip(start).Take(count).ToArray()) { Query = Query, SuggestionMode = SuggestionMode };

    internal CompletionResult WithPreparedWindow(int start, int count, CompletionResult prepared)
    {
        var items = Items.ToArray();
        for (var index = start; index < Math.Min(items.Length, start + count); index++) {
            foreach (var candidate in prepared.Items) {
                if (items[index].SameIdentity(candidate)) { items[index] = candidate; break; }
            }
        }
        return this with { Items = items, SuggestionMode = prepared.SuggestionMode };
    }
}

internal static class CompletionMatch
{
    internal static int Rank(string name, string pattern)
    {
        if (pattern.Length == 0) return 0;
        if (name.Equals(pattern, StringComparison.Ordinal)) return 1200;
        if (name.StartsWith(pattern, StringComparison.Ordinal)) return 1100;
        if (name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)) return 1000;
        var matched = 0;
        for (var index = 0; index < name.Length && matched < pattern.Length; index++) {
            var boundary = index == 0 || name[index - 1] == '_' || char.IsUpper(name[index]) &&
                (char.IsLower(name[index - 1]) || index + 1 < name.Length && char.IsLower(name[index + 1]));
            if (boundary && char.ToUpperInvariant(name[index]) == char.ToUpperInvariant(pattern[matched])) matched++;
        }
        if (matched == pattern.Length) return 800;
        var substring = name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        return substring >= 0 ? 500 - substring : -1;
    }
}
