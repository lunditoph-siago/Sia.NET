namespace Sia.Examples.Editor;

public readonly record struct CommandTarget(EditorState State, Action<EditorState> Apply);
