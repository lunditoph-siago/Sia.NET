namespace Sia_Examples.Editor;

public sealed record CompletionResult(IReadOnlyList<CompletionCandidate> Items)
{
    public static CompletionResult Empty { get; } = new([]);

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

        var prefix = document.SliceDoc(replaceStart, position);
        if (prefix.Any(static character => !CompletionIdentifier.IsCharacter(character))) {
            result = Empty;
            return false;
        }

        var items = Items
            .Where(item => item.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Take(maximumItems)
            .Select(item => item with { ReplaceEnd = position })
            .ToArray();
        result = items.Length == 0 ? Empty : new(items);
        return true;
    }
}
