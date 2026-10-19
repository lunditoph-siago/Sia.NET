
namespace Sia.Examples.Editor;

internal static class CompletionMatch
{
    internal static int Rank(string name, string pattern)
    {
        if (pattern.Length == 0) return 0;
        if (name.Equals(pattern, StringComparison.Ordinal)) return 1200;
        if (name.StartsWith(pattern, StringComparison.Ordinal)) return 1100;
        if (name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)) return 1000;
        var humps = HumpRank(name, pattern);
        if (humps > 0) return humps;
        var substring = name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (substring >= 0) return 600 - System.Math.Min(substring, 99);
        return TypoRank(name, pattern);
    }

    private static int HumpRank(string name, string pattern)
    {
        if (pattern.Length > name.Length) return 0;
        var matched = 0;
        for (var index = 0; index < name.Length && matched < pattern.Length; index++) {
            if (Equal(name[index], pattern[matched])) matched++;
        }
        if (matched < pattern.Length) return 0;
        var width = pattern.Length + 1;
        var states = width <= 256 ? stackalloc short[width * 2] : new short[width * 2];
        states.Clear();
        var adjacent = states[..width];
        var seen = states[width..];
        var best = 0;
        for (var index = 0; index < name.Length; index++) {
            var boundary = index == 0 || name[index] == '_' || !char.IsLetterOrDigit(name[index - 1])
                || char.IsDigit(name[index]) && !char.IsDigit(name[index - 1])
                || char.IsUpper(name[index]) && (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1])
                    || index + 1 < name.Length && char.IsLower(name[index + 1]));
            for (var part = pattern.Length - 1; part >= 0; part--) {
                short rank = 0;
                if (Equal(name[index], pattern[part])) {
                    rank = part == 0 && boundary ? (short)(index == 0 ? 900 : 800 - System.Math.Min(index, 99))
                        : System.Math.Max(adjacent[part], boundary ? seen[part] : (short)0);
                }
                adjacent[part + 1] = rank;
                var retained = seen[part + 1];
                if (retained > 0) retained = (short)System.Math.Max(retained > 800 ? 850 : 650, retained - 1);
                seen[part + 1] = System.Math.Max(retained, rank);
            }
            best = System.Math.Max(best, adjacent[pattern.Length]);
            if (best == 900 || pattern.Length == 1 && best > 0) return best;
        }
        return best;
    }

    private static int TypoRank(string name, string pattern)
    {
        if (pattern.Length is < 4 or > 128) return -1;
        var limit = pattern.Length >= 8 ? 2 : 1;
        var end = System.Math.Min(name.Length, pattern.Length + limit);
        var start = System.Math.Max(1, pattern.Length - limit);
        if (end < start) return -1;
        var width = end + 1;
        Span<int> states = stackalloc int[width * 3];
        states.Fill(limit + 1);
        var older = states[..width];
        var previous = states.Slice(width, width);
        var current = states[(width * 2)..];
        for (var column = 0; column <= System.Math.Min(end, limit); column++) previous[column] = column;
        for (var row = 1; row <= pattern.Length; row++) {
            current.Fill(limit + 1);
            current[0] = row;
            var minimum = limit + 1;
            for (var column = System.Math.Max(1, row - limit); column <= System.Math.Min(end, row + limit); column++) {
                var distance = System.Math.Min(previous[column] + 1, System.Math.Min(current[column - 1] + 1,
                    previous[column - 1] + (Equal(pattern[row - 1], name[column - 1]) ? 0 : 1)));
                if (row > 1 && column > 1 && Equal(pattern[row - 1], name[column - 2]) && Equal(pattern[row - 2], name[column - 1]))
                    distance = System.Math.Min(distance, older[column - 2] + 1);
                current[column] = distance;
                minimum = System.Math.Min(minimum, distance);
            }
            if (minimum > limit) return -1;
            var next = older;
            older = previous;
            previous = current;
            current = next;
        }
        var best = limit + 1;
        for (var column = start; column <= end; column++) best = System.Math.Min(best, previous[column]);
        return best <= limit ? 300 - best * 50 : -1;
    }

    private static bool Equal(char left, char right) => char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
}
