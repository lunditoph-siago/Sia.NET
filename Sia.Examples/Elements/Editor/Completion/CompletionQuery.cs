using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;

namespace Sia.Examples.Editor;

internal sealed record CompletionQuery(Document Document, CompletionService Service, CSharpSourceProjection? Projection,
    Func<string, int, CancellationToken, Task<CompletionResult>> Refresh)
{
    internal Document? EditingDocument { get; init; }
}
