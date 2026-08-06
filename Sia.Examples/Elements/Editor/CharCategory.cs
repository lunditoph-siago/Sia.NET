
using System.Text.RegularExpressions;

namespace Sia_Examples.Editor;

public enum CharCategory { Word, Space, Other }

public static class CharUtil
{
    public static int FindClusterBreak(string str, int pos, bool forward = true)
    {
        if (string.IsNullOrEmpty(str)) return pos;
        if (forward)
        {
            var next = pos + 1;
            while (next < str.Length && char.IsLowSurrogate(str[next])) next++;
            return next > str.Length ? str.Length : next;
        }
        else
        {
            var prev = pos - 1;
            while (prev > 0 && char.IsLowSurrogate(str[prev])) prev--;
            return prev < 0 ? 0 : prev;
        }
    }

    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static readonly Regex NonAsciiSingleCase = new(
        @"[ßև֐-״؀-ۿ぀-ゟ゠-ヿ㐀-䶵一-鿌가-힯]", RegexOptions.Compiled);

    private static bool HasWordChar(string str)
    {
        for (var i = 0; i < str.Length; i++)
        {
            var ch = str[i];
            if (char.IsLetterOrDigit(ch) || ch == '_') return true;
            if (ch > '\x80' && (char.ToUpperInvariant(ch) != char.ToLowerInvariant(ch)
                || NonAsciiSingleCase.IsMatch(ch.ToString()))) return true;
        }
        return false;
    }

    public static Func<string, CharCategory> MakeCategorizer(string wordChars)
        => ch =>
        {
            if (string.IsNullOrWhiteSpace(ch) || ch.All(char.IsWhiteSpace)) return CharCategory.Space;
            if (HasWordChar(ch)) return CharCategory.Word;
            for (var i = 0; i < wordChars.Length; i++)
                if (ch.IndexOf(wordChars[i]) > -1) return CharCategory.Word;
            return CharCategory.Other;
        };
}
