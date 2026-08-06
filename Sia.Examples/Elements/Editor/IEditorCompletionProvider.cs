namespace Sia_Examples.Editor;

public readonly record struct CompletionCandidate(string Label, string InsertText, int ReplaceStart, int ReplaceEnd);

public sealed record CompletionQueryResult(IReadOnlyList<CompletionCandidate> Items)
{
    public static readonly CompletionQueryResult Empty = new([]);

    public bool IsOpen => Items.Count > 0;
}

public interface IEditorCompletionProvider
{
    public Task<CompletionQueryResult> QueryAsync(string source, int position, CancellationToken cancellationToken = default);
}
