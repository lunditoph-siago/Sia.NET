using Sia.Examples.Dom;

namespace Sia.Examples;

internal static class BrowserControls
{
    internal static DomElement WordWrapToggle(bool menu = true)
    {
        var button = DomElement.Create("button").Class("word-wrap-toggle")
            .Class(menu ? "menu-item" : "icon-btn")
            .Attr("type", "button").Attr("role", menu ? "menuitemcheckbox" : "checkbox")
            .Attr("aria-label", "Word Wrap").Attr("aria-checked", "true")
            .Attr("title", "Toggle word wrap (Alt+Z)")
            .Attr("data-workbench-command", "ToggleWordWrap");
        if (menu) return button.Text("Word Wrap");
        using var icon = DomElement.Create("svg").Attr("viewBox", "0 0 24 24")
            .Attr("fill", "none").Attr("stroke", "currentColor").Attr("stroke-width", "1.5")
            .Attr("stroke-linecap", "round").Attr("stroke-linejoin", "round").Attr("aria-hidden", "true");
        using var path = DomElement.Create("path").Attr("d", "M4 6h16M4 11h13a4 4 0 0 1 0 8h-5m3-3-3 3 3 3M4 16h3");
        return button.Append(icon.Append(path));
    }

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
