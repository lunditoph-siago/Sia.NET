using System.Text.RegularExpressions;

namespace Sia_Examples.Editor;

public static class CharUtil
{
    private static readonly Regex _nonAsciiSingleCase = new(
        @"[ßև֐-״؀-ۿ぀-ゟ゠-ヿ㐀-䶵一-鿌가-힯]",
        RegexOptions.Compiled);

    public static int FindClusterBreak(string text, int position, bool forward = true)
    {
        if (string.IsNullOrEmpty(text)) {
            return position;
        }
        if (forward) {
            var next = position + 1;
            while (next < text.Length && char.IsLowSurrogate(text[next])) {
                next++;
            }
            return Math.Min(next, text.Length);
        }

        var previous = position - 1;
        while (previous > 0 && char.IsLowSurrogate(text[previous])) {
            previous--;
        }
        return Math.Max(previous, 0);
    }

    public static bool IsWordChar(char value)
        => char.IsLetterOrDigit(value) || value == '_';

    public static Func<string, CharCategory> MakeCategorizer(string wordChars)
        => text => Categorize(text, wordChars);

    private static CharCategory Categorize(string text, string wordChars)
    {
        if (string.IsNullOrWhiteSpace(text)) {
            return CharCategory.Space;
        }
        if (HasWordChar(text)) {
            return CharCategory.Word;
        }
        foreach (var character in wordChars) {
            if (text.Contains(character)) {
                return CharCategory.Word;
            }
        }
        return CharCategory.Other;
    }

    private static bool HasWordChar(string text)
    {
        foreach (var character in text) {
            if (IsWordChar(character)) {
                return true;
            }
            if (character > '\x80'
                && (char.ToUpperInvariant(character) != char.ToLowerInvariant(character)
                    || _nonAsciiSingleCase.IsMatch(character.ToString()))) {
                return true;
            }
        }
        return false;
    }
}
