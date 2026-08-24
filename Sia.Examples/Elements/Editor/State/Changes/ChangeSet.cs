namespace Sia_Examples.Editor;

public class ChangeSet : ChangeDesc
{
    internal ChangeSet(int[] sections, Text[] inserted)
        : base(sections)
    {
        Inserted = inserted;
    }

    internal Text[] Inserted { get; }

    public Text Apply(Text document)
    {
        if (Length != document.Length) {
            throw new ArgumentException(
                "Document length does not match the change set.",
                nameof(document));
        }
        IterateChanges(this, (oldFrom, oldTo, newFrom, _, text) =>
            document = document.Replace(
                newFrom,
                newFrom + oldTo - oldFrom,
                text));
        return document;
    }

    public static ChangeSet Of(ChangeSpec[] changes, int length)
    {
        var sections = new List<int>();
        var inserted = new List<Text>();
        var position = 0;
        ChangeSet? total = null;

        void Flush(bool force = false)
        {
            if (!force && sections.Count == 0) {
                return;
            }
            if (position < length) {
                ChangeSectionBuilder.Add(sections, length - position, -1);
            }
            var set = new ChangeSet([.. sections], [.. inserted]);
            total = total is null ? set : Compose(total, set);
            sections.Clear();
            inserted.Clear();
            position = 0;
        }

        foreach (var spec in changes) {
            var from = spec.From;
            var to = spec.To ?? from;
            if (from > to || from < 0 || to > length) {
                throw new ArgumentOutOfRangeException(nameof(changes));
            }

            var insertedText = spec.Insert is null
                ? Text.Empty
                : Text.OfString(spec.Insert);
            if (from == to && insertedText.Length == 0) {
                continue;
            }
            if (from < position) {
                Flush();
            }
            if (from > position) {
                ChangeSectionBuilder.Add(sections, from - position, -1);
            }
            ChangeSectionBuilder.Add(sections, to - from, insertedText.Length);
            ChangeSectionBuilder.AddInserted(inserted, sections, insertedText);
            position = to;
        }

        Flush(total is null);
        return total!;
    }

    public static ChangeSet Empty(int length)
        => new(length > 0 ? [length, -1] : [], []);

    internal void IterateChangedRanges(
        Action<int, int, int, int> action,
        bool individual = false)
        => IterateChanges(
            this,
            (oldFrom, oldTo, newFrom, newTo, _) =>
                action(oldFrom, oldTo, newFrom, newTo),
            individual);

    private static void IterateChanges(
        ChangeDesc description,
        Action<int, int, int, int, Text> action,
        bool individual = false)
    {
        var inserted = (description as ChangeSet)?.Inserted;
        var oldPosition = 0;
        var newPosition = 0;
        for (var index = 0; index < description.Sections.Length;) {
            var length = description.Sections[index++];
            var insertedLength = description.Sections[index++];
            if (insertedLength < 0) {
                oldPosition += length;
                newPosition += length;
                continue;
            }

            var oldEnd = oldPosition;
            var newEnd = newPosition;
            var text = Text.Empty;
            while (true) {
                oldEnd += length;
                newEnd += insertedLength;
                if (insertedLength > 0 && inserted is not null) {
                    text = text.Append(inserted[(index - 2) >> 1]);
                }
                if (individual
                    || index == description.Sections.Length
                    || description.Sections[index + 1] < 0) {
                    break;
                }
                length = description.Sections[index++];
                insertedLength = description.Sections[index++];
            }
            action(oldPosition, oldEnd, newPosition, newEnd, text);
            oldPosition = oldEnd;
            newPosition = newEnd;
        }
    }

    private static ChangeSet Compose(ChangeDesc first, ChangeDesc second)
    {
        var sections = new List<int>();
        var inserted = new List<Text>();
        var firstIterator = new ChangeSectionIterator(first);
        var secondIterator = new ChangeSectionIterator(second);
        var open = false;

        while (true) {
            if (firstIterator.Done && secondIterator.Done) {
                return new([.. sections], [.. inserted]);
            }
            if (firstIterator.InsertedLength == 0) {
                ChangeSectionBuilder.Add(
                    sections,
                    firstIterator.Length,
                    insertedLength: 0,
                    force: open);
                firstIterator.Next();
                continue;
            }
            if (secondIterator.Length == 0 && !secondIterator.Done) {
                ChangeSectionBuilder.Add(
                    sections,
                    length: 0,
                    insertedLength: secondIterator.InsertedLength,
                    force: open);
                ChangeSectionBuilder.AddInserted(
                    inserted,
                    sections,
                    secondIterator.InsertedText);
                secondIterator.Next();
                continue;
            }
            if (firstIterator.Done || secondIterator.Done) {
                throw new InvalidOperationException("Cannot compose mismatched change sets.");
            }

            var length = Math.Min(
                firstIterator.EffectiveLength,
                secondIterator.Length);
            var sectionCount = sections.Count;
            if (firstIterator.InsertedLength == -1) {
                var insertedLength = secondIterator.InsertedLength == -1
                    ? -1
                    : secondIterator.Offset != 0
                        ? 0
                        : secondIterator.InsertedLength;
                ChangeSectionBuilder.Add(sections, length, insertedLength, open);
                if (insertedLength > 0) {
                    ChangeSectionBuilder.AddInserted(
                        inserted,
                        sections,
                        secondIterator.InsertedText);
                }
            } else if (secondIterator.InsertedLength == -1) {
                ChangeSectionBuilder.Add(
                    sections,
                    firstIterator.Offset != 0 ? 0 : firstIterator.Length,
                    length,
                    open);
                ChangeSectionBuilder.AddInserted(
                    inserted,
                    sections,
                    firstIterator.SliceInsertedText(length));
            } else {
                ChangeSectionBuilder.Add(
                    sections,
                    firstIterator.Offset != 0 ? 0 : firstIterator.Length,
                    secondIterator.Offset != 0 ? 0 : secondIterator.InsertedLength,
                    open);
                if (secondIterator.Offset == 0) {
                    ChangeSectionBuilder.AddInserted(
                        inserted,
                        sections,
                        secondIterator.InsertedText);
                }
            }

            open = (firstIterator.InsertedLength > length
                    || (secondIterator.InsertedLength >= 0
                        && secondIterator.Length > length))
                && (open || sections.Count > sectionCount);
            firstIterator.ForwardEffective(length);
            secondIterator.Forward(length);
        }
    }
}
