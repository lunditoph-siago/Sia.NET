using System.Globalization;
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
        position = Math.Clamp(position, 0, text.Length);
        if (forward) {
            if (position + 1 < text.Length && text[position] <= '\x7f' && text[position + 1] <= '\x7f') {
                return position + (text[position] == '\r' && text[position + 1] == '\n' ? 2 : 1);
            }
            return position + StringInfo.GetNextTextElementLength(text.AsSpan(position));
        }
        if (position == 0) {
            return 0;
        }

        var previous = position - 1;
        while (previous > 0) {
            if (text[previous - 1] <= '\x7f' && text[previous] <= '\x7f'
                && !(text[previous - 1] == '\r' && text[previous] == '\n')) {
                break;
            }
            previous--;
        }
        for (var next = previous; next < position;) {
            previous = next;
            next += StringInfo.GetNextTextElementLength(text.AsSpan(next));
        }
        return previous;
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
