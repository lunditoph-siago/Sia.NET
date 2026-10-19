using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Sia_Examples.Editor;

internal sealed record EditorSearchMatch(int From, int To, string Replacement);

internal sealed record EditorSearchResult(IReadOnlyList<EditorSearchMatch> Matches, string? Error = null)
{
    internal static EditorSearchResult Find(string source, string query, string replacement,
        bool matchCase = false, bool wholeWord = false, bool regularExpression = false)
    {
        if (query.Length == 0) return new([]);
        var matches = new List<EditorSearchMatch>();
        var started = Stopwatch.GetTimestamp();
        try {
            if (regularExpression) {
                var options = RegexOptions.CultureInvariant | RegexOptions.Multiline;
                if (!matchCase) options |= RegexOptions.IgnoreCase;
                var regex = new Regex(query, options, TimeSpan.FromMilliseconds(100));
                for (var match = regex.Match(source); match.Success; match = match.NextMatch()) {
                    if (LimitReached()) return new([], "Search is too large or too expensive. Narrow the query.");
                    if (wholeWord && !IsWholeWord(source, match.Index, match.Length)) continue;
                    matches.Add(new(match.Index, match.Index + match.Length, match.Result(replacement)));
                    if (LimitReached()) return new([], "Search is too large or too expensive. Narrow the query.");
                }
            }
            else {
                var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                for (var position = 0; position <= source.Length - query.Length;) {
                    if (LimitReached()) return new([], "Search is too large or too expensive. Narrow the query.");
                    var index = source.IndexOf(query, position, comparison);
                    if (index < 0) break;
                    position = index + query.Length;
                    if (wholeWord && !IsWholeWord(source, index, query.Length)) continue;
                    matches.Add(new(index, position, replacement));
                    if (LimitReached()) return new([], "Search is too large or too expensive. Narrow the query.");
                }
            }
            return new(matches);
        }
        catch (RegexMatchTimeoutException) { return new([], "Search timed out. Simplify the regular expression."); }
        catch (RegexParseException error) { return new([], $"Invalid regular expression near character {error.Offset + 1}."); }
        catch (ArgumentException) { return new([], "Invalid search or replacement expression."); }

        bool LimitReached() => matches.Count > 20_000 || Stopwatch.GetElapsedTime(started).TotalMilliseconds > 250;
    }

    private static bool IsWholeWord(string source, int start, int length)
        => (start == 0 || !IsWord(source[start - 1])) && (start + length == source.Length || !IsWord(source[start + length]));

    private static bool IsWord(char value)
        => char.IsLetterOrDigit(value) || char.IsSurrogate(value) || value == '_'
            || char.GetUnicodeCategory(value) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.ConnectorPunctuation;
}
