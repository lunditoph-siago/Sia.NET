# Sia.NET

[![Build](https://img.shields.io/github/actions/workflow/status/lunditoph-siago/Sia.NET/test.yml?branch=master&event=push&label=Build&labelColor=343b45)](https://github.com/lunditoph-siago/Sia.NET/actions/workflows/test.yml?query=branch%3Amaster)
[![License: BSD](https://img.shields.io/badge/License-BSD-8250df?labelColor=343b45)](LICENSE)
[![Playground: Live](https://img.shields.io/badge/Playground-Live-2386c8?labelColor=343b45)](https://lunditoph-siago.github.io/Sia.NET/)

Sia.NET is a reactive entity component system for .NET. Generated component views
connect typed data to event-driven systems: change a value, and the systems that
observe it can respond.

## Quick start

Add Sia and its source generator to a .NET 10 or later project:

```bash
dotnet add package Sia
dotnet add package Sia.CodeGenerators
```

Here, changing a player's health triggers a system on the next stage tick:

```csharp
using Sia;

using var world = new World();
var player = world.Create(HList.From(new Health(100)));

using var stage = SystemChain.Empty
    .Add((ref Health health) => Console.WriteLine($"Health: {health.Value}"),
        trigger: EventUnion.Of<Health.SetValue>())
    .CreateStage(world);

new Health.View(player, world) { Value = 75 };
stage.Tick();

public partial record struct Health([Sia] int Value);
```

The generated view publishes the change; the stage processes it and prints
`Health: 75`.

The [Playground](https://lunditoph-siago.github.io/Sia.NET/) lets you edit and run
the C# examples in your browser, from ECS fundamentals to reactive systems.
Its Editor Lab also supports C# programs and programmable WebGPU rendering.
