namespace Sia_Examples.Editor;

public static class ColumnUtil
{
    public static int CountColumn(
        string text,
        int tabSize,
        int to = int.MaxValue)
    {
        var column = 0;
        for (var index = 0; index < to && index < text.Length;) {
            if (text[index] == '\t') {
                column += tabSize - column % tabSize;
                index++;
            } else {
                column++;
                index = CharUtil.FindClusterBreak(text, index);
            }
        }
        return column;
    }

    public static int FindColumn(
        string text,
        int column,
        int tabSize,
        bool strict = false)
    {
        var index = 0;
        var currentColumn = 0;
        while (true) {
            if (currentColumn >= column) {
                return index;
            }
            if (index == text.Length) {
                break;
            }
            currentColumn += text[index] == '\t'
                ? tabSize - currentColumn % tabSize
                : 1;
            index = CharUtil.FindClusterBreak(text, index);
        }
        return strict ? -1 : text.Length;
    }
}
