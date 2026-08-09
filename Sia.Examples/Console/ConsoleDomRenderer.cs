#if !BROWSER
using Sia_Examples.Console.Layout;

namespace Sia_Examples.Console;

internal sealed class ConsoleDomRenderer
{
    private const int SidebarWidth = 22;

    private int _sidebarScroll;
    private int _notebookScroll;

    public IReadOnlyList<string> Render(ConsoleRenderRequest request)
    {
        var screen = new Rect(0, 0, request.Width, request.Height);
        var vertical = LayoutEngine.Split(
            screen,
            Direction.Vertical,
            Constraint.Length(1),
            Constraint.Fill(),
            Constraint.Length(1));
        var titleArea = vertical[0];
        var boxArea = vertical[1];
        var statusArea = vertical[2];

        var inner = boxArea.Inset(1, 1);
        var horizontal = LayoutEngine.Split(
            inner,
            Direction.Horizontal,
            Constraint.Length(SidebarWidth),
            Constraint.Length(1),
            Constraint.Fill());
        var sidebarArea = horizontal[0];
        var dividerArea = horizontal[1];
        var contentArea = horizontal[2];

        var sidebarLines = new List<ConsoleLine>();
        RenderNode(sidebarLines, request.Sidebar, depth: 0, request.Focused, cursor: null);

        var headerLines = new List<ConsoleLine>();
        RenderNode(headerLines, request.ContentHeader, depth: 0, request.Focused, cursor: null);

        var notebookLines = new List<ConsoleLine>();
        RenderNode(notebookLines, request.Notebook, depth: 0, request.Focused, request.Cursor);
        if (!string.IsNullOrWhiteSpace(request.Error)) {
            AddLines(
                notebookLines,
                $"Error: {request.Error}",
                new(ConsoleColor.Red),
                depth: 0,
                node: null,
                marker: "! ",
                preserveWhitespace: false);
        }

        var notebookHeight = Math.Max(contentArea.Height - 1, 0);
        var focusedInActivePane = request.ActivePane == Pane.Content ? request.Focused : null;
        KeepVisible(sidebarLines, request.Focused, sidebarArea.Height, ref _sidebarScroll);
        KeepVisible(notebookLines, focusedInActivePane, notebookHeight, ref _notebookScroll);

        var sidebarRows = Materialize(sidebarLines, _sidebarScroll, sidebarArea.Width, sidebarArea.Height);
        var headerRow = headerLines.Count > 0
            ? headerLines[0].Render(contentArea.Width)
            : RenderLine(string.Empty, default, contentArea.Width);
        var notebookRows = Materialize(notebookLines, _notebookScroll, contentArea.Width, notebookHeight);

        var rows = new string[request.Height];
        if (titleArea.Height > 0) {
            rows[titleArea.Y] = RenderLine(
                "Sia.NET Examples · Console DOM",
                new(ConsoleColor.Cyan, ConsoleDecoration.Bold),
                titleArea.Width);
        }

        for (var y = boxArea.Y; y < boxArea.Bottom; y++) {
            rows[y] = y == boxArea.Y
                ? Border(BoxDrawing.TopLeft, BoxDrawing.TopJoin, BoxDrawing.TopRight)
                : y == boxArea.Bottom - 1
                    ? Border(BoxDrawing.BottomLeft, BoxDrawing.BottomJoin, BoxDrawing.BottomRight)
                    : InnerRow(y);
        }

        if (statusArea.Height > 0) {
            rows[statusArea.Y] = request.EditMode switch {
                EditMode.Normal => RenderLine(
                    "-- NORMAL -- hjkl move · i/a/I/A/o/O insert · x del · dd del line · gg/G top/bottom · Esc exit",
                    new(ConsoleColor.Yellow, ConsoleDecoration.Bold),
                    statusArea.Width),
                EditMode.Insert => RenderLine(
                    "-- INSERT -- type to edit · Esc back to normal mode",
                    new(ConsoleColor.Green, ConsoleDecoration.Bold),
                    statusArea.Width),
                _ => RenderLine(
                    "↑/↓ move · ←/→/Tab switch pane · Enter activate · Q/Esc quit",
                    new(ConsoleColor.Gray, ConsoleDecoration.Dim),
                    statusArea.Width),
            };
        }

        for (var y = 0; y < rows.Length; y++) {
            rows[y] ??= RenderLine(string.Empty, default, request.Width);
        }
        return rows;

        string Border(char left, char join, char right)
            => $"{left}{new string(BoxDrawing.Horizontal, sidebarArea.Width)}{join}"
                + $"{new string(BoxDrawing.Horizontal, contentArea.Width)}{right}";

        string InnerRow(int y)
        {
            var sidebarRow = sidebarRows[y - sidebarArea.Y];
            var contentRow = y == contentArea.Y
                ? headerRow
                : notebookRows[y - contentArea.Y - 1];
            return $"{BoxDrawing.Vertical}{sidebarRow}{BoxDrawing.Vertical}{contentRow}{BoxDrawing.Vertical}";
        }
    }

    private static string[] Materialize(List<ConsoleLine> lines, int scrollOffset, int width, int height)
    {
        var rows = new string[Math.Max(height, 0)];
        for (var row = 0; row < rows.Length; row++) {
            var index = scrollOffset + row;
            rows[row] = index < lines.Count
                ? lines[index].Render(width)
                : RenderLine(string.Empty, default, width);
        }
        return rows;
    }

    private static void RenderNode(
        List<ConsoleLine> lines,
        ConsoleDomNode node,
        int depth,
        ConsoleDomNode? focused,
        EditCursor? cursor)
    {
        if (!node.IsVisible
            || node.TagName is "datalist" or "option"
            || node.HasClass("editor-gutter")
            || node.HasClass("editor-status")) {
            return;
        }
        if (node.HasClass("cell-header") || node.HasClass("cell-controls")) {
            RenderInlineRow(lines, node, depth, focused);
            if (node.HasClass("cell-header")) {
                foreach (var child in node.Children.Where(static c => c.HasClass("cell-controls"))) {
                    RenderNode(lines, child, depth, focused, cursor);
                }
            }
            return;
        }
        if (node.HasClass("editor-surface")) {
            RenderEditorAffordance(lines, node, depth, focused);
        }
        if (cursor is { } target && ReferenceEquals(node, target.Line)) {
            RenderCursorLine(lines, node, depth, target.Column);
            return;
        }
        if (TryDescribe(node, out var text, out var consumeChildren)) {
            var isFocused = ReferenceEquals(node, focused);
            AddLines(
                lines,
                text,
                Style(node, isFocused),
                depth,
                node,
                isFocused ? "› " : Marker(node),
                PreserveWhitespace(node));
        }
        if (consumeChildren) {
            return;
        }

        var childDepth = HasStructuralLabel(node) ? depth + 1 : depth;
        foreach (var child in node.Children) {
            RenderNode(lines, child, childDepth, focused, cursor);
        }
    }

    private static void RenderEditorAffordance(
        List<ConsoleLine> lines,
        ConsoleDomNode node,
        int depth,
        ConsoleDomNode? focused)
    {
        var isFocused = ReferenceEquals(node, focused);
        var style = new ConsoleStyle(
            ConsoleColor.Gray,
            isFocused ? ConsoleDecoration.Reverse : ConsoleDecoration.Dim);
        var line = new ConsoleLine();
        line.Append(new string(' ', depth * 2), default);
        line.Append(isFocused ? "› " : "  ", style, node);
        line.Append("[Enter to edit]", style, node);
        lines.Add(line);
    }

    private static void RenderCursorLine(
        List<ConsoleLine> lines,
        ConsoleDomNode node,
        int depth,
        int column)
    {
        var text = node.TextContent();
        var before = column >= 0 && column <= text.Length ? text[..column] : text;
        var atCursor = column >= 0 && column < text.Length ? text[column].ToString() : " ";
        var after = column >= 0 && column + 1 <= text.Length ? text[(column + 1)..] : string.Empty;

        var line = new ConsoleLine();
        line.Append(new string(' ', depth * 2), default);
        line.Append("  ", default, node);
        line.Append(before, default, node);
        line.Append(atCursor, new(ConsoleColor.Default, ConsoleDecoration.Reverse), node);
        line.Append(after, default, node);
        lines.Add(line);
    }

    private static void RenderInlineRow(
        List<ConsoleLine> lines,
        ConsoleDomNode node,
        int depth,
        ConsoleDomNode? focused)
    {
        var line = new ConsoleLine();
        line.Append(new string(' ', depth * 2), default);
        var wrote = false;
        foreach (var child in node.Children) {
            if (child.HasClass("cell-controls") || !TryDescribe(child, out var text, out _)) {
                continue;
            }
            var normalized = Normalize(text);
            if (normalized.Length == 0) {
                continue;
            }
            if (wrote) {
                line.Append("  ", default);
            }
            wrote = true;
            var isFocused = ReferenceEquals(child, focused);
            var style = Style(child, isFocused);
            line.Append(isFocused ? "› " : Marker(child), style, child);
            line.Append(normalized, style, child);
        }
        if (wrote) {
            lines.Add(line);
        }
    }

    private static bool TryDescribe(
        ConsoleDomNode node,
        out string text,
        out bool consumeChildren)
    {
        consumeChildren = false;
        if (node.IsText) {
            text = node.Text;
            consumeChildren = true;
            return text.Length > 0;
        }

        text = StructuralLabel(node);
        if (text.Length > 0) {
            return true;
        }

        if (node.TagName == "button") {
            text = ButtonText(node);
            consumeChildren = true;
            return true;
        }
        if (node.TagName == "input") {
            var placeholder = node.Attributes.GetValueOrDefault("placeholder", "Input");
            var value = node.Attributes.GetValueOrDefault("value", string.Empty);
            text = value.Length == 0 ? $"{placeholder}: _" : $"{placeholder}: {value}";
            consumeChildren = true;
            return true;
        }
        if (node.HasClass("editor-line")
            || node.HasClass("diagnostic-error")
            || node.HasClass("diagnostic-warning")) {
            text = node.TextContent();
            consumeChildren = true;
            return true;
        }
        if (node.TagName is "h1" or "h2" or "h3" or "p" or "li" or "pre" or "code"
            || node.TagName == "span"
            || (node.HasClass("code") && !node.HasClass("editor-container"))
            || node.HasClass("output")) {
            text = node.TextContent();
            consumeChildren = true;
            return text.Length > 0;
        }
        return false;
    }

    private static string StructuralLabel(ConsoleDomNode node)
    {
        if (node.Id == "sidebar") {
            return "Examples";
        }
        if (node.Id == "notebook") {
            return "Notebook";
        }
        if (node.Id == "packages-popover" || node.HasClass("packages-body")) {
            return "Packages";
        }
        if (node.HasClass("section")) {
            return "Section";
        }
        if (node.HasClass("cell")) {
            return "Cell";
        }
        return string.Empty;
    }

    private static bool HasStructuralLabel(ConsoleDomNode node)
        => StructuralLabel(node).Length > 0;

    private static string ButtonText(ConsoleDomNode node)
    {
        if (!node.HasClass("example-btn")) {
            return node.TextContent();
        }
        var name = node.FirstWithClass("name")?.TextContent() ?? string.Empty;
        var description = node.FirstWithClass("desc")?.TextContent() ?? string.Empty;
        return description.Length == 0 ? name : $"{name} — {description}";
    }

    private static string Marker(ConsoleDomNode node)
        => node.TagName == "button" ? "• " : "  ";

    private static bool PreserveWhitespace(ConsoleDomNode node)
        => node.TagName is "pre" or "code" || node.HasClass("code") || node.HasClass("editor-line");

    private static ConsoleStyle Style(ConsoleDomNode node, bool focused)
    {
        var style = node.TagName switch {
            "h1" or "h2" or "h3" => new ConsoleStyle(
                ConsoleColor.Cyan,
                ConsoleDecoration.Bold),
            "button" => new ConsoleStyle(ConsoleColor.Yellow),
            "pre" or "code" => new ConsoleStyle(ConsoleColor.Gray),
            _ => default,
        };
        if (HasStructuralLabel(node)) {
            style = new(ConsoleColor.Cyan, ConsoleDecoration.Bold);
        }
        if (node.HasClass("active") || node.HasClass("selected")) {
            style = style.With(ConsoleDecoration.Bold);
        }
        if (node.HasClass("diagnostic-error")
            || node.HasClass("output-error")
            || node.HasClass("package-failed")) {
            style = style with { Color = ConsoleColor.Red };
        }
        if (focused) {
            style = style.With(ConsoleDecoration.Reverse);
        }
        return style;
    }

    private static void AddLines(
        List<ConsoleLine> lines,
        string text,
        ConsoleStyle style,
        int depth,
        ConsoleDomNode? node,
        string marker,
        bool preserveWhitespace)
    {
        var source = text.Replace("\r", string.Empty, StringComparison.Ordinal);
        foreach (var value in source.Split('\n')) {
            var normalized = preserveWhitespace ? value : Normalize(value);
            if (normalized.Length == 0 && !preserveWhitespace) {
                continue;
            }
            var line = new ConsoleLine();
            line.Append(new string(' ', depth * 2), default);
            line.Append(marker, style, node);
            line.Append(normalized, style, node);
            lines.Add(line);
        }
    }

    private static string Normalize(string value)
        => string.Join(' ', value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static void KeepVisible(
        IReadOnlyList<ConsoleLine> lines,
        ConsoleDomNode? focused,
        int height,
        ref int scrollOffset)
    {
        scrollOffset = Math.Clamp(scrollOffset, 0, Math.Max(lines.Count - height, 0));
        if (focused is null) {
            return;
        }
        var focusedLine = -1;
        for (var index = 0; index < lines.Count; index++) {
            if (lines[index].Nodes.Contains(focused)) {
                focusedLine = index;
                break;
            }
        }
        if (focusedLine < 0) {
            return;
        }
        if (focusedLine < scrollOffset) {
            scrollOffset = focusedLine;
        }
        else if (focusedLine >= scrollOffset + height) {
            scrollOffset = focusedLine - height + 1;
        }
    }

    private static string RenderLine(string text, ConsoleStyle style, int width)
    {
        var line = new ConsoleLine();
        line.Append(text, style);
        return line.Render(width);
    }
}
#endif
