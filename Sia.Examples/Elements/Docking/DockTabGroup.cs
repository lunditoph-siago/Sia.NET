using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public sealed record DockTabGroup(
    string Id,
    ImmutableArray<string> TabIds,
    string ActiveTabId) : DockNode(Id);
