namespace Sia.Examples.Editor;

internal static partial class CompilerTemplate
{
    public const string ShaderSource = """"
        using Sia.Spirv;

        public static class MeshShaders
        {
            [SpirvVertexShader]
            public static void Vertex()
            {
                Gpu.SetPosition(Gpu.GetInput(0, 0), Gpu.GetInput(0, 1), Gpu.GetInput(0, 2), 1f);
                Gpu.SetOutput(0, Gpu.GetInput(1, 0), Gpu.GetInput(1, 1), Gpu.GetInput(1, 2), 1f);
            }

            [SpirvFragmentShader]
            public static void Color()
                => Gpu.SetOutput(0, Gpu.GetInput(0, 0), Gpu.GetInput(0, 1), Gpu.GetInput(0, 2), 1f);
        }
        """";
}
