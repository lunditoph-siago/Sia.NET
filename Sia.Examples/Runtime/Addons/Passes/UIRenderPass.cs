using System.Drawing;
using System.Numerics;
using Sia.Examples.Runtime.Components;
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

    // Font and paint cache - greatly reduce object creation
    private readonly Dictionary<float, SKFont> _fontCache = [];
    private readonly Dictionary<uint, SKPaint> _paintCache = [];
    private readonly SKTypeface _typeface = SKTypeface.FromFamilyName("sans-serif") ??
                                           SKTypeface.Default;

    private readonly List<Entity> _sortedUIElements = [];
    private bool _uiListDirty = true;

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
        _surface = SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
        _canvas = _surface?.Canvas;

        if (_canvas == null)
        {
            throw new InvalidOperationException("Failed to create Skia canvas from GL context");
        }
    }

    public void Execute(World world, RenderPipeline pipeline)
    {
        if (_canvas == null || _grContext == null) return;

        if (_uiListDirty)
        {
            RefreshUIElementsList(world);
        }

        BeginFrame();

        try
        {
            RenderUIElements();
        }
        finally
        {
            EndFrame();
        }
    }

    private void RefreshUIElementsList(World world)
    {
        _sortedUIElements.Clear();

        // Collect all visible UI elements
        var uiQuery = world.Query(Matchers.Of<UIElement>());
        foreach (var entity in uiQuery)
        {
            ref readonly var uiElement = ref entity.Get<UIElement>();
            if (uiElement.IsVisible)
            {
                _sortedUIElements.Add(entity);
            }
        }

        // Sort by layer (lower layer renders first)
        _sortedUIElements.Sort(static (a, b) =>
        {
            var layerA = a.Get<UIElement>().Layer;
            var layerB = b.Get<UIElement>().Layer;
            return layerA.CompareTo(layerB);
        });

        _uiListDirty = false;
    }

    private void BeginFrame()
    {
        // Clear canvas
        _canvas!.Clear(SKColors.Transparent);

        // Save initial state
        _canvas.Save();
    }

    private void EndFrame()
    {
        // Restore canvas state
        _canvas!.Restore();

        // Flush GPU commands and wait for completion
        _grContext!.Flush();
    }

    private void RenderUIElements()
    {
        // Render UI elements in sorted order
        foreach (var entity in _sortedUIElements)
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
            // Apply element transformation
            _canvas.Translate(uiElement.Position.X, uiElement.Position.Y);

            // Render panel background
            if (entity.Contains<UIPanel>())
            {
                RenderPanel(entity, uiElement.Size);
            }

            // Render button
            if (entity.Contains<UIButton>())
            {
                RenderButton(entity, uiElement.Size);
            }

            // Render text - render last to ensure it's on top of other elements
            if (entity.Contains<UIText>())
            {
                RenderText(entity, uiElement.Size);
            }
        }
        finally
        {
            // Restore transformation state
            _canvas.Restore();
        }
    }

    private void RenderPanel(Entity entity, Vector2 size)
    {
        ref readonly var panel = ref entity.Get<UIPanel>();
        if (!panel.IsVisible) return;

        var paint = GetOrCreatePaint(panel.BackgroundColor);
        var rect = new SKRect(0, 0, size.X, size.Y);

        _canvas!.DrawRect(rect, paint);
    }

    private void RenderButton(Entity entity, Vector2 size)
    {
        ref readonly var button = ref entity.Get<UIButton>();
        if (!button.IsEnabled) return;

        // Determine button current state color
        var color = button.NormalColor;
        if (entity.Contains<UIInteractionState>())
        {
            ref readonly var state = ref entity.Get<UIInteractionState>();
            color = state.IsPressed ? button.PressedColor :
                   state.IsHovered ? button.HoverColor :
                   button.NormalColor;
        }

        var paint = GetOrCreatePaint(color);
        var rect = new SKRect(0, 0, size.X, size.Y);

        // Draw rounded button
        _canvas!.DrawRoundRect(rect, 8, 8, paint);

        // Add inner shadow effect if button is pressed
        if (entity.Contains<UIInteractionState>())
        {
            ref readonly var state = ref entity.Get<UIInteractionState>();
            if (state.IsPressed)
            {
                DrawButtonPressedEffect(rect);
            }
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
        if (!text.IsVisible || string.IsNullOrEmpty(text.Content)) return;

        var font = GetOrCreateFont(text.FontSize);
        var paint = GetOrCreatePaint(text.TextColor);

        font.MeasureText(text.Content, out var textBounds, paint);

        var x = (elementSize.X - textBounds.Width) * 0.5f;
        var y = (elementSize.Y + textBounds.Height) * 0.5f;

        _canvas!.DrawText(text.Content, x, y, SKTextAlign.Left, font, paint);
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

        _uiListDirty = true;
    }

    public void MarkUIListDirty() => _uiListDirty = true;

    public bool IsUIListDirty => _uiListDirty;

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