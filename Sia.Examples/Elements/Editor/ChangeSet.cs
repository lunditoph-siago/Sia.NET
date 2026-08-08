namespace Sia_Examples.Editor;

public readonly record struct ChangeSpec(int From, int? To, string? Insert)
{
    public ChangeSpec(int from, int to, string insert) : this(from, (int?)to, insert) { }
}

public class ChangeSet : ChangeDesc
{
    internal readonly Text[] Inserted;

    internal ChangeSet(int[] sections, Text[] inserted) : base(sections) { Inserted = inserted; }

    public Text Apply(Text doc)
    {
        if (Length != doc.Length) throw new ArgumentException("Wrong document length for changeset");
        IterChanges(this, (fromA, toA, fromB, toB, text) =>
            doc = doc.Replace(fromB, fromB + (toA - fromA), text), false);
        return doc;
    }

    public ChangeSet Invert(Text doc)
    {
        var sections = (int[])Sections.Clone();
        var inserted = new List<Text>();
        for (int i = 0, pos = 0; i < sections.Length; i += 2) {
            int len = sections[i], ins = sections[i + 1];
            if (ins >= 0) {
                sections[i] = ins; sections[i + 1] = len;
                while (inserted.Count < (i >> 1)) inserted.Add(Text.Empty);
                inserted.Add(len > 0 ? doc.Slice(pos, pos + len) : Text.Empty);
            }
            pos += len;
        }
        return new ChangeSet(sections, [.. inserted]);
    }

    public ChangeSet Compose(ChangeSet other)
        => IsEmpty ? other : other.IsEmpty ? this : ComposeImpl(this, other, true);

    public ChangeDesc Desc => new(Sections);

    public (ChangeSet Changes, ChangeDesc Filtered) Filter(int[] ranges)
    {
        var rs = new List<int>(); var ri = new List<Text>(); var fs = new List<int>();
        var iter = new SectionIter(this);
        for (int i = 0, pos = 0; ;) {
            var next = i == ranges.Length ? int.MaxValue : ranges[i++];
            while (pos < next || (pos == next && iter.Len == 0)) {
                if (iter.Done) goto done;
                var len = Math.Min(iter.Len, next - pos);
                SecHelp.Add(fs, len, -1);
                var ins = iter.Ins == -1 ? -1 : iter.Off == 0 ? iter.Ins : 0;
                SecHelp.Add(rs, len, ins);
                if (ins > 0) SecHelp.AddInsert(ri, rs, iter.Text);
                iter.Forward(len); pos += len;
            }
            var end = ranges[i++];
            while (pos < end) {
                if (iter.Done) goto done;
                var len = Math.Min(iter.Len, end - pos);
                SecHelp.Add(rs, len, -1);
                SecHelp.Add(fs, len, iter.Ins == -1 ? -1 : iter.Off == 0 ? iter.Ins : 0);
                iter.Forward(len); pos += len;
            }
        }
    done:
        return (new ChangeSet([.. rs], [.. ri]), new ChangeDesc([.. fs]));
    }

    public static ChangeSet Of(ChangeSpec[] changes, int length, string? lineSep = null)
    {
        var sections = new List<int>(); var inserted = new List<Text>();
        var pos = 0; ChangeSet? total = null;
        void Flush(bool force = false)
        {
            if (!force && sections.Count == 0) return;
            if (pos < length) SecHelp.Add(sections, length - pos, -1);
            var set = new ChangeSet([.. sections], [.. inserted]);
            total = total == null ? set : total.Compose(set);
            sections.Clear(); inserted.Clear(); pos = 0;
        }
        foreach (var spec in changes) {
            int from = spec.From, to = spec.To ?? from;
            if (from > to || from < 0 || to > length) throw new ArgumentOutOfRangeException();
            var insText = spec.Insert == null ? Text.Empty : Text.OfString(spec.Insert);
            var insLen = insText.Length;
            if (from == to && insLen == 0) continue;
            if (from < pos) Flush();
            if (from > pos) SecHelp.Add(sections, from - pos, -1);
            SecHelp.Add(sections, to - from, insLen);
            SecHelp.AddInsert(inserted, sections, insText);
            pos = to;
        }
        Flush(total == null);
        return total!;
    }

    public static ChangeSet Empty(int length) => new(length > 0 ? [length, -1] : [], []);

    internal static void IterChanges(ChangeDesc desc, Action<int, int, int, int, Text> f, bool individual)
    {
        var inserted = (desc as ChangeSet)?.Inserted;
        for (int posA = 0, posB = 0, i = 0; i < desc.Sections.Length;) {
            int len = desc.Sections[i++], ins = desc.Sections[i++];
            if (ins < 0) { posA += len; posB += len; } else {
                int endA = posA, endB = posB; var text = Text.Empty;
                while (true) {
                    endA += len; endB += ins;
                    if (ins > 0 && inserted != null) text = text.Append(inserted[(i - 2) >> 1]);
                    if (individual || i == desc.Sections.Length || desc.Sections[i + 1] < 0) break;
                    len = desc.Sections[i++]; ins = desc.Sections[i++];
                }
                f(posA, endA, posB, endB, text);
                posA = endA; posB = endB;
            }
        }
    }

    internal static ChangeSet ComposeImpl(ChangeDesc a, ChangeDesc b, bool mkSet)
    {
        var sections = new List<int>(); var insert = mkSet ? new List<Text>() : null;
        var ai = new SectionIter(a); var bi = new SectionIter(b);
        for (var open = false; ;) {
            if (ai.Done && bi.Done) return new ChangeSet([.. sections], insert?.ToArray() ?? []);
            if (ai.Ins == 0) { SecHelp.Add(sections, ai.Len, 0, open); ai.Next(); } else if (bi.Len == 0 && !bi.Done) { SecHelp.Add(sections, 0, bi.Ins, open); if (insert != null) SecHelp.AddInsert(insert, sections, bi.Text); bi.Next(); } else if (ai.Done || bi.Done) throw new InvalidOperationException("Mismatched lengths");
            else {
                int len = Math.Min(ai.Len2, bi.Len), sl = sections.Count;
                if (ai.Ins == -1) { var ib = bi.Ins == -1 ? -1 : bi.Off != 0 ? 0 : bi.Ins; SecHelp.Add(sections, len, ib, open); if (insert != null && ib > 0) SecHelp.AddInsert(insert, sections, bi.Text); } else if (bi.Ins == -1) { SecHelp.Add(sections, ai.Off != 0 ? 0 : ai.Len, len, open); if (insert != null) SecHelp.AddInsert(insert, sections, ai.TextBit(len)); } else { SecHelp.Add(sections, ai.Off != 0 ? 0 : ai.Len, bi.Off != 0 ? 0 : bi.Ins, open); if (insert != null && bi.Off == 0) SecHelp.AddInsert(insert, sections, bi.Text); }
                open = (ai.Ins > len || (bi.Ins >= 0 && bi.Len > len)) && (open || sections.Count > sl);
                ai.Forward2(len); bi.Forward(len);
            }
        }
    }
}

internal static class SecHelp
{
    public static void Add(List<int> s, int len, int ins, bool force = false)
    {
        if (len == 0 && ins <= 0) return;
        var last = s.Count - 2;
        if (last >= 0 && ins <= 0 && ins == s[last + 1]) s[last] += len;
        else if (last >= 0 && len == 0 && s[last] == 0) s[last + 1] += ins;
        else if (force) { s[last] += len; s[last + 1] += ins; } else { s.Add(len); s.Add(ins); }
    }

    public static void AddInsert(List<Text> v, List<int> s, Text val)
    {
        if (val.Length == 0) return;
        var idx = (s.Count - 2) >> 1;
        if (idx < v.Count) v[v.Count - 1] = v[v.Count - 1].Append(val);
        else { while (v.Count < idx) v.Add(Text.Empty); v.Add(val); }
    }
}

internal sealed class SectionIter
{
    public int I, Len, Off, Ins;
    private readonly ChangeDesc _set;
    public SectionIter(ChangeDesc set) { _set = set; Next(); }
    public void Next()
    {
        var s = _set.Sections;
        if (I < s.Length) { Len = s[I++]; Ins = s[I++]; } else { Len = 0; Ins = -2; }
        Off = 0;
    }
    public bool Done => Ins == -2;
    public int Len2 => Ins < 0 ? Len : Ins;
    public Text Text {
        get {
            var ins = (_set as ChangeSet)?.Inserted;
            if (ins == null) return Text.Empty;
            var idx = (I - 2) >> 1;
            return idx >= ins.Length ? Text.Empty : ins[idx];
        }
    }
    public Text TextBit(int len = 0)
    {
        var ins = (_set as ChangeSet)?.Inserted;
        if (ins == null) return Text.Empty;
        var idx = (I - 2) >> 1;
        return idx >= ins.Length && len == 0 ? Text.Empty : ins[idx].Slice(Off, len == 0 ? int.MaxValue : Off + len);
    }
    public void Forward(int len) { if (len == Len) Next(); else { Len -= len; Off += len; } }
    public void Forward2(int len) { if (Ins == -1) Forward(len); else if (len == Ins) Next(); else { Ins -= len; Off += len; } }
}
