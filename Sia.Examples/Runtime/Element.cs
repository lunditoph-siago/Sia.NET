using System.Drawing;
using System.Numerics;
using Sia.Examples.Runtime.Components;
using Sia.Reactors;

namespace Sia.Examples.Runtime;

public static class UIFactory
{
    public static Entity CreatePanel(World world, Vector2 position, Vector2 size, Entity? parent = null)
    {
        var entity = world.Create(HList.From(
            new UIElement(position, size, true, true),
            new UIStyle(Color.FromArgb(40, 40, 40, 40), Color.Transparent, 0f, 4f),
            new Node<UIHierarchyTag>(parent),
            new UILayer(0)
        ));

        return entity;
    }

    public static Entity CreateText(World world, Vector2 position, string content,
        Color color, float fontSize, Entity? parent = null)
    {
        var entity = world.Create(HList.From(
            new UIElement(position, Vector2.Zero, true, false),
            new UIText(content, color, fontSize, TextAlignment.Left),
            new Node<UIHierarchyTag>(parent),
            new UILayer(1)
        ));

        return entity;
    }

    public static Entity CreateTextArea(World world, Vector2 position, Vector2 size,
        string content, Entity? parent = null)
    {
        var entity = world.Create(HList.From(
            new UIElement(position, size, true, true),
            new UIText(content, Color.White, 12f, TextAlignment.Left),
            new UIScrollable(Vector2.Zero, Vector2.Zero, ScrollDirection.Vertical, 20f),
            new UIEventListener(true, UIEventMask.Scroll),
            new Node<UIHierarchyTag>(parent),
            new UILayer(1)
        ));

        return entity;
    }

    public static Entity CreateScrollView(World world, Vector2 position, Vector2 size,
        ScrollDirection direction, Entity? parent = null)
    {
        var entity = world.Create(HList.From(
            new UIElement(position, size, true, true),
            new UIScrollable(Vector2.Zero, Vector2.Zero, direction, 20f),
            new UIEventListener(true, UIEventMask.Scroll),
            new Node<UIHierarchyTag>(parent),
            new UILayer(0)
        ));

        return entity;
    }

    public static Entity CreateVStackLayout(World world, Vector2 position, float spacing, Entity? parent = null)
    {
        var entity = world.Create(HList.From(
            new UIElement(position, new Vector2(100, 100), true, false),
            new UILayout(LayoutType.Vertical, new Vector2(0, spacing), LayoutAlignment.Start, true),
            new Node<UIHierarchyTag>(parent),
            new UILayer(0)
        ));

        return entity;
    }

    public static Entity CreateButton(World world, Vector2 position, Vector2 size,
        string text, Entity? parent = null)
    {
        var entity = world.Create(HList.From(
            new UIElement(position, size, true, true),
            new UIText(text, Color.White, 14f, TextAlignment.Center),
            new UIButton(),
            new UIState(UIStateFlags.None),
            new UIEventListener(true, UIEventMask.Click | UIEventMask.Hover),
            new Node<UIHierarchyTag>(parent),
            new UILayer(1)
        ));

        return entity;
    }
}