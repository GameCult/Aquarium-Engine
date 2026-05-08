# Epiphany Aquarium Engine

![Epiphany Aquarium Engine icon](Aquarium-Engine-Icon.png)

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

Aquarium currently expects the sibling `CultMath` and `CultLib` repos at
`E:\Projects\CultMath` and `E:\Projects\CultLib`, or the equivalent
`..\..\..\CultMath` / `..\..\..\CultLib` paths from the engine project.

```powershell
dotnet build Aquarium.Engine.slnx
dotnet run --project src\Aquarium.Engine\Aquarium.Engine.csproj
```

For iteration while an Aquarium window is already open, use the dev reload
runner instead of `dotnet run`:

```powershell
.\scripts\dev-reload.ps1
```

It builds an apphost executable into a fresh disposable slot under
`artifacts\dev-reload`, records the slot/PID in PowerShell CLIXML, and launches
the runtime with a MessagePack CultCache store at
`artifacts\dev-reload\cultcache\aquarium-client.msgpack`.
The previous script-owned process is stopped only after the replacement build
succeeds. This keeps MSBuild away from the locked normal `bin\Debug` output
while a live window is still open.

For automatic rebuild/restart on source changes, use the watcher:

```powershell
.\scripts\dev-watch.ps1
```

The watcher polls source files, waits for writes to settle, builds into a new
slot, and only replaces the running Aquarium after the new build succeeds. If a
build fails, the previous good process keeps running and that same broken source
fingerprint is not retried until files change again.

Runtime live state is a typed CultCache document, not a loose sidecar file. The
first document is `epiphany.aquarium.live_state`, currently banking camera
target, yaw, pitch, distance, time, and save generation every few frames so a
reload can rehydrate without pretending memory is vibes.

Stop the script-owned Aquarium process:

```powershell
.\scripts\dev-stop.ps1
```

Headless smoke:

```powershell
dotnet run --project src\Aquarium.Engine\Aquarium.Engine.csproj -- --headless
```

Headless reload smoke without touching the normal build output:

```powershell
.\scripts\dev-reload.ps1 -Headless
```

Headless watch smoke:

```powershell
.\scripts\dev-watch.ps1 -Headless
```

The first cut opens a Win32 window directly and owns a D3D11 swapchain through
Vortice. No Stride runtime, no asset protocol, no borrowed game loop wearing a
fake mustache. See `docs/vortice-spine.md`.

Current camera controls are documented in `docs/input.md`.
