using Sia;

namespace Sia_Examples.Editor;

public struct EditorDoc(string initialText) : IEquatable<EditorDoc>
{
    private EditorDocument _value = new(initialText);
    private int _version = 1;

    public readonly EditorDocument Value => _value;
    public readonly int Version => _version;
    public readonly int LineCount => _value.LineCount;
    public readonly string FullText => _value.FullText;
    public readonly string this[int line] => _value[line];
    public readonly int LineLength(int line) => _value.LineLength(line);
    public readonly (int Line, int Col) Clamp(int line, int col) => _value.ClampPosition(line, col);

    internal void Apply(EditorDocument next)
    {
        _value = next;
        _version++;
    }

    public void Mutate(Action<EditorDocument> action)
    {
        action(_value);
        _version++;
    }

    public bool Equals(EditorDoc other) => _version == other._version;
    public override readonly int GetHashCode() => _version;
}
