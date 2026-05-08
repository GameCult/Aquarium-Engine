# Epiphany Aquarium Engine

C# engine-core seed for the next Epiphany Aquarium line.

This repo starts after the web and Rust prototypes taught the expensive lesson:
the Aquarium wants an owned renderer, owned state model, and owned interaction
grammar. Stride can stay on the shelf as reference material, but the runtime is
Aquarium-owned: window, renderer, state, and interaction grammar all belong
here.

## Intent

- C# host with renderer architecture kept legible in Rider.
- Vortice/D3D11 first, with the native boundary kept explicit.
- Grid-centered camera and world-space interaction invariants as first-class
  engine contracts.
- Diegetic UI surfaces driven by Aquarium objects instead of admin chrome.
- CultNet for Epiphany communication.
- CultCache for settings, persistent client state, and reload recovery.
- Procedural audio and visual state treated as coupled signals.

## Borrowed Lessons

- The web client proved the interaction grammar: objects first, panels only as
  local unfold surfaces.
- The Bevy prototype proved the renderer direction: raymarched bodies,
  Grid-space fields, HDR, bloom, and camera/Grid invariants.
- The Rust synth work proved the audio module deserves its own small, sharp API.

## First Build Target

Create the smallest C# executable that opens a window, owns the camera/Grid
invariant, and renders one sun plus orbiting planets with diegetic labels.
Everything else earns its way back in.

## Run

Aquarium currently expects the sibling `CultMath` repo at `E:\Projects\CultMath`
or the equivalent `..\..\..\CultMath` path from the engine project.

```powershell
dotnet build Aquarium.Engine.slnx
dotnet run --project src\Aquarium.Engine\Aquarium.Engine.csproj
```

Headless smoke:

```powershell
dotnet run --project src\Aquarium.Engine\Aquarium.Engine.csproj -- --headless
```

The first cut opens a Win32 window directly and owns a D3D11 swapchain through
Vortice. No Stride runtime, no asset protocol, no borrowed game loop wearing a
fake mustache. See `docs/vortice-spine.md`.
