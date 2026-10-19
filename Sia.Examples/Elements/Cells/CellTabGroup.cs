using System.Collections.Immutable;

namespace Sia.Examples.Notebook;

public sealed record CellTabGroup(
    string Id,
    ImmutableArray<string> TabIds,
    string ActiveTabId) : CellNode(Id);
