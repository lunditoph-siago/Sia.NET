#if !BROWSER
namespace Sia.Examples.Console;

internal sealed partial class ConsoleDomBackend
{
    private ConsoleDomNode CreateDocument()
    {
        // Hosts required by the shared application and notebook views.
        var document = NewNode("#document");
        var app = Element(document, "div", "app");
        var sidebar = Element(app, "aside", "sidebar");
        Element(sidebar, "div", "sidebar-actions");
        Element(sidebar, "div", "built-in-notebooks");
        Element(sidebar, "div", "user-notebooks");
        Element(sidebar, "div", "notebook-files");

        var content = Element(app, "main", "content");
        var header = Element(content, "div", "content-header");
        Element(header, "div", "notebook-actions");
        var packages = Element(header, "div", "header-packages");
        var packagesToggle = Element(packages, "button", "packages-toggle", "Packages");
        Element(packagesToggle, "span", "packages-badge");
        Element(packages, "div", "packages-popover");

        var notebook = Element(content, "div", "notebook");
        var home = Element(notebook, "div", "notebook-home");
        Element(home, "button", "home-start-guide", "Open the first example");
        Element(home, "button", "home-open-editor", "Open Editor Lab");
        return document;

        ConsoleDomNode Element(ConsoleDomNode parent, string tagName, string id, string? text = null)
        {
            var node = NewNode(tagName, id);
            AppendRaw(parent, node);
            if (text is not null) AppendRaw(node, NewText(text));
            return node;
        }
    }
}
#endif
