namespace Sia_Examples.Editor;

public enum MapMode { Simple, TrackDel, TrackBefore, TrackAfter }

public enum ChangeTouch { No, Yes, Cover }

public class ChangeDesc
{
    internal readonly int[] Sections;

    internal ChangeDesc(int[] sections) { Sections = sections; }

    public int Length { get { var r = 0; for (var i = 0; i < Sections.Length; i += 2) r += Sections[i]; return r; } }

    public int NewLength
    {
        get
        {
            var r = 0;
            for (var i = 0; i < Sections.Length; i += 2)
            { var ins = Sections[i + 1]; r += ins < 0 ? Sections[i] : ins; }
            return r;
        }
    }

    public bool IsEmpty => Sections.Length == 0 || (Sections.Length == 2 && Sections[1] < 0);

    public void IterGaps(Action<int, int, int> f)
    {
        for (int i = 0, posA = 0, posB = 0; i < Sections.Length;)
        {
            int len = Sections[i++], ins = Sections[i++];
            if (ins < 0) { f(posA, posB, len); posB += len; } else posB += ins;
            posA += len;
        }
    }

    public ChangeDesc InvertedDesc
    {
        get
        {
            var s = new int[Sections.Length];
            for (var i = 0; i < Sections.Length;)
            { int len = Sections[i++], ins = Sections[i++]; s[i - 2] = ins >= 0 ? ins : len; s[i - 1] = ins >= 0 ? len : ins; }
            return new ChangeDesc(s);
        }
    }

    public int? MapPos(int pos, int assoc = -1, MapMode mode = MapMode.Simple)
    {
        int posA = 0, posB = 0;
        for (var i = 0; i < Sections.Length;)
        {
            int len = Sections[i++], ins = Sections[i++], endA = posA + len;
            if (ins < 0) { if (endA > pos) return posB + (pos - posA); posB += len; }
            else
            {
                if (mode != MapMode.Simple && endA >= pos &&
                    ((mode == MapMode.TrackDel && posA < pos && endA > pos) ||
                     (mode == MapMode.TrackBefore && posA < pos) ||
                     (mode == MapMode.TrackAfter && endA > pos))) return null;
                if (endA > pos || (endA == pos && assoc < 0 && len == 0))
                    return pos == posA || assoc < 0 ? posB : posB + ins;
                posB += ins;
            }
            posA = endA;
        }
        if (pos > posA) throw new ArgumentOutOfRangeException(nameof(pos));
        return posB;
    }

    public void IterChangedRanges(Action<int, int, int, int> f, bool individual = false)
        => ChangeSet.IterChanges(this, (fromA, toA, fromB, toB, _) => f(fromA, toA, fromB, toB), individual);

    public ChangeTouch TouchesRange(int from, int to = -1)
    {
        if (to < 0) to = from;
        for (int i = 0, pos = 0; i < Sections.Length && pos <= to;)
        {
            int len = Sections[i++], ins = Sections[i++], end = pos + len;
            if (ins >= 0 && pos <= to && end >= from)
                return pos < from && end > to ? ChangeTouch.Cover : ChangeTouch.Yes;
            pos = end;
        }
        return ChangeTouch.No;
    }

    public int[] ToJSON() => Sections;
    public static ChangeDesc FromJSON(int[] json) => new(json);
}
