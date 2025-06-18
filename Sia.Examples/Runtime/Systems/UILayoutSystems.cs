using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sia.Examples.Runtime.Components;
using Sia.Reactors;

namespace Sia.Examples.Runtime.Systems;

public sealed class UILayoutReactor : ReactorBase
{
    private readonly HashSet<Entity> _dirtyElements = [];

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);

        Listen<UILayoutEvents.LayoutInvalidated>(OnLayoutInvalidated);

        Listen((Entity entity, in UILayout.SetWidth _) => InvalidateLayout(entity));
        Listen((Entity entity, in UILayout.SetHeight _) => InvalidateLayout(entity));
        Listen((Entity entity, in UILayout.SetMargin _) => InvalidateLayout(entity));
        Listen((Entity entity, in UILayout.SetPadding _) => InvalidateLayout(entity));

        Listen((Entity entity, in UIFlexContainer.SetDirection _) => InvalidateWithChildren(entity));
        Listen((Entity entity, in UIFlexContainer.SetJustifyContent _) => InvalidateWithChildren(entity));
        Listen((Entity entity, in UIFlexContainer.SetAlignItems _) => InvalidateWithChildren(entity));
        Listen((Entity entity, in UIFlexContainer.SetGap _) => InvalidateWithChildren(entity));

        Listen((Entity entity, in UIFlexItem.SetGrow _) => InvalidateParent(entity));
        Listen((Entity entity, in UIFlexItem.SetShrink _) => InvalidateParent(entity));
        Listen((Entity entity, in UIFlexItem.SetBasis _) => InvalidateParent(entity));

        Listen((Entity entity, in Node<UIHierarchyTag>.ChildAdded evt) =>
        {
            InvalidateLayout(entity);
            InvalidateLayout(evt.Entity);
        });
        Listen((Entity entity, in Node<UIHierarchyTag>.ChildRemoved _) => InvalidateLayout(entity));
    }

    private bool OnLayoutInvalidated(Entity entity, in UILayoutEvents.LayoutInvalidated e)
    {
        _dirtyElements.Add(e.Target);
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InvalidateLayout(Entity entity)
    {
        if (entity.IsValid && entity.Contains<UILayout>())
            entity.Execute(new UILayout.InvalidateLayout());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InvalidateWithChildren(Entity entity)
    {
        InvalidateLayout(entity);
        if (!entity.Contains<Node<UIHierarchyTag>>()) return;

        foreach (var child in entity.Get<Node<UIHierarchyTag>>().Children)
            InvalidateLayout(child);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InvalidateParent(Entity entity)
    {
        if (!entity.Contains<Node<UIHierarchyTag>>()) return;

        var parent = entity.Get<Node<UIHierarchyTag>>().Parent;
        if (parent is not null) InvalidateLayout(parent);
    }

    public IReadOnlySet<Entity> GetDirtyElements() => _dirtyElements;
    public void ClearDirtyElements() => _dirtyElements.Clear();
}

public sealed class UILayoutComputeSystem() : SystemBase(
    Matchers.Of<UILayout, UIElement>(),
    EventUnion.Of<UILayoutEvents.LayoutInvalidated>())
{
    private UILayoutReactor? _layoutReactor;

    public override void Initialize(World world)
    {
        base.Initialize(world);
        _layoutReactor = world.GetAddon<UILayoutReactor>();
    }

    public override void Execute(World world, IEntityQuery query)
    {
        if (_layoutReactor?.GetDirtyElements() is not { Count: > 0 } dirtyElements) return;

        var roots = GetRootElements(dirtyElements);
        foreach (var root in roots)
            ComputeLayoutRecursive(root, LayoutConstraints.Unconstrained);

        _layoutReactor.ClearDirtyElements();
    }

    private List<Entity> GetRootElements(IReadOnlySet<Entity> dirtyElements)
    {
        List<Entity> roots = [];
        foreach (var entity in dirtyElements)
        {
            if (!entity.IsValid || !entity.Contains<Node<UIHierarchyTag>>()) continue;

            var parent = entity.Get<Node<UIHierarchyTag>>().Parent;
            if (parent is null || !dirtyElements.Contains(parent))
                roots.Add(entity);
        }
        return roots;
    }

    private void ComputeLayoutRecursive(Entity entity, LayoutConstraints constraints)
    {
        if (!entity.IsValid || !entity.Contains<UILayout>()) return;

        ref var layout = ref entity.Get<UILayout>();
        if (!layout.NeedsLayout) return;

        var computedSize = ComputeElementLayout(entity, constraints);

        if (entity.Contains<UIElement>())
            new UIElement.View(entity).Size = computedSize;

        UpdateComputedLayout(entity, computedSize);
        new UILayout.View(entity).NeedsLayout = false;

        entity.Send(new UILayoutEvents.LayoutComputed(entity, computedSize));

        if (!entity.Contains<Node<UIHierarchyTag>>()) return;

        var contentConstraints = GetContentConstraints(entity, computedSize);
        var children = GetValidChildren(entity);

        var parentPosition = entity.Contains<UIElement>() ? entity.Get<UIElement>().Position : Vector2.Zero;
        var contentOffset = new Vector2(layout.Padding.X, layout.Padding.Y);
        var contentStartPosition = parentPosition + contentOffset;

        foreach (var child in children)
        {
            if (!child.IsValid) continue;

            if (layout.Type == LayoutType.Flex)
            {
                if (child.Contains<UILayout>())
                    new UILayout.View(child).NeedsLayout = false;
            }
            else
            {
                if (child.Contains<UIElement>())
                {
                    ref readonly var childElement = ref child.Get<UIElement>();
                    var relativePosition = childElement.Position;
                    var absolutePosition = contentStartPosition + relativePosition;

                    Console.WriteLine($"[Layout Debug] Child {child}: RelativePos={relativePosition}, ParentContentStart={contentStartPosition}, AbsolutePos={absolutePosition}");

                    new UIElement.View(child).Position = absolutePosition;
                }

                ComputeLayoutRecursive(child, contentConstraints);

                if (child.IsValid && child.Contains<UILayout>())
                    new UILayout.View(child).NeedsLayout = false;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector2 ComputeElementLayout(Entity entity, LayoutConstraints constraints)
    {
        ref readonly var layout = ref entity.Get<UILayout>();

        return layout.Type switch
        {
            LayoutType.Flex when entity.Contains<UIFlexContainer>() => ComputeFlexLayout(entity, constraints),
            LayoutType.Absolute => ComputeBasicLayout(entity, constraints),
            _ => ComputeBasicLayout(entity, constraints)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector2 ComputeBasicLayout(Entity entity, LayoutConstraints constraints)
    {
        ref readonly var layout = ref entity.Get<UILayout>();

        var width = layout.Width.Unit == SizeUnit.Auto
            ? MeasureContentWidth(entity)
            : layout.Width.Resolve(constraints.AvailableSize.X);
        var height = layout.Height.Unit == SizeUnit.Auto
            ? MeasureContentHeight(entity)
            : layout.Height.Resolve(constraints.AvailableSize.Y);

        return new Vector2(width, height);
    }

    private Vector2 ComputeFlexLayout(Entity entity, LayoutConstraints constraints)
    {
        var children = GetValidChildren(entity);
        if (children.Count == 0) return ComputeBasicLayout(entity, constraints);

        ref readonly var layout = ref entity.Get<UILayout>();
        ref readonly var flexContainer = ref entity.Get<UIFlexContainer>();
        var isRow = flexContainer.IsRowDirection();

        var containerWidth = layout.Width.Unit == SizeUnit.Auto
            ? constraints.AvailableSize.X
            : layout.Width.Resolve(constraints.AvailableSize.X);
        var containerHeight = layout.Height.Unit == SizeUnit.Auto
            ? constraints.AvailableSize.Y
            : layout.Height.Resolve(constraints.AvailableSize.Y);

        var containerSize = new Vector2(containerWidth, containerHeight);
        var contentSize = layout.GetContentSize(containerSize);

        var mainAxisSize = isRow ? contentSize.X : contentSize.Y;
        var crossAxisSize = isRow ? contentSize.Y : contentSize.X;

        var flexItems = new FlexItemData[children.Count];
        var totalGapSize = flexContainer.Gap * Math.Max(0, children.Count - 1);
        var usedMainSize = totalGapSize;

        for (int i = 0; i < children.Count; i++)
        {
            flexItems[i] = CalculateFlexItemBasics(children[i], constraints, isRow);
            usedMainSize += flexItems[i].MainSize;
        }

        var remainingSpace = mainAxisSize - usedMainSize;
        if (remainingSpace > 0)
            DistributeRemainingSpace(flexItems, remainingSpace);
        else if (remainingSpace < 0)
            ShrinkOverflowingItems(flexItems, -remainingSpace);

        CalculateCrossAxisSizes(flexItems, crossAxisSize, flexContainer.AlignItems, isRow);
        PositionFlexItems(entity, flexItems, flexContainer, contentSize, isRow);

        var actualMainSize = Math.Max(mainAxisSize, usedMainSize);

        if (layout.Width.Unit == SizeUnit.Auto || layout.Height.Unit == SizeUnit.Auto)
        {
            var maxCrossSize = GetMaxCrossSize(flexItems);
            if (layout.Width.Unit == SizeUnit.Auto)
                containerWidth = isRow
                    ? actualMainSize + layout.Padding.X + layout.Padding.Z
                    : maxCrossSize + layout.Padding.X + layout.Padding.Z;
            if (layout.Height.Unit == SizeUnit.Auto)
                containerHeight = isRow
                    ? maxCrossSize + layout.Padding.Y + layout.Padding.W
                    : actualMainSize + layout.Padding.Y + layout.Padding.W;
        }

        return new Vector2(containerWidth, containerHeight);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetMaxCrossSize(FlexItemData[] flexItems)
    {
        if (flexItems.Length == 0) return 0f;

        var max = flexItems[0].CrossSize;
        for (int i = 1; i < flexItems.Length; i++)
        {
            if (flexItems[i].CrossSize > max)
                max = flexItems[i].CrossSize;
        }
        return max;
    }

    private readonly record struct FlexItemData(
        Entity Entity,
        float MainSize,
        float CrossSize,
        float FlexBasis,
        float FlexGrow,
        float FlexShrink)
    {
        public FlexItemData WithMainSize(float mainSize) => this with { MainSize = mainSize };
        public FlexItemData WithCrossSize(float crossSize) => this with { CrossSize = crossSize };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private FlexItemData CalculateFlexItemBasics(Entity child, LayoutConstraints constraints, bool isRow)
    {
        ref readonly var childLayout = ref child.Get<UILayout>();

        var flexGrow = 0f;
        var flexShrink = 1f;
        var flexBasis = 0f;

        if (child.Contains<UIFlexItem>())
        {
            ref readonly var flexItem = ref child.Get<UIFlexItem>();
            flexGrow = flexItem.Grow;
            flexShrink = flexItem.Shrink;
            flexBasis = flexItem.Basis.Unit == SizeUnit.Auto
                ? 0f
                : flexItem.Basis.Resolve(isRow ? constraints.AvailableSize.X : constraints.AvailableSize.Y);
        }

        var basicSize = ComputeBasicLayout(child, constraints);

        var mainSize = flexBasis > 0
            ? flexBasis
            : GetAxisSize(childLayout, basicSize, constraints, isRow, true);

        var crossSize = GetAxisSize(childLayout, basicSize, constraints, isRow, false);

        return new FlexItemData(child, mainSize, crossSize, flexBasis, flexGrow, flexShrink);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetAxisSize(in UILayout layout, Vector2 basicSize, LayoutConstraints constraints,
        bool isRow, bool isMainAxis)
    {
        var isMainAxisSize = isRow == isMainAxis;
        var sizeValue = isMainAxisSize ? layout.Width : layout.Height;
        var constraintSize = isMainAxisSize ? constraints.AvailableSize.X : constraints.AvailableSize.Y;
        var basicAxisSize = isMainAxisSize ? basicSize.X : basicSize.Y;

        return sizeValue.Unit == SizeUnit.Auto
            ? basicAxisSize
            : sizeValue.Resolve(constraintSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DistributeRemainingSpace(FlexItemData[] flexItems, float remainingSpace)
    {
        var totalFlexGrow = 0f;
        for (int i = 0; i < flexItems.Length; i++)
            totalFlexGrow += flexItems[i].FlexGrow;

        if (totalFlexGrow <= 0f) return;

        var invTotalGrow = 1f / totalFlexGrow;
        for (int i = 0; i < flexItems.Length; i++)
        {
            if (flexItems[i].FlexGrow > 0f)
            {
                var growSpace = remainingSpace * flexItems[i].FlexGrow * invTotalGrow;
                flexItems[i] = flexItems[i].WithMainSize(flexItems[i].MainSize + growSpace);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ShrinkOverflowingItems(FlexItemData[] flexItems, float overflowSpace)
    {
        var totalWeightedBasis = 0f;
        for (int i = 0; i < flexItems.Length; i++)
            totalWeightedBasis += flexItems[i].FlexShrink * flexItems[i].MainSize;

        if (totalWeightedBasis <= 0f) return;

        var invTotalBasis = 1f / totalWeightedBasis;
        for (int i = 0; i < flexItems.Length; i++)
        {
            if (flexItems[i].FlexShrink > 0f)
            {
                var scaledShrinkFactor = flexItems[i].FlexShrink * flexItems[i].MainSize;
                var shrinkSpace = overflowSpace * scaledShrinkFactor * invTotalBasis;
                flexItems[i] = flexItems[i].WithMainSize(Math.Max(0f, flexItems[i].MainSize - shrinkSpace));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CalculateCrossAxisSizes(FlexItemData[] flexItems, float containerCrossSize,
        Alignment alignItems, bool isRow)
    {
        if (alignItems != Alignment.Stretch) return;

        for (int i = 0; i < flexItems.Length; i++)
        {
            ref readonly var layout = ref flexItems[i].Entity.Get<UILayout>();
            var crossAxisValue = isRow ? layout.Height : layout.Width;

            if (crossAxisValue.Unit == SizeUnit.Auto)
                flexItems[i] = flexItems[i].WithCrossSize(containerCrossSize);
        }
    }

    private static void PositionFlexItems(Entity container, FlexItemData[] flexItems,
        in UIFlexContainer flexContainer, Vector2 contentSize, bool isRow)
    {
        if (flexItems.Length == 0) return;

        var containerPos = container.Contains<UIElement>()
            ? container.Get<UIElement>().Position
            : Vector2.Zero;
        ref readonly var layout = ref container.Get<UILayout>();
        var contentStart = containerPos + new Vector2(layout.Padding.X, layout.Padding.Y);

        var (mainAxisStart, mainAxisSpacing) = CalculateMainAxisDistribution(
            flexItems, flexContainer, contentSize, isRow);

        var currentMainPos = mainAxisStart;
        for (int i = 0; i < flexItems.Length; i++)
        {
            var item = flexItems[i];
            var crossAxisStart = CalculateCrossAxisPosition(
                item.CrossSize, isRow ? contentSize.Y : contentSize.X, flexContainer.AlignItems);

            var (itemPosition, itemSize) = isRow switch
            {
                true => (contentStart + new Vector2(currentMainPos, crossAxisStart),
                        new Vector2(item.MainSize, item.CrossSize)),
                false => (contentStart + new Vector2(crossAxisStart, currentMainPos),
                         new Vector2(item.CrossSize, item.MainSize))
            };

            if (item.Entity.Contains<UIElement>())
            {
                var elementView = new UIElement.View(item.Entity);
                elementView.Position = itemPosition;
                elementView.Size = itemSize;
            }

            if (item.Entity.Contains<UIComputedLayout>())
            {
                var computedView = new UIComputedLayout.View(item.Entity);
                computedView.Position = itemPosition;
                computedView.Size = itemSize;
            }

            currentMainPos += item.MainSize + mainAxisSpacing;

            if (i < flexItems.Length - 1)
                currentMainPos += flexContainer.Gap;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (float start, float spacing) CalculateMainAxisDistribution(
        FlexItemData[] flexItems, in UIFlexContainer flexContainer, Vector2 contentSize, bool isRow)
    {
        var totalItemsMainSize = 0f;
        for (int i = 0; i < flexItems.Length; i++)
            totalItemsMainSize += flexItems[i].MainSize;

        var totalGaps = flexContainer.Gap * Math.Max(0, flexItems.Length - 1);
        var usedMainSize = totalItemsMainSize + totalGaps;
        var mainAxisSize = isRow ? contentSize.X : contentSize.Y;
        var remainingMainSpace = Math.Max(0, mainAxisSize - usedMainSize);

        return flexContainer.JustifyContent switch
        {
            Alignment.Start => (0f, 0f),
            Alignment.Center => (remainingMainSpace * 0.5f, 0f),
            Alignment.End => (remainingMainSpace, 0f),
            Alignment.Stretch when flexItems.Length > 1 => (0f, remainingMainSpace / (flexItems.Length - 1)),
            _ => (0f, 0f)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CalculateCrossAxisPosition(float itemCrossSize, float containerCrossSize, Alignment alignItems)
        => alignItems switch
        {
            Alignment.Start or Alignment.Stretch => 0f,
            Alignment.Center => (containerCrossSize - itemCrossSize) * 0.5f,
            Alignment.End => containerCrossSize - itemCrossSize,
            _ => 0f
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float MeasureContentWidth(Entity entity)
        => entity.Contains<UIText>()
            ? entity.Get<UIText>().Content.Length * entity.Get<UIText>().FontSize * 0.6f
            : 0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float MeasureContentHeight(Entity entity)
        => entity.Contains<UIText>()
            ? entity.Get<UIText>().FontSize * 1.2f
            : 0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<Entity> GetValidChildren(Entity parent)
    {
        if (!parent.Contains<Node<UIHierarchyTag>>()) return [];

        List<Entity> children = [];
        foreach (var child in parent.Get<Node<UIHierarchyTag>>().Children)
        {
            if (child.IsValid && child.Contains<UILayout>() && child.Contains<UIElement>())
                children.Add(child);
        }
        return children;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateComputedLayout(Entity entity, Vector2 size)
    {
        if (!entity.IsValid) return;

        ref readonly var layout = ref entity.Get<UILayout>();
        var position = entity.Contains<UIElement>() ? entity.Get<UIElement>().Position : Vector2.Zero;
        var contentPosition = position + new Vector2(layout.Padding.X, layout.Padding.Y);
        var contentSize = layout.GetContentSize(size);

        if (entity.Contains<UIComputedLayout>())
        {
            var computedLayoutView = new UIComputedLayout.View(entity);
            computedLayoutView.Position = position;
            computedLayoutView.Size = size;
            computedLayoutView.ContentPosition = contentPosition;
            computedLayoutView.ContentSize = contentSize;
        }
        else
        {
            try
            {
                var computedLayout = new UIComputedLayout
                {
                    Position = position,
                    Size = size,
                    ContentPosition = contentPosition,
                    ContentSize = contentSize
                };
                entity.Add(computedLayout);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Layout Error]: Failed to add UIComputedLayout to entity {entity}: {ex.Message}");
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LayoutConstraints GetContentConstraints(Entity entity, Vector2 containerSize)
    {
        ref readonly var layout = ref entity.Get<UILayout>();
        return new LayoutConstraints(layout.GetContentSize(containerSize));
    }
}