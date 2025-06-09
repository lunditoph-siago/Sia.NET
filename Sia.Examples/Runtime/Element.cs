using System.Drawing;
using System.Numerics;
using Sia.Examples.Runtime.Components;
using Sia.Reactors;

namespace Sia.Examples.Runtime;

public static class UI
{
    // Scrollable Text Area
    public static Entity ScrollableText(World world, Vector2 position, Vector2 size,
        string content, Color textColor, float fontSize = 12f, Color backgroundColor = default) =>
        world.Create(HList.From(
            new UIElement(position, size, true, true, 1),
            new UIText(content, textColor, fontSize, true),
            new UIPanel(backgroundColor == default ? Color.FromArgb(50, 30, 30, 40) : backgroundColor, true),
            new UIScrollable(),
            new UIEventListener(),
            new Node<UIHierarchyTag>()
        ));

    // Button
    public static Entity Button(World world, Vector2 position, Vector2 size, string text,
        Color normalColor = default, Color hoverColor = default, Color pressedColor = default) =>
        world.Create(HList.From(
            new UIElement(position, size, true, true, 1),
            new UIText(text, Color.White, 14f, true),
            new UIButton(
                normalColor == default ? Color.Gray : normalColor,
                hoverColor == default ? Color.LightGray : hoverColor,
                pressedColor == default ? Color.DarkGray : pressedColor,
                true),
            new UIEventListener(),
            new UIInteractionState()
        ));

    // Panel
    public static Entity Panel(World world, Vector2 position, Vector2 size, Color backgroundColor = default) =>
        world.Create(HList.From(
            new UIElement(position, size, true, false, 0),
            new UIPanel(backgroundColor == default ? Color.Gray : backgroundColor, true)
        ));

    // Text Label
    public static Entity Text(World world, Vector2 position, string content, Color color = default, float fontSize = 12f) =>
        world.Create(HList.From(
            new UIElement(position, new Vector2(content.Length * fontSize * 0.6f, fontSize * 1.2f), true, false, 1),
            new UIText(content, color == default ? Color.White : color, fontSize, true)
        ));

    // Scrolling operations
    public static void ScrollTo(Entity entity, Vector2 offset)
    {
        if (!entity.Contains<UIScrollable>()) return;
        ref var scrollable = ref entity.Get<UIScrollable>();
        scrollable = scrollable with
        {
            ScrollOffset = scrollable.ClampScrollOffset(offset, entity.Get<UIElement>().Size)
        };
    }

    public static void ScrollToTop(Entity entity) => ScrollTo(entity, Vector2.Zero);

    public static void ScrollToBottom(Entity entity)
    {
        if (!entity.Contains<UIScrollable>()) return;
        var scrollable = entity.Get<UIScrollable>();
        var element = entity.Get<UIElement>();
        ScrollTo(entity, new Vector2(0, scrollable.GetMaxScrollOffset(element.Size).Y));
    }

    public static void SetContent(Entity entity, string content)
    {
        if (!entity.Contains<UIText>()) return;
        new UIText.View(entity).Content = content;
        if (entity.Contains<UIScrollable>())
            new UIScrollable.View(entity).ScrollOffset = Vector2.Zero;
    }
}
