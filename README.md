# Aquarium Engine

<p align="center">
  <img src="Aquarium-Engine-Icon.png" alt="Aquarium Engine icon" width="260" />
</p>

C# native runtime for Aquarium clients: windowing, D3D12 rendering, input,
reload, persistent state, debug UI, and packageable engine machinery.

Epiphany is the first serious client in this repo. It is not the engine's
identity.

## Intent

- Native C# host with a legible renderer spine and explicit ownership.
- Vortice/D3D12 backend with graphics API details contained inside the engine.
- `Aquarium.Engine.Contracts` as the client-facing boundary.
- `Aquarium.Epiphany` as the current Epiphany Aquarium client.
- `Aquarium.Sample.Minimal` as a small non-Epiphany boundary proof.
- DirectWrite/Direct2D overlay text after the scene pass for crisp debug and UI
  typography.
- Diegetic scene/UI surfaces driven by client objects instead of admin chrome.
- CultCache for typed settings, persistent client state, and reload recovery.
- CultNet-ready runtime identity for clients that need Epiphany communication.
- Procedural audio and visual state treated as coupled signals.

## Current Shape

The engine opens a Win32 window, owns a D3D12 swapchain, compiles and hot-reloads
HLSL, allocates render targets, preserves last-good shader/runtime state across
failed reloads, hosts the debug overlay, and presents scene-linear HDR through
the current post stack.

Client code is loaded behind `Aquarium.Engine.Contracts`, so a client can reload
while the engine host, window, renderer, and D3D device stay alive. Runtime state
is banked through CultCache MessagePack documents instead of loose JSON.

`Aquarium.Epiphany` currently supplies the Grid-centered camera policy, Epiphany
agent SDF shaders, body placement, CultCache documents, and future CultNet
interpretation. Those are client semantics. The engine consumes them as generic
rendering and runtime data.

## Repository Shape

This repository intentionally contains both the reusable Aquarium engine and the
Epiphany client while the client API is still being carved into its final shape:

- `src/Aquarium.Engine.Contracts`: Vortice-free client API and shared data
  contracts.
- `src/Aquarium.Engine`: Win32 host, D3D12 backend, reload transport, debug UI,
  audio host, and engine-owned assets.
- `src/Aquarium.Epiphany`: Epiphany Aquarium runtime, state interpretation,
  render-plan configuration, and agent shaders.
- `src/Aquarium.Zyphos`: planetary-scale rendering demo client with its own
  runtime, controls, scene state, and SDF planet shaders.
- `src/Aquarium.Sample.Minimal`: tiny non-Epiphany client that proves the host
  can run something other than Epiphany.

Splitting Aquarium and Epiphany into separate repos is plausible, but not owed
yet. The cut becomes clean when the contracts are stable enough that Epiphany can
move out without taking renderer policy, shader include plumbing, or reload
folklore with it. Until then, the repo split would mostly create paperwork and a
new place for confusion to hide.

## Run

Aquarium currently expects the sibling `CultMath` and `CultLib` repos at
`E:\Projects\CultMath` and `E:\Projects\CultLib`, or the equivalent
`..\..\..\CultMath` / `..\..\..\CultLib` paths from the engine project.

Aquarium consumes `AquaSynth.Faust` from the repo-local `packages` NuGet feed,
not as a live project reference to `AquaSynth`. That package pulls
`AquaSynth.Core` transitively for the built-in patch editor. This keeps Aquarium
builds stable while synth work is in flight. To intentionally update that
boundary, pack a new synth version and bump the `Aquarium.Engine` package
reference:

```powershell
.\scripts\update-synth-package.ps1
```

```powershell
dotnet build Aquarium.Engine.sln
dotnet run --project src\Aquarium.Engine\Aquarium.Engine.csproj -- --client-assembly src\Aquarium.Epiphany\bin\Debug\net10.0\Aquarium.Epiphany.dll
```

Open or build `Aquarium.Engine.sln`. It is the single solution inventory for the
repo.

`src\Aquarium.Sample.Minimal` is a tiny non-Epiphany client used to keep the
engine boundary honest. It references only `Aquarium.Engine.Contracts`, declares
an `AquariumRenderPlan`, and boots through the same host/client loader path.

`src\Aquarium.Zyphos` is the first nontrivial non-Epiphany demo. It renders a
rotating spherical planet, procedural continents, atmospheric rim light,
night-side city glints, and an orbiting moon through the same host, renderer,
reload, UI, bloom, and SDF proxy machinery used by Epiphany.

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

Run a different client project through the same slot machinery with
`-ClientProject`:

```powershell
.\scripts\dev-reload.ps1 -ClientProject src\Aquarium.Zyphos\Aquarium.Zyphos.csproj
```

If the visible dev window was closed but the last slot is still present, reopen
it without rebuilding:

```powershell
.\scripts\dev-reload.ps1 -Reopen
```

Visible and headless runs keep separate PID state and logs, so a headless smoke
does not replace the visible dev window.

For automatic rebuild/restart on source changes, use the watcher:

```powershell
.\scripts\dev-watch.ps1
```

Watch a specific client project the same way:

```powershell
.\scripts\dev-watch.ps1 -ClientProject src\Aquarium.Zyphos\Aquarium.Zyphos.csproj
```

The watcher polls source files, waits for writes to settle, builds into a new
slot, and only replaces the running Aquarium after the new build succeeds. If a
build fails, the previous good process keeps running and that same broken source
fingerprint is not retried until files change again.
It also watches the recorded script-owned process. If the visible window is
closed or the recorded process dies, the watcher relaunches from a fresh slot on
the next poll instead of sitting there with its hands folded like this is fine.
To reopen a closed visible window from the last good slot without rebuilding,
run the watcher with:

```powershell
.\scripts\dev-watch.ps1 -ReopenWhenClosed
```

Shader edits do not restart the process. The dev watcher fingerprints
`.hlsl`/`.hlsli` files under both the engine and selected client shader roots,
copies them into the running apphost slot, and lets the renderer rebuild the
active D3D12 pipelines from its current shader source root. A bad shader edit
leaves the previous working shaders bound and writes the compiler failure to
stderr.

Epiphany client/runtime code is split into `Aquarium.Epiphany` behind
`Aquarium.Engine.Contracts`. The host loads that client DLL through a collectible
assembly load context, while `dev-watch.ps1` builds client-only changes into
`artifacts\dev-reload\live-slots` and updates `live-current.txt`. The running
host sees the pointer change, loads and starts the new runtime first, then
disposes the old one only after the replacement is valid. A bad client DLL leaves
the previous runtime alive and reports the reload failure to stderr. The watcher
waits for the host log to acknowledge the exact client DLL path before calling a
client reload successful; pointer updates without host acknowledgement are treated
as reload failures. Successful reloads rehydrate through CultCache without
restarting the window or D3D device.
Host, renderer, contract, project, script, and `src\Aquarium.Engine\Assets`
content changes still rebuild and restart the apphost. Runtime content belongs
to the process image; shader hot reload cannot make a running renderer discover
new files it never loaded.

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
dotnet run --project src\Aquarium.Engine\Aquarium.Engine.csproj -- --headless --client-assembly src\Aquarium.Epiphany\bin\Debug\net10.0\Aquarium.Epiphany.dll
```

Headless reload smoke without touching the normal build output:

```powershell
.\scripts\dev-reload.ps1 -Headless
```

Renderer/client boundary check:

```powershell
.\scripts\verify-boundaries.ps1
```

Headless watch smoke:

```powershell
.\scripts\dev-watch.ps1 -Headless
```

Shipping publish:

```powershell
.\scripts\publish-win-x64.ps1 -Clean -Zip
```

This writes a self-contained Windows build to
`artifacts\publish\win-x64`, verifies the executable, Epiphany client DLL,
contracts DLL, icon, bundled fonts, and shader source, then optionally creates
`artifacts\publish\EpiphanyAquariumEngine-win-x64.zip`.

## Persistent State

This repo carries its own Aquarium memory:

- `state/map.yaml`: canonical project map and next actions.
- `state/memory.json`: durable taste, doctrine, renderer rules, and warnings.
- `state/evidence.jsonl`: compact lessons that should change future behavior.
- `state/scratch.md`: disposable working context for the current pass.

Keep that state current when the engine learns something durable. Dead lessons
go out with the trash; the repo is not a scrapbook.

## Docs

- `docs/aquarium-engine-doctrine.md`: engine doctrine and invariants.
- `docs/engine-client-boundary.md`: Engine/Epiphany ownership split.
- `docs/epiphany-extraction-render-api-plan.md`: migration plan for the client
  extraction and ergonomic render graph API.
- `docs/vortice-spine.md`: native host/renderer spine.
- `docs/hlsl-renderer.md`: current shader path.
- `docs/input.md`: camera and input contract.
- `docs/cult-runtime-surface.md`: CultCache/CultNet runtime surface.
