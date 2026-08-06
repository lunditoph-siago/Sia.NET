namespace Sia_Examples.Editor;

public delegate bool StateCommand(CommandTarget target);

public readonly record struct CommandTarget(EditorState State, Action<EditorState> Apply);

public sealed class TransactionSpec
{
    public ChangeSpec[]? Changes { get; set; }
    public EditorSelection? Selection { get; set; }
    public object[]? Effects { get; set; }
    public object[]? Annotations { get; set; }
    public string? UserEvent { get; set; }
    public bool ScrollIntoView { get; set; }
    public bool? Filter { get; set; }
    public bool Sequential { get; set; }
}

public sealed class Transaction
{
    public EditorState StartState { get; }
    public EditorState NewState { get; }
    public ChangeSet Changes { get; }
    public EditorSelection? Selection { get; }
    public object[] Effects { get; }
    public object[] Annotations { get; }
    public bool ScrollIntoView { get; }

    public Text NewDoc => NewState.Doc;
    public EditorSelection NewSelection => NewState.Selection;
    public bool DocChanged => !Changes.IsEmpty;

    internal Transaction(EditorState startState, EditorState newState, ChangeSet changes, EditorSelection? selection,
        object[] effects, object[] annotations, bool scrollIntoView)
    {
        StartState = startState; NewState = newState; Changes = changes; Selection = selection;
        Effects = effects; Annotations = annotations; ScrollIntoView = scrollIntoView;
    }

    public T? Annotation<T>(AnnotationType<T> type)
    {
        foreach (var a in Annotations)
            if (a is Annotation<T> ann && ann.Type == type) return ann.Value;
        return default;
    }

    internal Transaction WithExtensions(EditorExtensions extensions)
    {
        if (ReferenceEquals(extensions, NewState.Extensions)) return this;
        return new Transaction(StartState, NewState with { Extensions = extensions }, Changes, Selection, Effects, Annotations, ScrollIntoView);
    }

    public static readonly AnnotationType<long> Time = AnnotationType<long>.Define();
    public static readonly AnnotationType<string> UserEvent = AnnotationType<string>.Define();
    public static readonly AnnotationType<bool> AddToHistory = AnnotationType<bool>.Define();
    public static readonly AnnotationType<bool> Remote = AnnotationType<bool>.Define();
}
