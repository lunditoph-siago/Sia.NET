# Sia.NET

[![Build](https://img.shields.io/github/actions/workflow/status/lunditoph-siago/Sia.NET/test.yml?branch=master&event=push&label=Build&labelColor=343b45)](https://github.com/lunditoph-siago/Sia.NET/actions/workflows/test.yml?query=branch%3Amaster)
[![Playground: Live](https://img.shields.io/badge/Playground-Live-2386c8?labelColor=343b45)](https://lunditoph-siago.github.io/Sia.NET/)
[![License: BSD](https://img.shields.io/badge/License-BSD-737d8c?labelColor=343b45)](LICENSE)

**A reactive entity component system for .NET.**
Typed components and queries, composable systems, and generated change events.

**[Open the Playground →](https://lunditoph-siago.github.io/Sia.NET/)**
Edit and run the guided C# examples in your browser, with no local installation.
Choose **Open Editor Lab** for C# programs and programmable WebGPU rendering with
WGSL/SPIR-V output.

## Get started

In a .NET 10 or later project:

```bash
dotnet add package Sia
dotnet add package Sia.CodeGenerators
```

```csharp
using Sia;

using var world = new World();
world.Create(HList.From(new Position(0, 0), new Velocity(2, 1)));

foreach (var entity in world.Query(Matchers.Of<Position, Velocity>())) {
    var position = new Position.View(entity, world);
    ref readonly var velocity = ref entity.Get<Velocity>();
    position.X += velocity.X;
    position.Y += velocity.Y;
    Console.WriteLine(entity.Get<Position>());
}

public partial record struct Position([Sia] float X, [Sia] float Y);
public readonly record struct Velocity(float X, float Y);
```

Generated views write back to components and dispatch change events. Explore
systems, events and `Sia.Prelude` reactive hooks in the Playground's built-in
notebooks.
