namespace Sia_Examples.Editor;

public static class ColumnUtil
{
    public static int CountColumn(string str, int tabSize, int to = int.MaxValue)
    {
        var n = 0;
        for (var i = 0; i < to && i < str.Length;)
        {
            if (str[i] == '\t') { n += tabSize - (n % tabSize); i++; }
            else { n++; i = CharUtil.FindClusterBreak(str, i); }
        }
        return n;
    }

    public static int FindColumn(string str, int col, int tabSize, bool strict = false)
    {
        for (int i = 0, n = 0; ;)
        {
            if (n >= col) return i;
            if (i == str.Length) break;
            n += str[i] == '\t' ? tabSize - (n % tabSize) : 1;
            i = CharUtil.FindClusterBreak(str, i);
        }
        return strict ? -1 : str.Length;
    }
}
