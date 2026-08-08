using System.Collections;

namespace Sia_Examples.Editor;

internal sealed class TextCursor : IEnumerator<string>
{
    private readonly Text _text;
    private int _position;

    public TextCursor(Text text)
    {
        _text = text;
    }

    public string Current { get; private set; } = "";

    object IEnumerator.Current => Current;

    public bool MoveNext()
    {
        if (_position >= _text.Length) {
            return false;
        }
        var line = _text.LineAt(_position);
        Current = line.Text;
        _position = line.To + 1;
        return true;
    }

    public void Reset() => throw new NotSupportedException();

    public void Dispose()
    {
    }
}
