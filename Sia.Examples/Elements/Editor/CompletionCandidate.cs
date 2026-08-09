namespace Sia_Examples.Editor;

public readonly record struct CompletionCandidate(
    string Label,
    string InsertText,
    int ReplaceStart,
    int ReplaceEnd);
