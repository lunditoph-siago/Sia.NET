using System.Numerics;
using System.Runtime.CompilerServices;
using Sia.Examples.Runtime.Components;
using Sia.Reactors;

namespace Sia.Examples.Runtime.Systems;

public class UILayoutSystem() : SystemBase(Matchers.Of<UIElement, UILayout, Node<UIHierarchyTag>>())
{
    private readonly List<Entity> _layoutQueue = new(32);
    private readonly List<Entity> _childrenBuffer = new(16);

    public override void Execute(World world, IEntityQuery query)
    {
        CollectLayoutContainers(query);
        ProcessLayoutQueue();
        _layoutQueue.Clear();
    }

    private void CollectLayoutContainers(IEntityQuery query)
    {
        foreach (var entity in query)
        {
            if (entity.Get<UILayout>().Type != LayoutType.None)
                _layoutQueue.Add(entity);
        }
        _layoutQueue.Sort(static (a, b) => GetHierarchyDepth(a).CompareTo(GetHierarchyDepth(b)));
    }

    private void ProcessLayoutQueue()
    {
        foreach (var container in _layoutQueue)
        {
            if (!container.IsValid) continue;

            try
            {
                ProcessSingleContainer(container);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UILayoutSystem] Error processing container: {ex.Message}");
            }
        }
    }

    private void ProcessSingleContainer(Entity container)
    {
        ref readonly var layout = ref container.Get<UILayout>();
        CollectVisibleChildren(container);
        if (_childrenBuffer.Count == 0)
        {
            _childrenBuffer.Clear();
            return;
        }

        var availableSpace = GetAvailableSpace(container);
        var layoutResult = layout.Type switch
        {
            LayoutType.Vertical => CalculateVerticalLayout(availableSpace, layout),
            LayoutType.Horizontal => CalculateHorizontalLayout(availableSpace, layout),
            LayoutType.Absolute => CalculateAbsoluteLayout(availableSpace),
            _ => new LayoutResult(Vector2.Zero, [])
        };

        ApplyLayoutResult(container, layoutResult, layout);
        _childrenBuffer.Clear();
    }

    private void CollectVisibleChildren(Entity container)
    {
        _childrenBuffer.Clear();
        if (!container.Contains<Node<UIHierarchyTag>>()) return;

        foreach (var child in container.Get<Node<UIHierarchyTag>>().Children)
        {
            if (child.IsValid && child.Contains<UIElement>() && child.Get<UIElement>().IsVisible)
                _childrenBuffer.Add(child);
        }
    }

    private LayoutResult CalculateVerticalLayout(Vector2 availableSpace, UILayout layout)
    {
        var positions = new Vector2[_childrenBuffer.Count];
        var currentY = 0f;
        var maxWidth = 0f;

        for (int i = 0; i < _childrenBuffer.Count; i++)
        {
            var childSize = GetChildSize(_childrenBuffer[i], availableSpace);
            positions[i] = new Vector2(CalculateAlignment(layout.Alignment, availableSpace.X, childSize.X), currentY);
            currentY += childSize.Y + layout.Spacing.Y;
            maxWidth = Math.Max(maxWidth, childSize.X);
        }

        if (_childrenBuffer.Count > 0) currentY -= layout.Spacing.Y;
        return new LayoutResult(new Vector2(maxWidth, currentY), positions);
    }

    private LayoutResult CalculateHorizontalLayout(Vector2 availableSpace, UILayout layout)
    {
        var positions = new Vector2[_childrenBuffer.Count];
        var currentX = 0f;
        var maxHeight = 0f;

        for (int i = 0; i < _childrenBuffer.Count; i++)
        {
            var childSize = GetChildSize(_childrenBuffer[i], availableSpace);
            positions[i] = new Vector2(currentX, CalculateAlignment(layout.Alignment, availableSpace.Y, childSize.Y));
            currentX += childSize.X + layout.Spacing.X;
            maxHeight = Math.Max(maxHeight, childSize.Y);
        }

        if (_childrenBuffer.Count > 0) currentX -= layout.Spacing.X;
        return new LayoutResult(new Vector2(currentX, maxHeight), positions);
    }

    private LayoutResult CalculateAbsoluteLayout(Vector2 availableSpace)
    {
        var positions = new Vector2[_childrenBuffer.Count];
        var minBounds = Vector2.Zero;
        var maxBounds = Vector2.Zero;

        for (int i = 0; i < _childrenBuffer.Count; i++)
        {
            var childElement = _childrenBuffer[i].Get<UIElement>();
            positions[i] = childElement.Position;
            var childMax = childElement.Position + childElement.Size;
            minBounds = Vector2.Min(minBounds, childElement.Position);
            maxBounds = Vector2.Max(maxBounds, childMax);
        }

        return new LayoutResult(maxBounds - minBounds, positions);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CalculateAlignment(LayoutAlignment alignment, float availableSize, float childSize) => alignment switch
    {
        LayoutAlignment.Center => (availableSize - childSize) * 0.5f,
        LayoutAlignment.End => availableSize - childSize,
        _ => 0f
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 GetChildSize(Entity child, Vector2 availableSpace)
    {
        var baseSize = child.Get<UIElement>().Size;
        return child.Contains<UILayoutConstraints>()
            ? child.Get<UILayoutConstraints>().GetActualSize(availableSpace, baseSize)
            : baseSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 GetAvailableSpace(Entity container)
    {
        var availableSpace = container.Get<UIElement>().Size;
        if (container.Contains<UIPadding>())
            availableSpace -= container.Get<UIPadding>().Size;
        return Vector2.Max(Vector2.Zero, availableSpace);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetHierarchyDepth(Entity entity)
    {
        if (!entity.Contains<Node<UIHierarchyTag>>()) return 0;
        int depth = 0;
        var current = entity.Get<Node<UIHierarchyTag>>().Parent;
        while (current != null && depth < 100)
        {
            depth++;
            current = current.Contains<Node<UIHierarchyTag>>() ? current.Get<Node<UIHierarchyTag>>().Parent : null;
        }
        return depth;
    }

    private void ApplyLayoutResult(Entity container, LayoutResult result, UILayout layout)
    {
        var containerPos = container.Get<UIElement>().Position;
        var paddingOffset = container.Contains<UIPadding>() ? container.Get<UIPadding>().TopLeft : Vector2.Zero;

        for (int i = 0; i < _childrenBuffer.Count && i < result.Positions.Length; i++)
        {
            var child = _childrenBuffer[i];
            var newPosition = containerPos + paddingOffset + result.Positions[i];
            if (child.Get<UIElement>().Position != newPosition)
                new UIElement.View(child).Position = newPosition;
        }

        if (layout.AutoResize)
        {
            var padding = container.Contains<UIPadding>() ? container.Get<UIPadding>().Size : Vector2.Zero;
            var newSize = result.ContentSize + padding;
            if (container.Get<UIElement>().Size != newSize)
                new UIElement.View(container).Size = newSize;
        }
    }

    private readonly record struct LayoutResult(Vector2 ContentSize, Vector2[] Positions);
}

public class UIScrollContentSizeSystem() : SystemBase(Matchers.Of<UIElement, UIScrollable, Node<UIHierarchyTag>>())
{
    public override void Execute(World world, IEntityQuery query)
    {
        query.ForSlice(static (Entity entity, ref UIElement element, ref UIScrollable scrollable) =>
        {
            var contentSize = CalculateContentBounds(entity);
            if (contentSize != scrollable.ContentSize)
                new UIScrollable.View(entity).ContentSize = contentSize;
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 CalculateContentBounds(Entity scrollContainer)
    {
        if (!scrollContainer.Contains<Node<UIHierarchyTag>>()) return Vector2.Zero;
        var children = scrollContainer.Get<Node<UIHierarchyTag>>().Children;
        if (children.Count == 0) return Vector2.Zero;

        var minPos = new Vector2(float.MaxValue);
        var maxPos = new Vector2(float.MinValue);
        var hasValidChild = false;

        foreach (var child in children)
        {
            if (!child.IsValid || !child.Contains<UIElement>()) continue;
            var childElement = child.Get<UIElement>();
            if (!childElement.IsVisible) continue;

            minPos = Vector2.Min(minPos, childElement.Position);
            maxPos = Vector2.Max(maxPos, childElement.Position + childElement.Size);
            hasValidChild = true;
        }

        return hasValidChild ? maxPos - minPos : Vector2.Zero;
    }
}

public class UIVisibilitySystem : EventSystemBase
{
    public override void Initialize(World world)
    {
        base.Initialize(world);
        RecordEvents<UIEvents>();
    }

    protected override void HandleEvent<TEvent>(Entity entity, in TEvent @event)
    {
        if (@event is UIEvents.VisibilityChanged visibilityChanged)
            PropagateVisibilityToChildren(visibilityChanged.Target, visibilityChanged.IsVisible);
    }

    private static void PropagateVisibilityToChildren(Entity parent, bool isVisible)
    {
        if (!parent.Contains<Node<UIHierarchyTag>>()) return;

        foreach (var child in parent.Get<Node<UIHierarchyTag>>().Children)
        {
            if (!child.Contains<UIElement>()) continue;
            var childElement = child.Get<UIElement>();
            var shouldBeVisible = isVisible && childElement.IsVisible;

            if (childElement.IsVisible != shouldBeVisible)
            {
                new UIElement.View(child).IsVisible = shouldBeVisible;
                PropagateVisibilityToChildren(child, shouldBeVisible);
            }
        }
    }
}