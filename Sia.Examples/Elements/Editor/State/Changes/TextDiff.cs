namespace Sia_Examples.Editor;

public static class TextDiff
{
    public enum Preference
    {
        None,
        End,
    }

    public static TextDifference? FindForSelection(
        string oldText,
        string newText,
        int selectionFrom,
        int selectionTo,
        Preference preference = Preference.None)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        if (oldText == newText) {
            return null;
        }

        selectionFrom = Math.Clamp(selectionFrom, 0, oldText.Length);
        selectionTo = Math.Clamp(selectionTo, selectionFrom, oldText.Length);
        if (selectionFrom < selectionTo
            && oldText.AsSpan(0, selectionFrom).SequenceEqual(
                newText.AsSpan(0, Math.Min(selectionFrom, newText.Length)))
            && selectionFrom <= newText.Length) {
            var suffixLength = oldText.Length - selectionTo;
            var insertedEnd = newText.Length - suffixLength;
            if (insertedEnd >= selectionFrom
                && newText.AsSpan(insertedEnd).SequenceEqual(oldText.AsSpan(selectionTo))) {
                return new(
                    selectionFrom,
                    selectionTo,
                    newText[selectionFrom..insertedEnd]);
            }
        }

        var preferredPosition = preference == Preference.End
            ? selectionTo
            : selectionFrom;
        return Find(oldText, newText, preferredPosition, preference);
    }

    public static TextDifference? Find(
        string oldText,
        string newText,
        int preferredPosition,
        Preference preference = Preference.None)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        if (oldText == newText) {
            return null;
        }

        var minimumLength = Math.Min(oldText.Length, newText.Length);
        var from = 0;
        while (from < minimumLength && oldText[from] == newText[from]) {
            from++;
        }

        var oldTo = oldText.Length;
        var newTo = newText.Length;
        while (oldTo > 0
            && newTo > 0
            && oldText[oldTo - 1] == newText[newTo - 1]) {
            oldTo--;
            newTo--;
        }

        preferredPosition = Math.Clamp(preferredPosition, 0, oldText.Length);
        if (preference == Preference.End) {
            var adjustment = Math.Max(0, from - Math.Min(oldTo, newTo));
            preferredPosition -= oldTo + adjustment - from;
        }
        if (oldTo < from && oldText.Length < newText.Length) {
            var move = preferredPosition <= from && preferredPosition >= oldTo
                ? from - preferredPosition
                : 0;
            from -= move;
            newTo = from + newTo - oldTo;
            oldTo = from;
        } else if (newTo < from) {
            var move = preferredPosition <= from && preferredPosition >= newTo
                ? from - preferredPosition
                : 0;
            from -= move;
            oldTo = from + oldTo - newTo;
            newTo = from;
        }

        return new(from, oldTo, newText[from..newTo]);
    }
}
