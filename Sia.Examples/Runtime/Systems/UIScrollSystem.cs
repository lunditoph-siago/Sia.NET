using System.Drawing;
using System.Numerics;
using Sia.Examples.Runtime.Components;
using Sia.Reactors;

namespace Sia.Examples.Runtime.Systems;

public class UIScrollSystem() : SystemBase(
    Matchers.Of<UIElement, UIScrollable, UIText>(),
    EventUnion.Of<UIScrollable.SetContentSize, UIText.SetContent>())
{
    public override void Execute(World world, IEntityQuery query)
    {
        foreach (var entity in query)
        {
            ref readonly var text = ref entity.Get<UIText>();
            ref readonly var uiElement = ref entity.Get<UIElement>();
            ref var scrollable = ref entity.Get<UIScrollable>();

            var lines = text.Content?.Split('\n') ?? [];
            var fontSize = text.FontSize;
            var contentSize = new Vector2(
                Math.Max(lines.Max(l => l.Length * fontSize * 0.6f), uiElement.Size.X),
                Math.Max(lines.Length * fontSize * 1.2f, uiElement.Size.Y)
            );

            if (scrollable.ContentSize != contentSize)
            {
                scrollable = scrollable with
                {
                    ContentSize = contentSize,
                    ScrollOffset = scrollable.ClampScrollOffset(scrollable.ScrollOffset, uiElement.Size)
                };
            }
        }
    }
}