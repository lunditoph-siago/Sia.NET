using Sia.Examples.Notebook;
using Destination = Sia.Examples.ApplicationDestination;

namespace Sia.Examples;

internal sealed record NavigationState(ApplicationRoute Page, string Content, bool Dirty);

internal sealed record NavigationEvent(
    Destination Target, NotebookInfo? Info = null, bool Reload = false,
    int HistoryTicket = 0, bool Replace = false, bool Discard = false,
    NavigationState? Before = null)
{
    public ApplicationRoute Page => new(Target, Info);
}

internal enum NavigationEffect { Stay, Confirm, Open }

internal readonly record struct NavigationTransition(ApplicationRoute Page, NavigationEffect Effect);

internal static class ApplicationNavigation
{
    public static NavigationTransition Update(NavigationState state, NavigationEvent action)
    {
        var samePage = (state.Page, action.Page) switch {
            ( { Target: Destination.Editor }, { Target: Destination.Editor }) => true,
            ( { Target: Destination.Notebook, Info: { } current }, { Target: Destination.Notebook, Info: { } target })
                => (current.Key, current.Origin) == (target.Key, target.Origin),
            _ => false,
        };
        var discardApplies = action.Discard
            && (action.Before is null || action.Before.Content == state.Content);
        return (action.Reload, samePage, state.Dirty, discardApplies) switch {
            (false, true, _, _) => new(state.Page, NavigationEffect.Stay),
            (_, _, true, false) => new(state.Page, NavigationEffect.Confirm),
            _ => new(action.Page, NavigationEffect.Open),
        };
    }
}
