using Sia.Examples.Dom;

namespace Sia.Examples;

internal static class BrowserControls
{
    internal static DomElement IconButton(string icon, string label)
        => DomElement.Create("button")
            .Class("icon-btn")
            .Attr("type", "button")
            .Attr("aria-label", label)
            .Attr("title", label)
            .Text(icon);

    internal static DomElement MenuItem(string icon, string label)
    {
        var button = DomElement.Create("button").Class("menu-item").Attr("type", "button");
        using var glyph = DomElement.Create("span").Class("menu-icon").Attr("aria-hidden", "true").Text(icon);
        using var text = DomElement.Create("span").Text(label);
        return button.Append(glyph).Append(text);
    }

    internal static DomElement InlineActions(
        string inputId,
        string saveLabel,
        string payload,
        string discardLabel = "Discard changes")
    {
        var actions = DomElement.Create("div").Class("inline-edit-actions");
        using var save = IconButton("▣", saveLabel).Attr("data-inline-save", inputId).On("click", payload);
        using var discard = IconButton("↶", discardLabel).Attr("data-inline-discard", inputId);
        return actions.Append(save).Append(discard);
    }
}
