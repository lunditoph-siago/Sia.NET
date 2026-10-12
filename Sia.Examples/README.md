# Examples and browser shader editor

`Sia.Examples.Browser.csproj` hosts the notebook and Editor Lab in WebAssembly.
`Sia.Examples.csproj` shares their C# compilation and workspace code with the
console host. Shader canvas preview runs in the browser.

## Shader workflow

1. Open **Editor Lab**, then **Shader Example**. This creates `shaders/Preview.cs`
   only if it does not exist; existing files are opened without replacement.
2. Choose **Compile Shader** or **Preview WGSL**. Shader compilation includes
   `.cs` files below the active saved file's folder, including helper files.
   It emits a class library, so it does not require `Main`. Keep top-level console
   programs in a separate folder.
3. Select an attributed entry. **Preview WGSL** renders the managed WGSL output;
   **Preview SPIR-V** emits SPIR-V, converts it to WGSL with the managed compiler,
   and renders that result. Browser WebGPU consumes WGSL.
4. Expand **Compiled shader** to inspect WGSL, SPIR-V bytes or the translated WGSL.
   **Download .spv** exports the complete binary. The hex view shows up to 4096 bytes.
5. For `Gradient`, parameter controls supply time, canvas width and height.
   Other scalar parameters default to a constant value. Choose the source or
   enter a value explicitly; **Pause** freezes time, and **Resume** continues it.

The preview supplies a fullscreen triangle. Supported fragment entries produce
one `vec4<float>` color at location 0, using `Gpu.SetOutput` or an equivalent
typed result. They may read fragment position and take `float`, `int`, or `uint`
scalar parameters. The module validates the compiled uniform binding and the
packed 16-byte scalar layout before uploading values. Textures, storage inputs,
user varyings and additional render targets need a resource/vertex provider and
are reported as unsupported for this preview. Vertex and compute entries may
still produce inspectable artifacts.

Ordinary **Build**, **Run**, and `Notebook.Render` continue to compile/execute the
C# workspace. Shader compilation uses PE metadata and CIL; it does not load or
execute the user shader assembly. `Ctrl+S` saves the active file; `Ctrl+Shift+S`
saves open files. Files persist in the existing browser workspace storage.

## Ownership and execution

- `CSharpCompilationEngine` prepares references on the caller's UI thread and
  performs parsing, generators and Emit on a background task. Shader requests
  use Library options; notebook/ordinary editor requests retain Console options.
- `EditorShaderCompiler` prepares the host Core PE image and converts immutable
  assembly/entry inputs through `Sia.Spirv.Compiler 0.0.23`. Compiler dependencies
  use normal package references in the two Examples projects. Core and Runtime
  flow transitively; the ECS projects have no new graphics dependency.
- `BrowserEditorPage.Shader` owns job coordination and a bounded artifact cache.
  Cache validity includes source paths/content, reference version and entry
  signature; the compiler version and target are fixed for this app instance.
  Each new job invalidates older completions, including pending GPU pipelines.
  There is one active shader job and at most one queued replacement with its
  selected source folder and entry. Temporary frontend CFGs are not retained in
  the entry list.
- `EditorShaderPreview` owns one JS preview instance. `shader-preview.js` owns
  its GPU device, uniform buffer, canvas, observers and animation callback. A
  successful current pipeline replaces the old one; failure preserves the last
  valid picture. Hidden panels stop drawing. Closing Render releases the GPU
  instance; opening a preview again recreates it. Device loss permits a retry.

Source edits discard cached results and mark the previous picture stale.
Compilation failures identify the snapshot and entry of the picture still shown.
Reference versions are checked before reusing or installing results. C# diagnostics
retain file/line/column; IL and generated WGSL diagnostics identify their own
locations and are not presented as C# source lines. Console shows original
snapshot phase timings and whether source/artifacts were reused, followed by the
current GPU pipeline timing.

Cancel stops subsequent phases and prevents results from being installed. Roslyn
supports cancellation; synchronous Compiler phases can continue until their
current call returns. The editor waits before starting the queued replacement.

## Build and verify

From the Sia.NET repository root, use a .NET 11 SDK and install the .NET 10
runtime needed by the existing tests. The browser project requires the matching
WebAssembly workload and enables threads.

```sh
dotnet workload restore Sia.Examples/Sia.Examples.Browser.csproj
dotnet build Sia.Examples/Sia.Examples.csproj -c Release
dotnet publish Sia.Examples/Sia.Examples.Browser.csproj -c Release -o Sia.Examples/bin/published
dotnet test Sia.Tests/Sia.Tests.csproj -c Release --blame-hang --blame-hang-timeout 5m
```

Serve published `wwwroot` in a secure context (localhost is suitable), with
`Cross-Origin-Opener-Policy: same-origin` and
`Cross-Origin-Embedder-Policy: require-corp`. The existing `threads.js` startup
also checks isolation; external package resources must satisfy CORS requirements.

When a daily SDK's exact multithread runtime is absent from nuget.org, restore
that version from Microsoft's matching `dotnet11` feed rather than changing the
runtime version:

```sh
dotnet restore Sia.Examples/Sia.Examples.Browser.csproj --source https://api.nuget.org/v3/index.json --source https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11/nuget/v3/index.json
```

Verify both preview routes on WebGPU, including constant colors, coordinate
gradients and time changes. Freeze identical inputs for pixel comparisons.
Also check errors, editing during compilation, queued/cancelled jobs, persistence,
panel resize/movement/visibility, device loss and closing during a pending job.
Existing ECS tests do not cover the browser integration.

Reference preparation remains on the UI thread and can briefly delay cold
compilations. Unchanged source/entry snapshots reuse the bounded compilation and
artifact caches. Phase timings are available in Console.
