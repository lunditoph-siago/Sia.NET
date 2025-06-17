using System.Drawing;
using System.Numerics;
using Sia.Examples.Runtime.Components;
using Sia.Reactors;

namespace Sia.Examples.Runtime;

public static class UIFactory
{
    public static Entity CreatePanel(World world, Vector2 position, Vector2 size, Entity? parent = null,
        int layer = 0, UIEventMask eventMask = UIEventMask.None, bool isInteractable = true)
    {
        if (eventMask != UIEventMask.None)
        {
            return world.Create(HList.From(
                new UIElement(position, size, true, isInteractable),
                new UIStyle(Color.FromArgb(40, 40, 40, 40), Color.Transparent, 0f, 4f),
                new Node<UIHierarchyTag>(parent),
                new UILayer(layer),
                new UIEventListener(true, eventMask)
            ));
        }
        else
        {
            return world.Create(HList.From(
                new UIElement(position, size, true, isInteractable),
                new UIStyle(Color.FromArgb(40, 40, 40, 40), Color.Transparent, 0f, 4f),
                new Node<UIHierarchyTag>(parent),
                new UILayer(layer)
            ));
        }
    }

    public static Entity CreateText(World world, Vector2 position, string content,
        Color color, float fontSize, Entity? parent = null)
    {
        return world.Create(HList.From(
            new UIElement(position, Vector2.Zero, true, false),
            new UIText(content, color, fontSize, TextAlignment.Left),
            new Node<UIHierarchyTag>(parent),
            new UILayer(1)
        ));
    }

    public static Entity CreateTextArea(World world, Vector2 position, Vector2 size,
        string content, Entity? parent = null)
    {
        return world.Create(HList.From(
            new UIElement(position, size, true, true),
            new UIText(content, Color.White, 12f, TextAlignment.Left),
            new UIScrollable(Vector2.Zero, Vector2.Zero, ScrollDirection.Vertical, 20f),
            new UIEventListener(true, UIEventMask.Scroll),
            new Node<UIHierarchyTag>(parent),
            new UILayer(1)
        ));
    }

    public static Entity CreateScrollView(World world, Vector2 position, Vector2 size,
        ScrollDirection direction, Entity? parent = null)
    {
        return world.Create(HList.From(
            new UIElement(position, size, true, true),
            new UILayout(LayoutType.Static, Vector2.Zero, LayoutAlignment.Start, false),
            new UIScrollable(Vector2.Zero, Vector2.Zero, direction, 20f),
            new UIEventListener(true, UIEventMask.Scroll),
            new Node<UIHierarchyTag>(parent),
            new UILayer(0)
        ));
    }

    public static Entity CreateVStackLayout(World world, Vector2 position, float spacing, Entity? parent = null)
    {
        return CreateLayout(world, position, new Vector2(100, 100), LayoutType.Vertical,
            new Vector2(0, spacing), LayoutAlignment.Start, parent);
    }

    public static Entity CreateHStackLayout(World world, Vector2 position, float spacing, Entity? parent = null)
    {
        return CreateLayout(world, position, new Vector2(100, 100), LayoutType.Horizontal,
            new Vector2(spacing, 0), LayoutAlignment.Start, parent);
    }

    public static Entity CreateLayout(World world, Vector2 position, Vector2 size, LayoutType layoutType,
        Vector2 spacing, LayoutAlignment alignment, Entity? parent = null, bool autoResize = true, int layer = 0)
    {
        return world.Create(HList.From(
            new UIElement(position, size, true, false),
            new UILayout(layoutType, spacing, alignment, autoResize),
            new Node<UIHierarchyTag>(parent),
            new UILayer(layer)
        ));
    }

    public static Entity CreateButton(World world, Vector2 position, Vector2 size,
        string text, Entity? parent = null)
    {
        return world.Create(HList.From(
            new UIElement(position, size, true, true),
            new UIText(text, Color.White, 14f, TextAlignment.Center),
            new UIButton(),
            new UIState(UIStateFlags.None),
            new UIEventListener(true, UIEventMask.Click | UIEventMask.Hover),
            new Node<UIHierarchyTag>(parent),
            new UILayer(1)
        ));
    }

    public static Entity CreateClickablePanel(World world, Vector2 position, Vector2 size, Entity? parent = null, int layer = 0)
    {
        return CreatePanel(world, position, size, parent, layer, UIEventMask.Click, true);
    }

    public static Entity CreateScrollableClickablePanel(World world, Vector2 position, Vector2 size, Entity? parent = null, int layer = 0)
    {
        return CreatePanel(world, position, size, parent, layer, UIEventMask.Click | UIEventMask.Scroll, true);
    }

    public static Entity CreateLayoutPanel(World world, Vector2 position, Vector2 size, LayoutType layoutType,
        Vector2 spacing, LayoutAlignment alignment, Entity? parent = null, int layer = 0, bool autoResize = true)
    {
        return world.Create(HList.From(
            new UIElement(position, size, true, false),
            new UIStyle(Color.FromArgb(40, 40, 40, 40), Color.Transparent, 0f, 4f),
            new UILayout(layoutType, spacing, alignment, autoResize),
            new Node<UIHierarchyTag>(parent),
            new UILayer(layer)
        ));
    }

    public static Entity CreateScrollableContainer(World world, Vector2 position, Vector2 size,
        ScrollDirection direction = ScrollDirection.Vertical, Entity? parent = null, int layer = 0)
    {
        return world.Create(HList.From(
            new UIElement(position, size, true, true),
            new UIScrollable(Vector2.Zero, Vector2.Zero, direction, 20f),
            new UIEventListener(true, UIEventMask.Scroll),
            new Node<UIHierarchyTag>(parent),
            new UILayer(layer)
        ));
    }

    public static Entity CreateHorizontalLayoutPanel(World world, Vector2 position, Vector2 size, float spacing,
        LayoutAlignment alignment = LayoutAlignment.Center, Entity? parent = null, int layer = 0, bool autoResize = true)
    {
        return CreateLayoutPanel(world, position, size, LayoutType.Horizontal, new Vector2(spacing, 0),
            alignment, parent, layer, autoResize);
    }

    public static Entity CreateVerticalLayoutPanel(World world, Vector2 position, Vector2 size, float spacing,
        LayoutAlignment alignment = LayoutAlignment.Start, Entity? parent = null, int layer = 0, bool autoResize = true)
    {
        return CreateLayoutPanel(world, position, size, LayoutType.Vertical, new Vector2(0, spacing),
            alignment, parent, layer, autoResize);
    }
}