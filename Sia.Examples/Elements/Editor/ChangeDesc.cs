namespace Sia_Examples.Editor;

public class ChangeDesc
{
    internal ChangeDesc(int[] sections)
    {
        Sections = sections;
    }

    internal int[] Sections { get; }

    public int Length {
        get {
            var length = 0;
            for (var index = 0; index < Sections.Length; index += 2) {
                length += Sections[index];
            }
            return length;
        }
    }

    public bool IsEmpty
        => Sections.Length == 0
            || (Sections.Length == 2 && Sections[1] < 0);

    public int? MapPos(int position, int assoc = -1, MapMode mode = MapMode.Simple)
    {
        var oldPosition = 0;
        var newPosition = 0;
        for (var index = 0; index < Sections.Length;) {
            var length = Sections[index++];
            var insertedLength = Sections[index++];
            var oldEnd = oldPosition + length;
            if (insertedLength < 0) {
                if (oldEnd > position) {
                    return newPosition + position - oldPosition;
                }
                newPosition += length;
            } else {
                if (IsTrackedDeletion(position, mode, oldPosition, oldEnd)) {
                    return null;
                }
                if (oldEnd > position
                    || (oldEnd == position && assoc < 0 && length == 0)) {
                    return position == oldPosition || assoc < 0
                        ? newPosition
                        : newPosition + insertedLength;
                }
                newPosition += insertedLength;
            }
            oldPosition = oldEnd;
        }

        if (position > oldPosition) {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
        return newPosition;
    }

    private static bool IsTrackedDeletion(
        int position,
        MapMode mode,
        int oldStart,
        int oldEnd)
        => mode != MapMode.Simple
            && oldEnd >= position
            && mode switch {
                MapMode.TrackDel => oldStart < position && oldEnd > position,
                MapMode.TrackBefore => oldStart < position,
                MapMode.TrackAfter => oldEnd > position,
                _ => false,
            };
}
