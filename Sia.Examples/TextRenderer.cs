using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Silk.NET.OpenGL;
using SkiaSharp;

namespace Sia_Examples;

public class TextRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly List<TextLine> _lines = [];
    private uint _vao, _vbo;
    private uint _shaderProgram;
    private int _windowWidth = 800;
    private int _windowHeight = 600;

    // SkiaSharp resources
    private readonly SKTypeface _typeface;
    private readonly SKFont _font;
    private readonly SKPaint _paint;
    private readonly Dictionary<string, LineInfo> _lineCache = new();

    public int MaxLines { get; set; } = 100;
    public float LineHeight { get; set; } = 18.0f;
    public float FontSize { get; set; } = 12.0f;
    public float ScrollOffset { get; set; } = 0.0f;

    private struct TextLine
    {
        public string Text;
        public Color Color;
        public float Width;
    }

    private struct LineInfo
    {
        public uint TextureId;
        public Vector2 Size;
        public float Width;
    }

    public TextRenderer(GL gl)
    {
        _gl = gl;

        // Create SkiaSharp typeface and font with modern APIs
        _typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal) ??
                   SKTypeface.FromFamilyName("Courier New", SKFontStyle.Normal) ??
                   SKTypeface.Default;

        _font = new SKFont(_typeface, FontSize)
        {
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias,
            Hinting = SKFontHinting.Full
        };

        _paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White
        };

        InitializeRenderer();
    }

    private void InitializeRenderer()
    {
        // Enable blending for text transparency
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

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

        // Check for linking errors
        _gl.GetProgram(_shaderProgram, GLEnum.LinkStatus, out int success);
        if (success == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(_shaderProgram);
            throw new Exception($"Shader program linking failed: {infoLog}");
        }

        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);

        // Create VAO and VBO
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(sizeof(float) * 6 * 4), IntPtr.Zero, BufferUsageARB.DynamicDraw);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 4, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);

        // Disable byte-alignment restriction
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
    }

    private LineInfo CreateLineTexture(string text, Color color)
    {
        string cacheKey = $"{text}_{color.ToArgb()}";
        if (_lineCache.TryGetValue(cacheKey, out var cached))
            return cached;

        text = string.IsNullOrEmpty(text) ? " " : text;

        // Set paint color
        _paint.Color = new SKColor((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);

        // Measure text using SKFont
        var bounds = new SKRect();
        float textWidth = _font.MeasureText(text, out bounds);

        int width = Math.Max(1, (int)Math.Ceiling(textWidth) + 8);
        int height = Math.Max(1, (int)Math.Ceiling(LineHeight) + 4);

        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Transparent);
        canvas.DrawText(text, 4, 4 + _font.Metrics.CapHeight, _font, _paint);
        var pixels = bitmap.Pixels;
        byte[] textureData = new byte[width * height];

        for (int i = 0; i < pixels.Length; i++)
            textureData[i] = pixels[i].Alpha;
        uint texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);

        unsafe
        {
            fixed (byte* dataPtr = textureData)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Red,
                    (uint)width, (uint)height, 0,
                    PixelFormat.Red, PixelType.UnsignedByte, dataPtr);
            }
        }

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);

        var lineInfo = new LineInfo
        {
            TextureId = texture,
            Size = new Vector2(width, height),
            Width = textWidth
        };

        // Cache the result (limit cache size)
        if (_lineCache.Count > 200)
        {
            CleanupCache();
        }

        _lineCache[cacheKey] = lineInfo;
        return lineInfo;
    }

    private void CleanupCache()
    {
        var keysToRemove = _lineCache.Keys.Take(50).ToList();

        foreach (var key in keysToRemove)
        {
            if (_lineCache.TryGetValue(key, out var oldLine))
            {
                _gl.DeleteTexture(oldLine.TextureId);
                _lineCache.Remove(key);
            }
        }
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
            throw new Exception($"Shader compilation failed: {infoLog}");
        }

        return shader;
    }

    public void SetWindowSize(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;
        _gl.Viewport(0, 0, (uint)width, (uint)height);
    }

    public void AddLine(string text, Color color = default)
    {
        color = color == default ? Color.White : color;

        var textLine = new TextLine
        {
            Text = text,
            Color = color,
            Width = _font.MeasureText(text)
        };

        _lines.Add(textLine);

        // Remove old lines if we exceed the maximum
        while (_lines.Count > MaxLines)
        {
            _lines.RemoveAt(0);
        }
    }

    public void Clear()
    {
        _lines.Clear();
    }

    public void Scroll(float delta)
    {
        ScrollOffset += delta;

        // Calculate max scroll based on content
        float totalHeight = _lines.Count * LineHeight;
        float visibleHeight = _windowHeight - 60; // Account for margins
        float maxScroll = Math.Max(0, totalHeight - visibleHeight);

        ScrollOffset = Math.Clamp(ScrollOffset, 0, maxScroll);
    }

    public int GetTotalLines() => _lines.Count;
    public int GetVisibleLines() => (int)((_windowHeight - 60) / LineHeight);

    public void Render()
    {
        _gl.UseProgram(_shaderProgram);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindVertexArray(_vao);

        // Set up projection matrix for 2D rendering
        var projection = Matrix4x4.CreateOrthographicOffCenter(0.0f, _windowWidth, 0.0f, _windowHeight, -1.0f, 1.0f);
        int projectionLoc = _gl.GetUniformLocation(_shaderProgram, "projection");
        unsafe
        {
            _gl.UniformMatrix4(projectionLoc, 1, false, (float*)&projection);
        }

        int textColorLoc = _gl.GetUniformLocation(_shaderProgram, "textColor");
        int alphaLoc = _gl.GetUniformLocation(_shaderProgram, "alpha");

        // Calculate which lines are visible
        float startY = _windowHeight - 30 + ScrollOffset;
        int startLine = Math.Max(0, (int)(ScrollOffset / LineHeight));
        int endLine = Math.Min(_lines.Count, startLine + GetVisibleLines() + 2);

        for (int i = startLine; i < endLine; i++)
        {
            float y = startY - (i * LineHeight);

            // Skip lines that are completely outside the viewport
            if (y < -LineHeight || y > _windowHeight + LineHeight)
                continue;

            var line = _lines[i];

            // Calculate fade effect for lines near edges
            float alpha = CalculateAlpha(y);

            // Set text color and alpha
            _gl.Uniform3(textColorLoc, line.Color.R / 255f, line.Color.G / 255f, line.Color.B / 255f);
            _gl.Uniform1(alphaLoc, alpha);

            // Render line
            RenderLine(line.Text, line.Color, 10, y);
        }

        _gl.BindVertexArray(0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    private float CalculateAlpha(float y)
    {
        if (y < 30) return Math.Max(0, y / 30f);
        if (y > _windowHeight - 30) return Math.Max(0, (_windowHeight - y) / 30f);
        return 1.0f;
    }

    private void RenderLine(string text, Color color, float x, float y)
    {
        var lineInfo = CreateLineTexture(text, color);

        float w = lineInfo.Size.X;
        float h = lineInfo.Size.Y;

        // Update VBO for the line
        float[] vertices = [
            x,     y + h,   0.0f, 0.0f,
            x,     y,       0.0f, 1.0f,
            x + w, y,       1.0f, 1.0f,

            x,     y + h,   0.0f, 0.0f,
            x + w, y,       1.0f, 1.0f,
            x + w, y + h,   1.0f, 0.0f
        ];

        // Render line texture over quad
        _gl.BindTexture(TextureTarget.Texture2D, lineInfo.TextureId);

        // Update content of VBO memory
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* verticesPtr = vertices)
            {
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(vertices.Length * sizeof(float)), verticesPtr);
            }
        }
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        // Render quad
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
    }

    public void Dispose()
    {
        // Clean up OpenGL resources
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteProgram(_shaderProgram);

        // Clean up cached textures
        foreach (var lineInfo in _lineCache.Values)
        {
            _gl.DeleteTexture(lineInfo.TextureId);
        }
        _lineCache.Clear();

        // Clean up SkiaSharp resources
        _paint?.Dispose();
        _font?.Dispose();
        _typeface?.Dispose();
    }
}