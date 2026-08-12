using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public sealed record CellTabGroup(
    string Id,
    ImmutableArray<string> TabIds,
    string ActiveTabId) : CellNode(Id);
