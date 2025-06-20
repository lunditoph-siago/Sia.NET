using System.Drawing;
using System.Numerics;
using Sia.Examples.Runtime.Components;
using Sia.Reactors;

namespace Sia.Examples.Runtime;

public static class UIFactory
{
    #region Basic Elements

    public static Entity CreatePanel(
        this World world,
        Vector2 position,
        Vector2 size,
        Entity? parent = null,
        int layer = 0,
        UIStyle? style = null)
    {
        var entity = world.Create(HList.From(
            new UIElement(position, size, true, true),
            style ?? new UIStyle(),
            new UILayer(layer),
            new UIState(),
            new UIEventListener(),
            new UILayout(LayoutType.None, SizeValue.Pixels(size.X), SizeValue.Pixels(size.Y), Vector4.Zero, Vector4.Zero, true),
            new UIComputedLayout(),
            new Node<UIHierarchyTag>(parent)
        ));

        return entity;
    }

    public static Entity CreateAbsolutePanel(
        this World world,
        Vector2 position,
        Vector2 size,
        Entity? parent = null,
        int layer = 0,
        UIStyle? style = null)
    {
        var entity = world.Create(HList.From(
            new UIElement(position, size, true, true),
            style ?? new UIStyle(),
            new UILayer(layer),
            new UIState(),
            new UIEventListener(),
            new UILayout(LayoutType.Absolute, SizeValue.Pixels(size.X), SizeValue.Pixels(size.Y), Vector4.Zero, Vector4.Zero, true),
            new UIComputedLayout(),
            new UIAbsolutePosition(SizeValue.Pixels(position.X), SizeValue.Pixels(position.Y), SizeValue.Auto, SizeValue.Auto),
            new Node<UIHierarchyTag>(parent)
        ));

        return entity;
    }

    public static Entity CreatePaddedPanel(
        this World world,
        Vector2 position,
        Vector2 size,
        Vector4 padding,
        Entity? parent = null,
        int layer = 0,
        UIStyle? style = null)
    {
        var entity = world.Create(HList.From(
            new UIElement(position, size, true, true),
            style ?? new UIStyle(),
            new UILayer(layer),
            new UIState(),
            new UIEventListener(),
            new UILayout(LayoutType.None, SizeValue.Pixels(size.X), SizeValue.Pixels(size.Y), Vector4.Zero, padding, true),
            new UIComputedLayout(),
            new Node<UIHierarchyTag>(parent)
        ));

        return entity;
    }

    #endregion

    #region Text Elements

    public static Entity CreateText(
        this World world,
        Vector2 position,
        string content,
        Color color,
        float fontSize,
        Entity? parent = null,
        int? layer = null,
        TextAlignment alignment = TextAlignment.Left)
    {
        var actualLayer = layer ?? (parent?.Contains<UILayer>() == true ? parent.Get<UILayer>().Value + 1 : 0);

        var entity = world.Create(HList.From(
            new UIElement(position, Vector2.Zero, true, false),
            new UIText(content, color, fontSize, alignment),
            new UIStyle(),
            new UILayer(actualLayer),
            new UIState(),
            new UIEventListener(false, UIEventMask.None),
            new UILayout(LayoutType.None, SizeValue.Auto, SizeValue.Auto, Vector4.Zero, Vector4.Zero, true),
            new UIComputedLayout(),
            new Node<UIHierarchyTag>(parent)
        ));

        return entity;
    }

    public static Entity CreateTextArea(
        this World world,
        Vector2 position,
        Vector2 size,
        string content,
        Entity? parent = null,
        Color? textColor = null,
        float fontSize = 14f,
        int? layer = null)
    {
        var actualLayer = layer ?? (parent?.Contains<UILayer>() == true ? parent.Get<UILayer>().Value + 1 : 0);
        var actualColor = textColor ?? Color.Black;

        var entity = world.Create(HList.From(
            new UIElement(position, size, true, false),
            new UIText(content, actualColor, fontSize, TextAlignment.Left),
            new UIStyle(),
            new UILayer(actualLayer),
            new UIState(),
            new UIEventListener(false, UIEventMask.None),
            new UILayout(LayoutType.None, SizeValue.Pixels(size.X), SizeValue.Pixels(size.Y), Vector4.Zero, Vector4.Zero, true),
            new UIComputedLayout(),
            new Node<UIHierarchyTag>(parent)
        ));

        return entity;
    }

    #endregion

    #region Interactive Elements

    public static Entity CreateButton(
        this World world,
        Vector2 position,
        Vector2 size,
        string text,
        Entity? parent = null,
        int? layer = null,
        UIButton? buttonStyle = null,
        Color? textColor = null,
        float fontSize = 14f)
    {
        var actualLayer = layer ?? (parent?.Contains<UILayer>() == true ? parent.Get<UILayer>().Value + 1 : 0);
        var actualTextColor = textColor ?? Color.Black;

        var buttonEntity = world.Create(HList.From(
            new UIElement(position, size, true, true),
            buttonStyle ?? new UIButton(),
            new UIStyle(),
            new UILayer(actualLayer),
            new UIState(),
            new UIEventListener(true, UIEventMask.Click | UIEventMask.Hover),
            new UILayout(LayoutType.None, SizeValue.Pixels(size.X), SizeValue.Pixels(size.Y), Vector4.Zero, new Vector4(8, 8, 8, 8), true),
            new UIComputedLayout(),
            new Node<UIHierarchyTag>(parent)
        ));

        // Create text child for the button
        var textEntity = CreateText(world, Vector2.Zero, text, actualTextColor, fontSize, buttonEntity);
        new UIEventListener.View(textEntity).IsEnabled = false; // Disable text interaction

        return buttonEntity;
    }

    #endregion

    #region Layout Containers

    public static Entity CreateFlexContainer(
        this World world,
        Vector2 position,
        Vector2 size,
        FlexDirection direction,
        float gap,
        Entity? parent = null,
        int? layer = null,
        UIStyle? style = null,
        Alignment justifyContent = Alignment.Start,
        Alignment alignItems = Alignment.Start)
    {
        var actualLayer = layer ?? (parent?.Contains<UILayer>() == true ? parent.Get<UILayer>().Value + 1 : 0);

        var entity = world.Create(HList.From(
            new UIElement(position, size, true, true),
            style ?? new UIStyle(),
            new UILayer(actualLayer),
            new UIState(),
            new UIEventListener(false, UIEventMask.None),
            new UILayout(LayoutType.Flex, SizeValue.Pixels(size.X), SizeValue.Pixels(size.Y), Vector4.Zero, Vector4.Zero, true),
            new UIComputedLayout(),
            new UIFlexContainer(direction, justifyContent, alignItems, gap),
            new Node<UIHierarchyTag>(parent)
        ));

        return entity;
    }

    public static Entity CreateScrollablePanel(
        this World world,
        Vector2 position,
        Vector2 size,
        ScrollDirection scrollDirection,
        Entity? parent = null,
        int layer = 0,
        UIStyle? style = null,
        Vector2? contentSize = null,
        float scrollSpeed = 20f)
    {
        var actualContentSize = contentSize ?? size;

        var entity = world.Create(HList.From(
            new UIElement(position, size, true, true),
            style ?? new UIStyle(),
            new UILayer(layer),
            new UIState(),
            new UIEventListener(true, UIEventMask.Scroll),
            new UILayout(LayoutType.None, SizeValue.Pixels(size.X), SizeValue.Pixels(size.Y), Vector4.Zero, new Vector4(10, 10, 10, 10), true),
            new UIComputedLayout(),
            new UIScrollable(actualContentSize, Vector2.Zero, scrollDirection, scrollSpeed),
            new Node<UIHierarchyTag>(parent)
        ));

        return entity;
    }

    #endregion
}