namespace Sia_Examples.Editor;

public readonly record struct EditorState(
    Text Doc,
    EditorSelection Selection,
    int TabSize,
    bool ReadOnly,
    string LineBreak,
    int Version,
    EditorExtensions Extensions
) : IEquatable<EditorState>
{
    public bool Equals(EditorState other) => Version == other.Version;
    public override int GetHashCode() => Version;

    public string SliceDoc(int from = 0, int to = int.MaxValue) => Doc.SliceDoc(from, to);
    public Line LineAt(int pos) => Doc.LineAt(pos);

    public T Field<T>(StateField<T> field) => Extensions.Field(field);

    public TOutput Facet<TInput, TOutput>(Facet<TInput, TOutput> facet) => Extensions.Facet(facet);

    public EditorState Apply(TransactionSpec spec) => ToTransaction(spec).NewState;

    public Transaction ToTransaction(TransactionSpec spec)
    {
        var changes = spec.Changes != null
            ? ChangeSet.Of(spec.Changes, Doc.Length, LineBreak)
            : ChangeSet.Empty(Doc.Length);

        var annotations = new List<object>();
        if (spec.Annotations != null) annotations.AddRange(spec.Annotations);
        if (spec.UserEvent != null) annotations.Add(Transaction.UserEvent.Of(spec.UserEvent));
        annotations.Add(Transaction.Time.Of(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        var newSel = spec.Selection ?? Selection.Map(changes);
        var effects = spec.Effects ?? [];

        var provisional = this with { Doc = changes.Apply(Doc), Selection = newSel, Version = Version + 1 };
        var tr = new Transaction(this, provisional, changes, newSel, effects, [.. annotations], spec.ScrollIntoView);
        return tr.WithExtensions(Extensions.Update(tr));
    }

    public static EditorState Create(EditorStateConfig config)
    {
        var doc = config.Doc ?? Text.Of([""]);
        var state = new EditorState(
            Doc: doc,
            Selection: config.Selection ?? EditorSelection.Single(0),
            TabSize: config.TabSize,
            ReadOnly: config.ReadOnly,
            LineBreak: config.LineBreak ?? "\n",
            Version: 1,
            Extensions: EditorExtensions.Empty);

        var extensions = config.Extensions;
        return extensions is null or { Length: 0 }
            ? state
            : state with { Extensions = EditorExtensions.Create(extensions, state) };
    }
}

public sealed class EditorStateConfig
{
    public Text? Doc { get; set; }
    public EditorSelection? Selection { get; set; }
    public IExtension[]? Extensions { get; set; }
    public int TabSize { get; set; } = 4;
    public bool ReadOnly { get; set; }
    public string? LineBreak { get; set; }
}
