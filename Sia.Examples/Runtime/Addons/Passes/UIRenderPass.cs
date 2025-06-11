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

    public void Initialize(GL gl)
    {
        _gl = gl;
        InitializeSkiaGL();
    }

    private void InitializeSkiaGL()
    {
        // Create Skia GL context
        var glInterface = GRGlInterface.Create();
        _grContext = GRContext.CreateGl(glInterface);

        if (_grContext == null)
        {
            throw new InvalidOperationException("Failed to create Skia GL context");
        }

        // Set GL context state
        _grContext.ResetContext();

        CreateRenderTarget();
    }

    private void CreateRenderTarget()
    {
        if (_grContext == null) return;

        // Get current framebuffer information
        _gl.GetInteger(GLEnum.FramebufferBinding, out var framebuffer);

        // Create backend render target
        var glInfo = new GRGlFramebufferInfo((uint)framebuffer, SKColorType.Rgba8888.ToGlSizedFormat());
        _renderTarget = new GRBackendRenderTarget(_windowWidth, _windowHeight, 0, 8, glInfo);

        // Create Skia surface
        _surface = SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888);
        _canvas = _surface?.Canvas;

        if (_canvas == null)
        {
            throw new InvalidOperationException("Failed to create Skia canvas from GL context");
        }
    }

    public void Execute(World world, RenderPipeline pipeline)
    {
        if (_canvas == null || _grContext == null) return;

        BeginFrame();

        try
        {
            var sortedUIElements = GetSortedUIElements(world);
            RenderUIElements(sortedUIElements);
        }
        finally
        {
            EndFrame();
        }
    }

    private List<Entity> GetSortedUIElements(World world)
    {
        var uiElements = new List<Entity>();

        // Query root nodes first, then traverse the hierarchy
        var rootQuery = world.Query(Matchers.Of<UIElement, Node<UIHierarchyTag>>());
        foreach (var entity in rootQuery)
        {
            ref readonly var node = ref entity.Get<Node<UIHierarchyTag>>();
            if (node.Parent == null) // Only process root nodes
            {
                CollectUIHierarchy(entity, uiElements);
            }
        }

        // Collect non-hierarchical UI elements (avoid duplicate collection)
        var standaloneQuery = world.Query(Matchers.Of<UIElement>());
        foreach (var entity in standaloneQuery)
        {
            // Skip elements that have already been collected in the hierarchy
            if (entity.Contains<Node<UIHierarchyTag>>()) continue;
            
            ref readonly var uiElement = ref entity.Get<UIElement>();
            if (uiElement.IsVisible)
            {
                uiElements.Add(entity);
            }
        }

        // Sort by layer (hierarchy traversal ensures correct order, here only adjust layer)
        uiElements.Sort(static (a, b) =>
        {
            if (!a.IsValid || !a.Contains<UIElement>()) return 1;
            if (!b.IsValid || !b.Contains<UIElement>()) return -1;
            
            var aLayer = a.Contains<UILayer>() ? a.Get<UILayer>().Value : 0;
            var bLayer = b.Contains<UILayer>() ? b.Get<UILayer>().Value : 0;
            return aLayer.CompareTo(bLayer);
        });

        return uiElements;
    }

    private static void CollectUIHierarchy(Entity entity, List<Entity> result)
    {
        if (!entity.IsValid || !entity.Contains<UIElement>()) return;

        ref readonly var uiElement = ref entity.Get<UIElement>();
        if (uiElement.IsVisible)
        {
            result.Add(entity);
        }

        // Recursively collect child elements
        if (entity.Contains<Node<UIHierarchyTag>>())
        {
            ref readonly var node = ref entity.Get<Node<UIHierarchyTag>>();
            foreach (var child in node.Children)
            {
                CollectUIHierarchy(child, result);
            }
        }
    }

    private void BeginFrame()
    {
        // Clear canvas
        _canvas!.Clear(SKColors.Transparent);

        // Save initial state
        _canvas.Save();

        // Convert coordinate system: from bottom-left origin to top-left origin
        // Flip Y axis and translate to move (0,0) to top-left
        _canvas.Scale(1, -1);
        _canvas.Translate(0, -_windowHeight);
    }

    private void EndFrame()
    {
        // Restore canvas state
        _canvas!.Restore();

        // Flush GPU commands and wait for completion
        _grContext!.Flush();
    }

    private void RenderUIElements(List<Entity> sortedUIElements)
    {
        // Render UI elements in sorted order
        foreach (var entity in sortedUIElements)
        {
            RenderSingleUIElement(entity);
        }
    }

    private void RenderSingleUIElement(Entity entity)
    {
        ref readonly var uiElement = ref entity.Get<UIElement>();

        // Save current transformation state
        _canvas!.Save();

        try
        {
            _canvas.Translate(uiElement.Position.X, uiElement.Position.Y);

            if (entity.Contains<UIScrollable>())
            {
                RenderScrollableElement(entity, uiElement.Size);
            }
            else
            {
                RenderRegularElement(entity, uiElement.Size);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UIRenderPass] Failed to render entity: {ex.Message}");
        }
        finally
        {
            // Restore transformation state
            _canvas.Restore();
        }
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

            // Render background style if present
            if (entity.Contains<UIStyle>())
            {
                _canvas.Save();
                _canvas.Translate(scrollable.ScrollOffset.X, scrollable.ScrollOffset.Y);
                RenderStyle(entity, size);
                _canvas.Restore();
            }

            RenderScrollableContent(entity, scrollable.ContentSize);
        }
        finally
        {
            _canvas.Restore();
        }

        RenderScrollbars(entity, size, scrollable);
    }

    private void RenderScrollableContent(Entity entity, Vector2 contentSize)
    {
        if (entity.Contains<UIText>())
        {
            RenderScrollableText(entity, contentSize);
        }
    }

    private void RenderScrollableText(Entity entity, Vector2 contentSize)
    {
        ref readonly var text = ref entity.Get<UIText>();
        if (string.IsNullOrEmpty(text.Content)) return;

        var font = GetOrCreateFont(text.FontSize);
        var paint = GetOrCreatePaint(text.Color);
        var lines = text.Content.Split('\n') ?? [];
        var lineHeight = text.FontSize * 1.2f;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrEmpty(line)) continue;

            var y = i * lineHeight + text.FontSize;
            _canvas!.DrawText(line, 0, y, SKTextAlign.Left, font, paint);
        }
    }

    private void RenderScrollbars(Entity entity, Vector2 viewportSize, UIScrollable scrollable)
    {
        var maxOffset = scrollable.GetMaxScrollOffset(viewportSize);

        if (scrollable.CanScroll(ScrollDirection.Vertical) && maxOffset.Y > 0)
        {
            RenderVerticalScrollbar(viewportSize, scrollable, maxOffset);
        }

        if (scrollable.CanScroll(ScrollDirection.Horizontal) && maxOffset.X > 0)
        {
            RenderHorizontalScrollbar(viewportSize, scrollable, maxOffset);
        }
    }

    private void RenderVerticalScrollbar(Vector2 viewportSize, UIScrollable scrollable, Vector2 maxOffset)
    {
        const float thumbWidth = 4f;
        const float margin = 2f;

        // Calculate thumb position and size
        var thumbHeight = Math.Max(30f, viewportSize.Y * (viewportSize.Y / scrollable.ContentSize.Y));
        var thumbPosition = (scrollable.ScrollOffset.Y / maxOffset.Y) * (viewportSize.Y - thumbHeight);

        // Position thumb close to right edge
        var thumbRect = new SKRect(
            viewportSize.X - thumbWidth - margin,
            thumbPosition,
            viewportSize.X - margin,
            thumbPosition + thumbHeight);

        // Render Android-style thumb: semi-transparent, rounded
        var thumbPaint = GetOrCreatePaint(Color.FromArgb(120, 100, 100, 100));
        _canvas!.DrawRoundRect(thumbRect, thumbWidth / 2, thumbWidth / 2, thumbPaint);
    }

    private void RenderHorizontalScrollbar(Vector2 viewportSize, UIScrollable scrollable, Vector2 maxOffset)
    {
        const float thumbHeight = 4f;
        const float margin = 2f;

        // Calculate thumb position and size
        var thumbWidth = Math.Max(30f, viewportSize.X * (viewportSize.X / scrollable.ContentSize.X));
        var thumbPosition = (scrollable.ScrollOffset.X / maxOffset.X) * (viewportSize.X - thumbWidth);

        // Position thumb close to bottom edge
        var thumbRect = new SKRect(
            thumbPosition,
            viewportSize.Y - thumbHeight - margin,
            thumbPosition + thumbWidth,
            viewportSize.Y - margin);

        // Render Android-style thumb: semi-transparent, rounded
        var thumbPaint = GetOrCreatePaint(Color.FromArgb(120, 100, 100, 100));
        _canvas!.DrawRoundRect(thumbRect, thumbHeight / 2, thumbHeight / 2, thumbPaint);
    }

    private void RenderRegularElement(Entity entity, Vector2 size)
    {
        // Render background style
        if (entity.Contains<UIStyle>())
        {
            RenderStyle(entity, size);
        }

        // Render button (if it has UIButton component)
        if (entity.Contains<UIButton>())
        {
            RenderButton(entity, size);
        }

        // Render text - render last to ensure it's on top of other elements
        if (entity.Contains<UIText>())
        {
            RenderText(entity, size);
        }
    }

    private void RenderStyle(Entity entity, Vector2 size)
    {
        ref readonly var style = ref entity.Get<UIStyle>();
        
        var paint = GetOrCreatePaint(style.BackgroundColor);
        var rect = new SKRect(0, 0, size.X, size.Y);

        if (style.CornerRadius > 0)
        {
            _canvas!.DrawRoundRect(rect, style.CornerRadius, style.CornerRadius, paint);
        }
        else
        {
            _canvas!.DrawRect(rect, paint);
        }

        // Draw border if needed
        if (style.BorderWidth > 0 && style.BorderColor.A > 0)
        {
            var borderPaint = GetOrCreatePaint(style.BorderColor);
            borderPaint.Style = SKPaintStyle.Stroke;
            borderPaint.StrokeWidth = style.BorderWidth;

            if (style.CornerRadius > 0)
            {
                _canvas!.DrawRoundRect(rect, style.CornerRadius, style.CornerRadius, borderPaint);
            }
            else
            {
                _canvas!.DrawRect(rect, borderPaint);
            }
        }
    }

    private void RenderButton(Entity entity, Vector2 size)
    {
        ref readonly var button = ref entity.Get<UIButton>();
        
        // Get current style based on UI state flags
        var stateFlags = entity.Contains<UIState>() ? entity.Get<UIState>().Flags : UIStateFlags.None;
        var currentStyle = button.GetStyleForState(stateFlags);
        
        var paint = GetOrCreatePaint(currentStyle.BackgroundColor);
        var rect = new SKRect(0, 0, size.X, size.Y);

        // Draw rounded button
        _canvas!.DrawRoundRect(rect, currentStyle.CornerRadius, currentStyle.CornerRadius, paint);

        // Add inner shadow effect if button is pressed
        if ((stateFlags & UIStateFlags.Pressed) != 0)
        {
            DrawButtonPressedEffect(rect);
        }
    }

    private void DrawButtonPressedEffect(SKRect rect)
    {
        using var shadowPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 50),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };

        var innerRect = new SKRect(rect.Left + 1, rect.Top + 1, rect.Right - 1, rect.Bottom - 1);
        _canvas!.DrawRoundRect(innerRect, 6, 6, shadowPaint);
    }

    private void RenderText(Entity entity, Vector2 elementSize)
    {
        ref readonly var text = ref entity.Get<UIText>();
        if (string.IsNullOrEmpty(text.Content)) return;

        var font = GetOrCreateFont(text.FontSize);
        var paint = GetOrCreatePaint(text.Color);
        var content = text.Content;

        font.MeasureText(content, out var textBounds, paint);

        var x = text.Alignment switch
        {
            TextAlignment.Left => 0,
            TextAlignment.Center => (elementSize.X - textBounds.Width) * 0.5f,
            TextAlignment.Right => elementSize.X - textBounds.Width,
            _ => 0
        };

        var y = (elementSize.Y + textBounds.Height) * 0.5f;

        _canvas!.DrawText(content, x, y, SKTextAlign.Left, font, paint);
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
        // Cleanup font cache
        foreach (var font in _fontCache.Values)
            font.Dispose();
        _fontCache.Clear();

        // Cleanup paint cache
        foreach (var paint in _paintCache.Values)
            paint.Dispose();
        _paintCache.Clear();

        // Cleanup Skia resources
        _surface?.Dispose();
        _renderTarget?.Dispose();
        _grContext?.Dispose();
        _typeface?.Dispose();
    }
}