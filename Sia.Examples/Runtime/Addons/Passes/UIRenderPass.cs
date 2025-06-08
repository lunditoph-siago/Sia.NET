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
    private uint _vao, _vbo;
    private uint _shaderProgram;
    private int _windowWidth = windowWidth;
    private int _windowHeight = windowHeight;

    private readonly Dictionary<TextCacheKey, uint> _textureCache = new(32);

    private readonly SKTypeface _typeface = SKTypeface.FromFamilyName("Consolas") ??
                                            SKTypeface.FromFamilyName("Courier New") ??
                                            SKTypeface.Default;

    private readonly record struct TextCacheKey(int TextHash, int ColorArgb, float FontSize);

    public void Initialize(GL gl)
    {
        _gl = gl;
        InitializeShaders();
        InitializeGeometry();
    }

    private void InitializeShaders()
    {
        const string vertexShader = """
                                    #version 330 core
                                    layout (location = 0) in vec4 vertex; // <vec2 pos, vec2 tex>
                                    out vec2 TexCoords;

                                    uniform mat4 projection;

                                    void main()
                                    {
                                        gl_Position = projection * vec4(vertex.xy, 0.0, 1.0);
                                        TexCoords = vertex.zw;
                                    }
                                    """;

        const string fragmentShader = """
                                      #version 330 core
                                      in vec2 TexCoords;
                                      out vec4 color;

                                      uniform sampler2D text;
                                      uniform vec3 textColor;
                                      uniform float alpha;

                                      void main()
                                      {    
                                          vec4 sampled = vec4(1.0, 1.0, 1.0, texture(text, TexCoords).r);
                                          color = vec4(textColor, alpha) * sampled;
                                      }
                                      """;

        var vs = CompileShader(ShaderType.VertexShader, vertexShader);
        var fs = CompileShader(ShaderType.FragmentShader, fragmentShader);

        _shaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_shaderProgram, vs);
        _gl.AttachShader(_shaderProgram, fs);
        _gl.LinkProgram(_shaderProgram);

        if (_gl.GetProgram(_shaderProgram, GLEnum.LinkStatus) == 0)
        {
            var infoLog = _gl.GetProgramInfoLog(_shaderProgram);
            throw new InvalidOperationException($"Shader program linking failed: {infoLog}");
        }

        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
    }

    private void InitializeGeometry()
    {
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, sizeof(float) * 6 * 4,
            in IntPtr.Zero, BufferUsageARB.DynamicDraw);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 4, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    public void Execute(World world, RenderPipeline pipeline)
    {
        _gl.UseProgram(_shaderProgram);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindVertexArray(_vao);

        var projection = Matrix4x4.CreateOrthographicOffCenter(0.0f, _windowWidth, 0.0f, _windowHeight, -1.0f, 1.0f);
        var projectionLoc = _gl.GetUniformLocation(_shaderProgram, "projection");

        unsafe
        {
            _gl.UniformMatrix4(projectionLoc, 1, false, (float*)&projection);
        }

        RenderUIPanels(world);
        RenderUITexts(world);

        _gl.BindVertexArray(0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    private void RenderUITexts(World world)
    {
        var textQuery = world.Query(Matchers.Of<UIText, UIElement>());

        foreach (var entity in textQuery)
        {
            ref readonly var text = ref entity.Get<UIText>();
            ref readonly var uiElement = ref entity.Get<UIElement>();

            if (text.IsVisible && uiElement.IsVisible && !string.IsNullOrEmpty(text.Content))
            {
                RenderText(text.Content, text.TextColor, text.FontSize, uiElement.Position);
            }
        }
    }

    private void RenderUIPanels(World world)
    {
        var panelQuery = world.Query(Matchers.Of<UIPanel, UIElement>());

        foreach (var entity in panelQuery)
        {
            ref readonly var panel = ref entity.Get<UIPanel>();
            ref readonly var uiElement = ref entity.Get<UIElement>();

            if (panel.IsVisible && uiElement.IsVisible)
            {
                RenderPanel(panel.BackgroundColor, uiElement.Position, uiElement.Size);
            }
        }
    }

    private void RenderText(string text, Color color, float fontSize, Vector2 position)
    {
        var textureId = GetOrCreateTextTexture(text, color, fontSize);
        SetShaderColor(color);

        using var font = new SKFont(_typeface, fontSize);
        var textBounds = font.MeasureText(text, out _);
        var size = new Vector2(textBounds + 8, fontSize + 4);

        RenderQuad(textureId, position, size);
    }

    private void RenderPanel(Color color, Vector2 position, Vector2 size)
    {
        var textureId = GetOrCreateColorTexture(color);
        SetShaderColor(color);
        RenderQuad(textureId, position, size);
    }

    private void SetShaderColor(Color color)
    {
        var textColorLoc = _gl.GetUniformLocation(_shaderProgram, "textColor");
        var alphaLoc = _gl.GetUniformLocation(_shaderProgram, "alpha");

        _gl.Uniform3(textColorLoc, color.R / 255f, color.G / 255f, color.B / 255f);
        _gl.Uniform1(alphaLoc, color.A / 255f);
    }

    private void RenderQuad(uint textureId, Vector2 position, Vector2 size)
    {
        _gl.BindTexture(TextureTarget.Texture2D, textureId);

        // Flip Y coordinate for UI coordinate system
        var flippedY = _windowHeight - position.Y - size.Y;

        Span<float> vertices =
        [
            position.X,           flippedY + size.Y, 0.0f, 0.0f,
            position.X,           flippedY,          0.0f, 1.0f,
            position.X + size.X,  flippedY,          1.0f, 1.0f,

            position.X,           flippedY + size.Y, 0.0f, 0.0f,
            position.X + size.X,  flippedY,          1.0f, 1.0f,
            position.X + size.X,  flippedY + size.Y, 1.0f, 0.0f
        ];

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* verticesPtr = vertices)
            {
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0,
                    (nuint)(vertices.Length * sizeof(float)), verticesPtr);
            }
        }

        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
    }

    private uint GetOrCreateTextTexture(string text, Color color, float fontSize)
    {
        var key = new TextCacheKey(text.GetHashCode(), color.ToArgb(), fontSize);

        if (_textureCache.TryGetValue(key, out var existingTexture))
            return existingTexture;

        var textureId = CreateTextTexture(text, color, fontSize);

        if (_textureCache.Count >= 30)
        {
            var oldestKey = _textureCache.Keys.First();
            _gl.DeleteTexture(_textureCache[oldestKey]);
            _textureCache.Remove(oldestKey);
        }

        _textureCache[key] = textureId;
        return textureId;
    }

    private uint GetOrCreateColorTexture(Color color)
    {
        var key = new TextCacheKey(0, color.ToArgb(), 0);

        if (_textureCache.TryGetValue(key, out var existingTexture))
            return existingTexture;

        var textureId = CreateColorTexture(color);
        _textureCache[key] = textureId;
        return textureId;
    }

    private uint CreateTextTexture(string text, Color color, float fontSize)
    {
        using var font = new SKFont(_typeface, fontSize);
        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.Color = new SKColor(color.R, color.G, color.B, color.A);

        var textWidth = font.MeasureText(text, out _);
        var width = Math.Max(1, (int)Math.Ceiling(textWidth) + 8);
        var height = Math.Max(1, (int)Math.Ceiling(fontSize) + 4);

        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Transparent);
        canvas.DrawText(text, 4, 4 + font.Metrics.CapHeight, font, paint);

        return CreateTextureFromBitmap(bitmap);
    }

    private uint CreateColorTexture(Color color)
    {
        using var bitmap = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.SetPixel(0, 0, new SKColor(color.R, color.G, color.B, color.A));
        return CreateTextureFromBitmap(bitmap);
    }

    private uint CreateTextureFromBitmap(SKBitmap bitmap)
    {
        var pixels = bitmap.Pixels;
        Span<byte> textureData = stackalloc byte[bitmap.Width * bitmap.Height];

        for (var i = 0; i < pixels.Length; i++)
            textureData[i] = pixels[i].Alpha;

        var texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);

        unsafe
        {
            fixed (byte* dataPtr = textureData)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Red,
                    (uint)bitmap.Width, (uint)bitmap.Height, 0,
                    PixelFormat.Red, PixelType.UnsignedByte, dataPtr);
            }
        }

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);

        return texture;
    }

    private uint CompileShader(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        if (_gl.GetShader(shader, ShaderParameterName.CompileStatus) == 0)
        {
            var infoLog = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"Shader compilation failed ({type}): {infoLog}");
        }

        return shader;
    }

    public void OnResize(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;
    }

    public void Dispose()
    {
        foreach (var textureId in _textureCache.Values)
            _gl.DeleteTexture(textureId);
        _textureCache.Clear();

        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteProgram(_shaderProgram);
        _typeface?.Dispose();
    }
}