namespace Sia_Examples.Editor;

public static class TextDiff
{
    public static (int From, int To, string Insert)? Minimal(string oldText, string newText)
    {
        if (oldText == newText) return null;

        var oldLen = oldText.Length;
        var newLen = newText.Length;

        var prefix = 0;
        var maxPrefix = Math.Min(oldLen, newLen);
        while (prefix < maxPrefix && oldText[prefix] == newText[prefix]) prefix++;

        var suffix = 0;
        var maxSuffix = Math.Min(oldLen, newLen) - prefix;
        while (suffix < maxSuffix && oldText[oldLen - 1 - suffix] == newText[newLen - 1 - suffix]) suffix++;

        var from = prefix;
        var to = oldLen - suffix;
        var insert = newText[prefix..(newLen - suffix)];
        return (from, to, insert);
    }
}
