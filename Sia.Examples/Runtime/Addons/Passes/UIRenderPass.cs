using System.Drawing;
using System.Numerics;
using Sia.Examples.Runtime.Components;
using Silk.NET.OpenGL;
using SkiaSharp;

namespace Sia.Examples.Runtime.Addons.Passes;

public class UIRenderPass : IRenderPass
{
    public string Name => "UI Render Pass";
    public int Priority => 1000; // UI renders last
    public bool IsEnabled { get; set; } = true;

    private GL _gl = null!;
    private uint _vao, _vbo;
    private uint _shaderProgram;
    private int _windowWidth, _windowHeight;

    private readonly SKTypeface _typeface;
    private readonly Dictionary<string, TextureInfo> _textCache = [];

    private struct TextureInfo
    {
        public uint TextureId;
        public Vector2 Size;
    }

    public UIRenderPass(int windowWidth, int windowHeight)
    {
        _windowWidth = windowWidth;
        _windowHeight = windowHeight;

        _typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal) ??
                    SKTypeface.FromFamilyName("Courier New", SKFontStyle.Normal) ??
                    SKTypeface.Default;
    }

    public void Initialize(GL gl)
    {
        _gl = gl;

        try
        {
            InitializeShaders();
            InitializeGeometry();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UIRenderPass] Initialization failed: {ex.Message}");
            throw;
        }
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

        uint vs = CompileShader(ShaderType.VertexShader, vertexShader);
        uint fs = CompileShader(ShaderType.FragmentShader, fragmentShader);

        _shaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_shaderProgram, vs);
        _gl.AttachShader(_shaderProgram, fs);
        _gl.LinkProgram(_shaderProgram);

        _gl.GetProgram(_shaderProgram, GLEnum.LinkStatus, out int success);
        if (success == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(_shaderProgram);
            throw new Exception($"Shader program linking failed: {infoLog}");
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
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(sizeof(float) * 6 * 4), IntPtr.Zero,
            BufferUsageARB.DynamicDraw);

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
        int projectionLoc = _gl.GetUniformLocation(_shaderProgram, "projection");

        unsafe
        {
            _gl.UniformMatrix4(projectionLoc, 1, false, (float*)&projection);
        }

        RenderUITexts(world);
        RenderUIPanels(world);

        _gl.BindVertexArray(0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    private void RenderUITexts(World world)
    {
        var textQuery = world.Query(Matchers.Of<UIText, UIElement>());

        foreach (var entity in textQuery)
        {
            ref var text = ref entity.Get<UIText>();
            ref var uiElement = ref entity.Get<UIElement>();

            if (!text.IsVisible || !uiElement.IsVisible) continue;

            try
            {
                RenderText(text.Content, text.TextColor, text.FontSize,
                    uiElement.Position.X, uiElement.Position.Y);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UIRenderPass] Text rendering failed: {ex.Message}");
            }
        }
    }

    private void RenderUIPanels(World world)
    {
        var panelQuery = world.Query(Matchers.Of<UIPanel, UIElement>());

        foreach (var entity in panelQuery)
        {
            ref var panel = ref entity.Get<UIPanel>();
            ref var uiElement = ref entity.Get<UIElement>();

            if (!panel.IsVisible || !uiElement.IsVisible) continue;

            try
            {
                RenderPanel(uiElement.Size, panel.BackgroundColor,
                    uiElement.Position.X, uiElement.Position.Y);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UIRenderPass] Panel rendering failed: {ex.Message}");
            }
        }
    }

    private void RenderText(string text, Color color, float fontSize, float x, float y)
    {
        if (string.IsNullOrEmpty(text)) return;

        var textureInfo = CreateTextTexture(text, color, fontSize);

        int textColorLoc = _gl.GetUniformLocation(_shaderProgram, "textColor");
        int alphaLoc = _gl.GetUniformLocation(_shaderProgram, "alpha");

        _gl.Uniform3(textColorLoc, color.R / 255f, color.G / 255f, color.B / 255f);
        _gl.Uniform1(alphaLoc, color.A / 255f);

        RenderQuad(textureInfo.TextureId, x, y, textureInfo.Size.X, textureInfo.Size.Y);
    }

    private void RenderPanel(Vector2 size, Color color, float x, float y)
    {
        var textureInfo = CreateColorTexture(color);

        int textColorLoc = _gl.GetUniformLocation(_shaderProgram, "textColor");
        int alphaLoc = _gl.GetUniformLocation(_shaderProgram, "alpha");

        _gl.Uniform3(textColorLoc, color.R / 255f, color.G / 255f, color.B / 255f);
        _gl.Uniform1(alphaLoc, color.A / 255f);

        RenderQuad(textureInfo.TextureId, x, y, size.X, size.Y);
    }

    private void RenderQuad(uint textureId, float x, float y, float width, float height)
    {
        _gl.BindTexture(TextureTarget.Texture2D, textureId);

        // Flip Y coordinate since OpenGL origin is bottom-left, UI origin is top-left
        float flippedY = _windowHeight - y - height;

        float[] vertices =
        [
            x, flippedY + height, 0.0f, 0.0f,
            x, flippedY, 0.0f, 1.0f,
            x + width, flippedY, 1.0f, 1.0f,

            x, flippedY + height, 0.0f, 0.0f,
            x + width, flippedY, 1.0f, 1.0f,
            x + width, flippedY + height, 1.0f, 0.0f
        ];

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* verticesPtr = vertices)
            {
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(vertices.Length * sizeof(float)),
                    verticesPtr);
            }
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
    }

    private TextureInfo CreateTextTexture(string text, Color color, float fontSize)
    {
        string cacheKey = $"{text}_{color.ToArgb()}_{fontSize}";
        if (_textCache.TryGetValue(cacheKey, out var cached))
            return cached;

        using var font = new SKFont(_typeface, fontSize);
        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.Color = new SKColor(color.R, color.G, color.B, color.A);

        var textWidth = font.MeasureText(text, out _);

        var width = Math.Max(1, (int)Math.Ceiling(textWidth) + 8);
        var height = Math.Max(1, (int)Math.Ceiling(fontSize) + 4);

        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Transparent);
        canvas.DrawText(text, 4, 4 + font.Metrics.CapHeight, font, paint);

        uint textureId = CreateTextureFromBitmap(bitmap);

        var textureInfo = new TextureInfo
        {
            TextureId = textureId,
            Size = new Vector2(width, height)
        };

        _textCache[cacheKey] = textureInfo;
        return textureInfo;
    }

    private TextureInfo CreateColorTexture(Color color)
    {
        string cacheKey = $"color_{color.ToArgb()}";
        if (_textCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var bitmap = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.SetPixel(0, 0, new SKColor((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A));

        uint textureId = CreateTextureFromBitmap(bitmap);

        var textureInfo = new TextureInfo
        {
            TextureId = textureId,
            Size = new Vector2(1, 1)
        };

        _textCache[cacheKey] = textureInfo;
        return textureInfo;
    }

    private uint CreateTextureFromBitmap(SKBitmap bitmap)
    {
        var pixels = bitmap.Pixels;
        byte[] textureData = new byte[bitmap.Width * bitmap.Height];

        for (int i = 0; i < pixels.Length; i++)
            textureData[i] = pixels[i].Alpha;

        uint texture = _gl.GenTexture();
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
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
        if (success == 0)
        {
            string infoLog = _gl.GetShaderInfoLog(shader);
            throw new Exception($"Shader compilation failed ({type}): {infoLog}");
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
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteProgram(_shaderProgram);

        foreach (var textureInfo in _textCache.Values)
            _gl.DeleteTexture(textureInfo.TextureId);

        _textCache.Clear();
        _typeface?.Dispose();
    }
}