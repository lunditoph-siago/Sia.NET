namespace Sia_Examples.Editor;

internal sealed class ChangeSectionIterator
{
    private readonly ChangeDesc _set;
    private int _index;

    public ChangeSectionIterator(ChangeDesc set)
    {
        _set = set;
        Next();
    }

    public int Length { get; private set; }

    public int Offset { get; private set; }

    public int InsertedLength { get; private set; }

    public bool Done => InsertedLength == -2;

    public int EffectiveLength => InsertedLength < 0 ? Length : InsertedLength;

    public Text InsertedText {
        get {
            var inserted = (_set as ChangeSet)?.Inserted;
            if (inserted is null) {
                return Text.Empty;
            }
            var index = (_index - 2) >> 1;
            return index >= inserted.Length ? Text.Empty : inserted[index];
        }
    }

    public void Next()
    {
        var sections = _set.Sections;
        if (_index < sections.Length) {
            Length = sections[_index++];
            InsertedLength = sections[_index++];
        } else {
            Length = 0;
            InsertedLength = -2;
        }
        Offset = 0;
    }

    public Text SliceInsertedText(int length = 0)
    {
        var inserted = (_set as ChangeSet)?.Inserted;
        if (inserted is null) {
            return Text.Empty;
        }
        var index = (_index - 2) >> 1;
        if (index >= inserted.Length && length == 0) {
            return Text.Empty;
        }
        return inserted[index].Slice(
            Offset,
            length == 0 ? int.MaxValue : Offset + length);
    }

    public void Forward(int length)
    {
        if (length == Length) {
            Next();
        } else {
            Length -= length;
            Offset += length;
        }
    }

    public void ForwardEffective(int length)
    {
        if (InsertedLength == -1) {
            Forward(length);
        } else if (length == InsertedLength) {
            Next();
        } else {
            InsertedLength -= length;
            Offset += length;
        }
    }
}
