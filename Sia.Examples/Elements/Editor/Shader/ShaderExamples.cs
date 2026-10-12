namespace Sia_Examples.Editor;

internal static class ShaderExamples
{
    public const string Folder = "shaders";
    public const string FileName = "Preview.cs";
    public const string Source = """
        using Sia.Spirv;

        public static class Preview
        {
            // Preview uses a fullscreen triangle supplied by the editor.
            [SpirvFragmentShader]
            public static void StaticColor()
            {
                Gpu.SetOutput(0, 0.2f, 0.6f, 0.9f, 1f);
            }

            // The preview controls supply time in seconds and canvas size in pixels.
            [SpirvFragmentShader]
            public static void Gradient(float time, float width, float height)
            {
                float x = Gpu.GetFragmentPosition(0) / width;
                float y = Gpu.GetFragmentPosition(1) / height;
                float pulse = time % 2f / 2f;
                Gpu.SetOutput(0, x, y, pulse, 1f);
            }
        }
        """;
}
