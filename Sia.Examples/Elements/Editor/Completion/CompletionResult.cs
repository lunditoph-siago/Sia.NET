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
        for (var index = start; index < Math.Min(items.Length, start + count); index++) {
            foreach (var candidate in prepared.Items) {
                if (items[index].SameIdentity(candidate)) { items[index] = candidate; break; }
            }
        }
        return this with { Items = items, SuggestionMode = SuggestionMode || prepared.SuggestionMode };
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
        if (MatchesHumps(name, pattern)) return 800;
        var substring = name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (substring >= 0) return 600 - Math.Min(substring, 99);
        return TypoRank(name, pattern);
    }

    private static bool MatchesHumps(string name, string pattern)
    {
        if (pattern.Length > name.Length) return false;
        var width = pattern.Length + 1;
        Span<byte> states = width <= 256 ? stackalloc byte[width * 2] : new byte[width * 2];
        states.Clear();
        var adjacent = states[..width];
        var seen = states[width..];
        seen[0] = 1;
        for (var index = 0; index < name.Length; index++) {
            var boundary = index == 0 || name[index] == '_' || !char.IsLetterOrDigit(name[index - 1])
                || char.IsDigit(name[index]) && !char.IsDigit(name[index - 1])
                || char.IsUpper(name[index]) && (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1])
                    || index + 1 < name.Length && char.IsLower(name[index + 1]));
            for (var part = pattern.Length - 1; part >= 0; part--) {
                adjacent[part + 1] = (byte)(Equal(name[index], pattern[part])
                    && (adjacent[part] != 0 || boundary && seen[part] != 0) ? 1 : 0);
                seen[part + 1] |= adjacent[part + 1];
            }
            if (adjacent[pattern.Length] != 0) return true;
        }
        return false;
    }

    private static int TypoRank(string name, string pattern)
    {
        if (pattern.Length is < 4 or > 128) return -1;
        var limit = pattern.Length >= 8 ? 2 : 1;
        var end = Math.Min(name.Length, pattern.Length + limit);
        var start = Math.Max(1, pattern.Length - limit);
        if (end < start) return -1;
        var width = end + 1;
        Span<int> states = stackalloc int[width * 3];
        states.Fill(limit + 1);
        var older = states[..width];
        var previous = states.Slice(width, width);
        var current = states[(width * 2)..];
        for (var column = 0; column <= Math.Min(end, limit); column++) previous[column] = column;
        for (var row = 1; row <= pattern.Length; row++) {
            current.Fill(limit + 1);
            current[0] = row;
            var minimum = limit + 1;
            for (var column = Math.Max(1, row - limit); column <= Math.Min(end, row + limit); column++) {
                var distance = Math.Min(previous[column] + 1, Math.Min(current[column - 1] + 1,
                    previous[column - 1] + (Equal(pattern[row - 1], name[column - 1]) ? 0 : 1)));
                if (row > 1 && column > 1 && Equal(pattern[row - 1], name[column - 2]) && Equal(pattern[row - 2], name[column - 1]))
                    distance = Math.Min(distance, older[column - 2] + 1);
                current[column] = distance;
                minimum = Math.Min(minimum, distance);
            }
            if (minimum > limit) return -1;
            var next = older;
            older = previous;
            previous = current;
            current = next;
        }
        var best = limit + 1;
        for (var column = start; column <= end; column++) best = Math.Min(best, previous[column]);
        return best <= limit ? 300 - best * 50 : -1;
    }

    private static bool Equal(char left, char right) => char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
}
