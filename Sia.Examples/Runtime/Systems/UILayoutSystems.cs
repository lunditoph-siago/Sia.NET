using System.Numerics;
using System.Runtime.CompilerServices;
using Sia.Examples.Runtime.Components;
using Sia.Reactors;

namespace Sia.Examples.Runtime.Systems;

public class UILayoutSystem() : SystemBase(Matchers.Of<UIElement, UILayout, Node<UIHierarchyTag>>())
{
    private readonly List<Entity> _autoResizeQueue = new(32);
    private readonly List<Entity> _positioningQueue = new(32);
    private readonly List<Entity> _childrenBuffer = new(16);

    public override void Execute(World world, IEntityQuery query)
    {
        CollectLayoutContainers(query);
        ProcessAutoResizePhase();
        ProcessPositioningPhase();
        ClearQueues();
    }

    private void CollectLayoutContainers(IEntityQuery query)
    {
        foreach (var entity in query)
        {
            var layout = entity.Get<UILayout>();

            if (layout.AutoResize)
                _autoResizeQueue.Add(entity);

            if (layout.Type != LayoutType.None)
                _positioningQueue.Add(entity);
        }

        // AutoResize: process children before parents (deep to shallow)
        _autoResizeQueue.Sort(static (a, b) => GetHierarchyDepth(b).CompareTo(GetHierarchyDepth(a)));

        // Positioning: process parents before children (shallow to deep)
        _positioningQueue.Sort(static (a, b) => GetHierarchyDepth(a).CompareTo(GetHierarchyDepth(b)));
    }

    private void ProcessAutoResizePhase()
    {
        foreach (var container in _autoResizeQueue)
        {
            if (!container.IsValid) continue;

            try
            {
                ProcessContainerAutoResize(container);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[UILayoutSystem] AutoResize error: {ex.Message}");
            }
        }
    }

    private void ProcessPositioningPhase()
    {
        foreach (var container in _positioningQueue)
        {
            if (!container.IsValid) continue;

            try
            {
                ProcessContainerPositioning(container);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[UILayoutSystem] Positioning error: {ex.Message}");
            }
        }
    }

    private void ProcessContainerAutoResize(Entity container)
    {
        ref readonly var layout = ref container.Get<UILayout>();

        CollectVisibleChildren(container);
        if (_childrenBuffer.Count == 0)
        {
            _childrenBuffer.Clear();
            return;
        }

        // Use infinite space for auto-resize calculations
        var infiniteSpace = new Vector2(float.MaxValue, float.MaxValue);
        var layoutResult = CalculateLayoutResult(infiniteSpace, layout);

        var padding = container.Contains<UIPadding>() ? container.Get<UIPadding>().Size : Vector2.Zero;
        var newSize = layoutResult.ContentSize + padding;

        if (container.Get<UIElement>().Size != newSize)
        {
            new UIElement.View(container).Size = newSize;
        }

        _childrenBuffer.Clear();
    }

    private void ProcessContainerPositioning(Entity container)
    {
        ref readonly var layout = ref container.Get<UILayout>();

        CollectVisibleChildren(container);
        if (_childrenBuffer.Count == 0)
        {
            _childrenBuffer.Clear();
            return;
        }

        var availableSpace = GetAvailableSpace(container);
        var layoutResult = CalculateLayoutResult(availableSpace, layout);

        ApplyPositions(container, layoutResult, layout);
        _childrenBuffer.Clear();
    }

    private LayoutResult CalculateLayoutResult(Vector2 availableSpace, UILayout layout)
    {
        return layout.Type switch
        {
            LayoutType.Vertical => CalculateVerticalLayout(availableSpace, layout),
            LayoutType.Horizontal => CalculateHorizontalLayout(availableSpace, layout),
            LayoutType.Absolute => CalculateAbsoluteLayout(availableSpace),
            LayoutType.Static => CalculateStaticLayout(),
            _ => new LayoutResult(Vector2.Zero, [])
        };
    }

    private void CollectVisibleChildren(Entity container)
    {
        _childrenBuffer.Clear();
        if (!container.Contains<Node<UIHierarchyTag>>()) return;

        foreach (var child in container.Get<Node<UIHierarchyTag>>().Children)
            if (child is { IsValid: true } && child.Contains<UIElement>() && child.Get<UIElement>().IsVisible)
                _childrenBuffer.Add(child);
    }

    private LayoutResult CalculateVerticalLayout(Vector2 availableSpace, UILayout layout)
    {
        var positions = new Vector2[_childrenBuffer.Count];
        var currentY = 0f;
        var maxWidth = 0f;

        for (var i = 0; i < _childrenBuffer.Count; i++)
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

        for (var i = 0; i < _childrenBuffer.Count; i++)
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

        for (var i = 0; i < _childrenBuffer.Count; i++)
        {
            var childElement = _childrenBuffer[i].Get<UIElement>();
            positions[i] = childElement.Position;
            var childMax = childElement.Position + childElement.Size;
            minBounds = Vector2.Min(minBounds, childElement.Position);
            maxBounds = Vector2.Max(maxBounds, childMax);
        }

        return new LayoutResult(maxBounds - minBounds, positions);
    }

    private LayoutResult CalculateStaticLayout()
    {
        var positions = new Vector2[_childrenBuffer.Count];
        var maxSize = Vector2.Zero;

        for (var i = 0; i < _childrenBuffer.Count; i++)
        {
            var childElement = _childrenBuffer[i].Get<UIElement>();
            positions[i] = Vector2.Zero;
            maxSize = Vector2.Max(maxSize, childElement.Size);
        }

        return new LayoutResult(maxSize, positions);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CalculateAlignment(LayoutAlignment alignment, float availableSize, float childSize)
    {
        return alignment switch
        {
            LayoutAlignment.Center => (availableSize - childSize) * 0.5f,
            LayoutAlignment.End => availableSize - childSize,
            _ => 0f
        };
    }

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

        var depth = 0;
        var current = entity.Get<Node<UIHierarchyTag>>().Parent;

        while (current is not null && depth < 100)
        {
            depth++;
            current = current.Contains<Node<UIHierarchyTag>>() ? current.Get<Node<UIHierarchyTag>>().Parent : null;
        }

        return depth;
    }

    private void ApplyPositions(Entity container, LayoutResult result, UILayout layout)
    {
        var containerPos = container.Get<UIElement>().Position;
        var paddingOffset = container.Contains<UIPadding>() ? container.Get<UIPadding>().TopLeft : Vector2.Zero;
        var basePosition = containerPos + paddingOffset;

        for (var i = 0; i < _childrenBuffer.Count && i < result.Positions.Length; i++)
        {
            var child = _childrenBuffer[i];
            var newPosition = basePosition + result.Positions[i];

            if (child.Get<UIElement>().Position != newPosition)
                new UIElement.View(child).Position = newPosition;
        }
    }

    private void ClearQueues()
    {
        _autoResizeQueue.Clear();
        _positioningQueue.Clear();
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
            {
                new UIScrollable.View(entity).ContentSize = contentSize;
            }
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
            if (child is not { IsValid: true } || !child.Contains<UIElement>()) continue;
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
    private readonly Dictionary<Entity, bool> _originalVisibilityStates = [];

    public override void Initialize(World world)
    {
        base.Initialize(world);
        RecordEvents<UIEvents>();
    }

    protected override void HandleEvent<TEvent>(Entity entity, in TEvent @event)
    {
        if (@event is UIEvents.VisibilityChanged { Target: var target, IsVisible: var isVisible })
            try
            {
                if (!target.IsValid) return;

                if (isVisible)
                    RestoreVisibilityToChildren(target);
                else
                    SaveAndHideChildren(target);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[UIVisibilitySystem] Visibility event error: {ex.Message}");
            }
    }

    private void SaveAndHideChildren(Entity parent)
    {
        if (parent is not { IsValid: true } || !parent.Contains<Node<UIHierarchyTag>>()) return;

        try
        {
            foreach (var child in parent.Get<Node<UIHierarchyTag>>().Children)
            {
                if (child is not { IsValid: true } || !child.Contains<UIElement>()) continue;

                var childElement = child.Get<UIElement>();
                _originalVisibilityStates[child] = childElement.IsVisible;

                if (childElement.IsVisible)
                    new UIElement.View(child).IsVisible = false;

                SaveAndHideChildren(child);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UIVisibilitySystem] Save children state error: {ex.Message}");
        }
    }

    private void RestoreVisibilityToChildren(Entity parent)
    {
        if (parent is not { IsValid: true } || !parent.Contains<Node<UIHierarchyTag>>()) return;

        try
        {
            foreach (var child in parent.Get<Node<UIHierarchyTag>>().Children)
            {
                if (child is not { IsValid: true } || !child.Contains<UIElement>()) continue;

                if (_originalVisibilityStates.TryGetValue(child, out var originalState))
                {
                    new UIElement.View(child).IsVisible = originalState;
                    _originalVisibilityStates.Remove(child);

                    if (originalState)
                        RestoreVisibilityToChildren(child);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UIVisibilitySystem] Restore children state error: {ex.Message}");
        }
    }
}