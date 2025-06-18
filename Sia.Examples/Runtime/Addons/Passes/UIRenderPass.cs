using System.Drawing;
using System.Numerics;
using Sia.Examples.Runtime.Components;
using Sia.Reactors;
using Silk.NET.OpenGL;
using SkiaSharp;

namespace Sia.Examples.Runtime.Addons.Passes;

public class UIRenderPass(int windowWidth, int windowHeight) : IRenderPass
{
    public string Name => "UI Render Pass";
    public int Priority => 1000; // UI renders last
    public bool IsEnabled { get; set; } = true;

    private GL _gl = null!;
    private int _windowWidth = windowWidth;
    private int _windowHeight = windowHeight;

    // SkiaSharp GL related resources
    private GRContext? _grContext;
    private GRBackendRenderTarget? _renderTarget;
    private SKSurface? _surface;
    private SKCanvas? _canvas;

    // Font and paint cache
    private readonly Dictionary<float, SKFont> _fontCache = [];
    private readonly Dictionary<uint, SKPaint> _paintCache = [];

    private readonly SKTypeface _typeface = SKTypeface.FromFamilyName("sans-serif") ??
                                            SKTypeface.Default;

    private readonly List<RenderElement> _renderQueue = new(128);

    public void Initialize(GL gl)
    {
        _gl = gl;
        InitializeSkiaGL();
    }

    public void Execute(World world, RenderPipeline pipeline)
    {
        if (_canvas is null || _grContext is null) return;

        try
        {
            BeginFrame();
            CollectRenderElements(world);
            SortRenderElements();
            RenderElements();
        }
        finally
        {
            EndFrame();
        }
    }

    public void OnResize(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;

        _surface?.Dispose();
        _renderTarget?.Dispose();
        CreateRenderTarget();
    }

    public void Dispose()
    {
        foreach (var font in _fontCache.Values)
            font.Dispose();
        _fontCache.Clear();

        foreach (var paint in _paintCache.Values)
            paint.Dispose();
        _paintCache.Clear();

        _surface?.Dispose();
        _renderTarget?.Dispose();
        _grContext?.Dispose();
        _typeface?.Dispose();
    }

    private void InitializeSkiaGL()
    {
        // Create Skia GL context
        var glInterface = GRGlInterface.Create();
        _grContext = GRContext.CreateGl(glInterface);

        if (_grContext is null)
            throw new InvalidOperationException("Failed to create Skia GL context");

        // Set GL context state
        _grContext.ResetContext();

        CreateRenderTarget();
    }

    private void CreateRenderTarget()
    {
        if (_grContext is null) return;

        // Get current framebuffer information
        _gl.GetInteger(GLEnum.FramebufferBinding, out var framebuffer);

        // Create backend render target
        var glInfo = new GRGlFramebufferInfo((uint)framebuffer, SKColorType.Rgba8888.ToGlSizedFormat());
        _renderTarget = new GRBackendRenderTarget(_windowWidth, _windowHeight, 0, 8, glInfo);

        // Create Skia surface
        _surface = SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888);
        _canvas = _surface?.Canvas;

        if (_canvas is null)
            throw new InvalidOperationException("Failed to create Skia canvas from GL context");
    }

    private void BeginFrame()
    {
        // Clear canvas
        _canvas!.Clear(SKColors.Transparent);

        // Save initial state
        _canvas.Save();

        // Convert OpenGL coordinate system: bottom-origin to top-origin
        _canvas.Scale(1, -1);
        _canvas.Translate(0, -_windowHeight);
    }

    private void EndFrame()
    {
        _canvas!.Restore();
        _grContext!.Flush();
    }

    private void CollectRenderElements(World world)
    {
        _renderQueue.Clear();

        var query = world.Query(Matchers.Of<UIElement>());

        foreach (var entity in query)
        {
            ref readonly var element = ref entity.Get<UIElement>();
            if (!element.IsVisible) continue;

            var layer = entity.Contains<UILayer>() ? entity.Get<UILayer>().Value : 0;
            var depth = CalculateHierarchyDepth(entity);

            _renderQueue.Add(new RenderElement(
                entity, layer, depth, element.Position, element.Size
            ));
        }
    }

    private static int CalculateHierarchyDepth(Entity entity)
    {
        if (!entity.Contains<Node<UIHierarchyTag>>()) return 0;

        var depth = 0;
        var current = entity.Get<Node<UIHierarchyTag>>().Parent;
        while (current is not null && depth < 100)
        {
            depth++;
            current = current.Contains<Node<UIHierarchyTag>>()
                ? current.Get<Node<UIHierarchyTag>>().Parent
                : null;
        }

        return depth;
    }

    // Sort by layer first, then by hierarchy depth (parents before children)
    private void SortRenderElements()
    {
        _renderQueue.Sort(static (a, b) =>
        {
            var layerComparison = a.Layer.CompareTo(b.Layer);
            return layerComparison != 0 ? layerComparison : a.HierarchyDepth.CompareTo(b.HierarchyDepth);
        });
    }

    private void RenderElements()
    {
        foreach (var renderElement in _renderQueue)
        {
            if (!IsInViewport(renderElement.WorldPosition, renderElement.Size))
                continue;

            RenderSingleElement(renderElement);
        }
    }

    private bool IsInViewport(Vector2 position, Vector2 size)
    {
        return position.X + size.X > 0 &&
               position.Y + size.Y > 0 &&
               position.X < _windowWidth &&
               position.Y < _windowHeight;
    }

    private void RenderSingleElement(RenderElement renderElement)
    {
        var entity = renderElement.Entity;
        if (!entity.IsValid) return;

        _canvas!.Save();
        try
        {
            _canvas.Translate(renderElement.WorldPosition.X, renderElement.WorldPosition.Y);

            if (entity.Contains<UIScrollable>())
                RenderScrollableElement(entity, renderElement.Size);
            else
                RenderStaticElement(entity, renderElement.Size);
        }
        finally
        {
            _canvas.Restore();
        }
    }

    private void RenderStaticElement(Entity entity, Vector2 size)
    {
        if (entity.Contains<UIStyle>()) RenderElementStyle(entity, size);

        if (entity.Contains<UIButton>()) RenderButtonState(entity, size);

        if (entity.Contains<UIText>()) RenderElementText(entity, size);
    }

    private void RenderScrollableElement(Entity entity, Vector2 size)
    {
        ref readonly var scrollable = ref entity.Get<UIScrollable>();

        var clipRect = new SKRect(0, 0, size.X, size.Y);
        _canvas!.ClipRect(clipRect);

        _canvas.Save();
        try
        {
            _canvas.Translate(-scrollable.ScrollOffset.X, -scrollable.ScrollOffset.Y);

            if (entity.Contains<UIStyle>())
            {
                _canvas.Save();
                _canvas.Translate(scrollable.ScrollOffset.X, scrollable.ScrollOffset.Y);
                RenderElementStyle(entity, size);
                _canvas.Restore();
            }

            if (entity.Contains<UIText>())
            {
                RenderScrollableText(entity, scrollable.ContentSize);
            }
        }
        finally
        {
            _canvas.Restore();
        }

        RenderScrollbars(entity, size, scrollable);
    }

    private void RenderElementStyle(Entity entity, Vector2 size)
    {
        ref readonly var style = ref entity.Get<UIStyle>();
        var rect = new SKRect(0, 0, size.X, size.Y);

        if (style.HasBackground)
        {
            var backgroundPaint = GetOrCreatePaint(style.BackgroundColor);
            if (style.HasRoundedCorners)
                _canvas!.DrawRoundRect(rect, style.CornerRadius, style.CornerRadius, backgroundPaint);
            else
                _canvas!.DrawRect(rect, backgroundPaint);
        }

        if (style.HasBorder)
        {
            var borderPaint = GetOrCreatePaint(style.BorderColor);
            borderPaint.Style = SKPaintStyle.Stroke;
            borderPaint.StrokeWidth = style.BorderWidth;

            if (style.HasRoundedCorners)
                _canvas!.DrawRoundRect(rect, style.CornerRadius, style.CornerRadius, borderPaint);
            else
                _canvas!.DrawRect(rect, borderPaint);
        }
    }

    private void RenderButtonState(Entity entity, Vector2 size)
    {
        ref readonly var button = ref entity.Get<UIButton>();
        var stateFlags = entity.Contains<UIState>() ? entity.Get<UIState>().Flags : UIStateFlags.None;
        var currentStyle = button.GetStyleForState(stateFlags);

        var rect = new SKRect(0, 0, size.X, size.Y);
        var paint = GetOrCreatePaint(currentStyle.BackgroundColor);

        _canvas!.DrawRoundRect(rect, currentStyle.CornerRadius, currentStyle.CornerRadius, paint);

        if ((stateFlags & UIStateFlags.Pressed) != 0)
        {
            var shadowPaint = GetOrCreatePaint(Color.FromArgb(50, 0, 0, 0));
            shadowPaint.Style = SKPaintStyle.Stroke;
            shadowPaint.StrokeWidth = 2;
            var innerRect = new SKRect(rect.Left + 1, rect.Top + 1, rect.Right - 1, rect.Bottom - 1);
            _canvas.DrawRoundRect(innerRect, currentStyle.CornerRadius - 1, currentStyle.CornerRadius - 1, shadowPaint);
        }
    }

    private void RenderElementText(Entity entity, Vector2 size)
    {
        ref readonly var text = ref entity.Get<UIText>();
        if (string.IsNullOrEmpty(text.Content)) return;

        var font = GetOrCreateFont(text.FontSize);
        var paint = GetOrCreatePaint(text.Color);

        font.MeasureText(text.Content, out var textBounds, paint);

        var x = text.Alignment switch
        {
            TextAlignment.Left => 0,
            TextAlignment.Center => (size.X - textBounds.Width) * 0.5f,
            TextAlignment.Right => size.X - textBounds.Width,
            _ => 0
        };

        var y = (size.Y - textBounds.Height) * 0.5f + textBounds.Height;

        _canvas!.DrawText(text.Content, x, y, SKTextAlign.Left, font, paint);
    }

    private void RenderScrollableText(Entity entity, Vector2 contentSize)
    {
        ref readonly var text = ref entity.Get<UIText>();
        if (string.IsNullOrEmpty(text.Content)) return;

        var font = GetOrCreateFont(text.FontSize);
        var paint = GetOrCreatePaint(text.Color);
        var lines = text.Content.Split('\n');
        var lineHeight = text.FontSize * 1.2f;

        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i])) continue;
            var y = i * lineHeight + text.FontSize;
            _canvas!.DrawText(lines[i], 0, y, SKTextAlign.Left, font, paint);
        }
    }

    private void RenderScrollbars(Entity entity, Vector2 viewportSize, UIScrollable scrollable)
    {
        var maxOffset = scrollable.GetMaxScrollOffset(viewportSize);

        if (scrollable.CanScroll(ScrollDirection.Vertical) && maxOffset.Y > 0)
            RenderVerticalScrollbar(viewportSize, scrollable, maxOffset);

        if (scrollable.CanScroll(ScrollDirection.Horizontal) && maxOffset.X > 0)
            RenderHorizontalScrollbar(viewportSize, scrollable, maxOffset);
    }

    private void RenderVerticalScrollbar(Vector2 viewportSize, UIScrollable scrollable, Vector2 maxOffset)
    {
        const float thumbWidth = 4f;
        const float margin = 2f;

        var thumbHeight = Math.Max(30f, viewportSize.Y * (viewportSize.Y / scrollable.ContentSize.Y));
        var thumbPosition = maxOffset.Y > 0
            ? scrollable.ScrollOffset.Y / maxOffset.Y * (viewportSize.Y - thumbHeight)
            : 0;

        var thumbRect = new SKRect(
            viewportSize.X - thumbWidth - margin,
            thumbPosition,
            viewportSize.X - margin,
            thumbPosition + thumbHeight);

        var thumbPaint = GetOrCreatePaint(Color.FromArgb(120, 100, 100, 100));
        _canvas!.DrawRoundRect(thumbRect, thumbWidth / 2, thumbWidth / 2, thumbPaint);
    }

    private void RenderHorizontalScrollbar(Vector2 viewportSize, UIScrollable scrollable, Vector2 maxOffset)
    {
        const float thumbHeight = 4f;
        const float margin = 2f;

        var thumbWidth = Math.Max(30f, viewportSize.X * (viewportSize.X / scrollable.ContentSize.X));
        var thumbPosition = maxOffset.X > 0
            ? scrollable.ScrollOffset.X / maxOffset.X * (viewportSize.X - thumbWidth)
            : 0;

        var thumbRect = new SKRect(
            thumbPosition,
            viewportSize.Y - thumbHeight - margin,
            thumbPosition + thumbWidth,
            viewportSize.Y - margin);

        var thumbPaint = GetOrCreatePaint(Color.FromArgb(120, 100, 100, 100));
        _canvas!.DrawRoundRect(thumbRect, thumbHeight / 2, thumbHeight / 2, thumbPaint);
    }

    private SKFont GetOrCreateFont(float fontSize)
    {
        if (_fontCache.TryGetValue(fontSize, out var font))
            return font;

        font = new SKFont(_typeface, fontSize)
        {
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias
        };

        _fontCache[fontSize] = font;
        return font;
    }

    private SKPaint GetOrCreatePaint(Color color)
    {
        var colorKey = (uint)color.ToArgb();
        if (_paintCache.TryGetValue(colorKey, out var paint))
            return paint;

        paint = new SKPaint
        {
            Color = new SKColor(color.R, color.G, color.B, color.A),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _paintCache[colorKey] = paint;
        return paint;
    }

    private readonly record struct RenderElement(
        Entity Entity,
        int Layer,
        int HierarchyDepth,
        Vector2 WorldPosition,
        Vector2 Size
    );
}