namespace Sia_Examples.Editor;

public sealed record CompletionResult(IReadOnlyList<CompletionCandidate> Items)
{
    public static CompletionResult Empty { get; } = new([]);

    public bool HasItems => Items.Count > 0;
}
