using System.Text;

namespace Sia_Examples.Editor;

public sealed class EditorDocument
{
    private readonly List<StringBuilder> _lines;
    private int _version;

    public EditorDocument(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var raw = text.Replace("\r\n", "\n").Split('\n');
        _lines = new List<StringBuilder>(raw.Length);
        foreach (var line in raw)
        {
            _lines.Add(new StringBuilder(line));
        }
        if (_lines.Count == 0)
        {
            _lines.Add(new StringBuilder());
        }
        _version = 1;
    }

    private EditorDocument(List<StringBuilder> lines, int version)
    {
        _lines = lines;
        _version = version;
    }

    public int LineCount => _lines.Count;
    public int Version => _version;

    public string this[int line]
    {
        get
        {
            if (line < 0 || line >= _lines.Count) return "";
            return _lines[line].ToString();
        }
    }

    public int LineLength(int line)
        => line >= 0 && line < _lines.Count ? _lines[line].Length : 0;

    public string FullText => string.Join('\n', _lines);

    public IReadOnlyList<string> Lines => new LineListView(this);

    public (int Line, int Col) ClampPosition(int line, int col)
    {
        var clampedLine = Math.Clamp(line, 0, _lines.Count - 1);
        var clampedCol = Math.Clamp(col, 0, _lines[clampedLine].Length);
        return (clampedLine, clampedCol);
    }

    public EditorDocument Insert(int line, int col, string text)
    {
        if (string.IsNullOrEmpty(text)) return this;

        var parts = text.Replace("\r\n", "\n").Split('\n');
        if (parts.Length == 1)
        {
            _lines[line].Insert(col, parts[0]);
            _version++;
            return this;
        }

        var rest = _lines[line].ToString(col, _lines[line].Length - col);
        _lines[line].Remove(col, _lines[line].Length - col);
        _lines[line].Append(parts[0]);

        for (var i = 1; i < parts.Length - 1; i++)
        {
            _lines.Insert(line + i, new StringBuilder(parts[i]));
        }

        var lastLine = new StringBuilder(parts[^1]);
        lastLine.Append(rest);
        _lines.Insert(line + parts.Length - 1, lastLine);
        _version++;
        return this;
    }

    public EditorDocument Delete(int line, int col, int count)
    {
        if (count <= 0) return this;

        while (count > 0)
        {
            var sb = _lines[line];
            if (col < sb.Length)
            {
                var available = sb.Length - col;
                var toRemove = Math.Min(count, available);
                sb.Remove(col, toRemove);
                count -= toRemove;
            }
            else
            {
                col = sb.Length;
                if (line + 1 >= _lines.Count) break;
                var next = _lines[line + 1];
                sb.Append(next);
                _lines.RemoveAt(line + 1);
                count--;
            }
        }
        _version++;
        return this;
    }

    public EditorDocument DeleteRange(int startLine, int startCol, int endLine, int endCol)
    {
        if (startLine == endLine)
        {
            return Delete(startLine, startCol, endCol - startCol);
        }

        _lines[startLine].Remove(startCol, _lines[startLine].Length - startCol);
        var carry = _lines[endLine].ToString(endCol, _lines[endLine].Length - endCol);
        _lines[startLine].Append(carry);

        for (var i = endLine; i > startLine; i--)
        {
            _lines.RemoveAt(i);
        }

        _version++;
        return this;
    }

    public EditorDocument SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _lines.Clear();
        var raw = text.Replace("\r\n", "\n").Split('\n');
        foreach (var line in raw)
        {
            _lines.Add(new StringBuilder(line));
        }
        if (_lines.Count == 0)
        {
            _lines.Add(new StringBuilder());
        }
        _version++;
        return this;
    }

    public EditorDocument Clone()
    {
        var copy = new List<StringBuilder>(_lines.Count);
        foreach (var line in _lines)
        {
            copy.Add(new StringBuilder(line.ToString()));
        }
        return new EditorDocument(copy, _version);
    }

    public override string ToString() => FullText;

    private sealed class LineListView(EditorDocument doc) : IReadOnlyList<string>
    {
        public string this[int index] => doc[index];
        public int Count => doc.LineCount;
        public IEnumerator<string> GetEnumerator()
        {
            for (var i = 0; i < doc.LineCount; i++)
                yield return doc[i];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
