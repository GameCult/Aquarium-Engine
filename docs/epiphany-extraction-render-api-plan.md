# Epiphany Extraction And Render API Plan

Aquarium is the engine. Epiphany is one client app.

The current solution has the first physical boundary in place:

- `Aquarium.Engine` owns the host, D3D12 renderer, input loop, shader reload,
  debug overlay, and client runtime loader.
- `Aquarium.Engine.Contracts` is the shared API surface between host and client.
- `Aquarium.Epiphany` owns the current runtime state, camera rig, CultCache
  documents, and client factory.

That split is real but shallow. The engine still carries Epiphany-shaped world
knowledge inside `D3D12Renderer`: fixed role counts, Self/cursor constants,
agent shader paths, body light setup, Grid brush generation, orbit fallback
motion, and the body visual buffer layout. The client can reload, but it cannot
yet describe what gets rendered. The renderer remains a monolith with a client
mask taped to the front. Rude, but accurate.

## Design Target

Client authors should be able to describe a frame in C# using named engine
abstractions:

- render targets with formats, size policies, clears, history, and persistence
- cameras with projection, view, previous-frame state, jitter, and named use
- shaders with hot reload, includes, entry points, root bindings, and defaults
- passes that declare reads, writes, depth, viewport, draw shape, and diagnostics
- structured buffers, constants, textures, samplers, cubemaps, and body registries
- standard features such as bloom, tonemapping, overlay, IBL, resize handling,
  shader reload, transient descriptors, barriers, and debug views

Aquarium should make the ordinary path boring:

```csharp
public sealed class EpiphanyClient : AquariumClient
{
    protected override void Configure(AquariumApp app)
    {
        var mainCamera = app.Cameras.Orbit("main")
            .LookAtGrid()
            .WithHistory();

        var gridHeight = app.RenderTargets.Create("grid-height")
            .FixedSize(128, 128)
            .Format(RenderFormat.R16Float)
            .Clear(0.0f);

        var scene = app.RenderTargets.Hdr("scene")
            .MatchWindow()
            .WithHistory();

        app.Graph.Pass("grid-height")
            .Fullscreen("Shaders/GridHeight.hlsl", "GridHeightPS")
            .Write(gridHeight)
            .BindFrame()
            .Bind("GridBrushes", GridBrushes);

        app.Graph.Pass("scene")
            .Fullscreen("Shaders/Scene.hlsl", "ScenePS")
            .Read(gridHeight)
            .Write(scene.Color, scene.Metadata, scene.Control)
            .Depth(scene.Depth)
            .UseCamera(mainCamera)
            .Bind("BodyLights", BodyLights);

        app.Features.Bloom(scene.Color);
        app.Features.Tonemap(scene.Color).Present();
        app.Features.DirectWriteOverlay();
    }
}
```

That is the level of API Aquarium should expose: explicit enough to be durable,
ergonomic enough that nobody has to learn descriptor heaps before rendering a
flower. Descriptor heaps are Aquarium's problem. If the client author asks for a
sharp object, the engine hands them a handle, not the knife factory.

## Literature Anchor

This plan follows the render graph pattern used in production engines:

- Unity describes render graphs as high-level render-pass and resource
  declarations that simplify pipeline configuration and let the system manage
  resources efficiently: https://docs.unity.cn/Packages/com.unity.render-pipelines.core%4014.0/manual/render-graph-system.html
- Unreal's Rendering Dependency Graph defers execution until all passes are
  gathered, then uses whole-frame knowledge for scheduling, barriers, memory,
  async compute, and validation:
  https://dev.epicgames.com/documentation/ru-ru/unreal-engine/rendering-dependency-graph?application_version=4.27
- Frostbite's FrameGraph talk frames the same problem as a graph of passes and
  resources that keeps rendering features modular without giving up efficiency:
  https://www.gdcvault.com/play/1024612/FrameGraph-Extensible-RenderingArc
- Granite's Vulkan render graph writeup is useful implementation pressure:
  declare reads and writes, validate, traverse dependencies, assign logical
  resources to physical resources, generate barriers, alias compatible
  transient targets, then execute:
  https://themaister.net/blog/2017/08/15/render-graphs-and-vulkan-a-deep-dive/

The Aquarium version should start smaller than those engines. No async compute,
no memory aliasing, no multipass merging in the first cut. First: client-owned
declarations, engine-owned execution.

## Current Coupling Inventory

These are the seams to cut.

### Shared Contracts

`Aquarium.Engine.Contracts` currently exposes:

- `IAquariumRuntime`
- `IAquariumRuntimeFactory`
- `AquariumRuntimeOptions`
- `AquariumFrame`
- `GraphicsSettings`
- `GridFrame`
- input state

`AquariumFrame` is too specific. It assumes one Grid, one camera position, one
cursor, one time scalar, and a renderer that knows what those mean.

### Host

`AquariumHost` owns:

- client assembly load and reload pointer
- window creation
- renderer construction
- debug input routing
- settings synchronization
- mouse projection onto the Grid plane

The mouse projection is client policy. The engine may provide ray utilities,
but `ProjectMouseToGridPlane` belongs in the client or in a reusable camera
helper chosen by the client.

### Renderer

`D3D12Renderer` owns both engine machinery and Epiphany scene policy:

- engine machinery: device, swapchain, fences, command list, descriptor arenas,
  upload ring, shader compilation, render targets, overlays, resize, resource
  transitions, pipeline state
- Epiphany policy: Grid height target, Self gravity brush, role agent count,
  fixed agent body shader list, cursor hibiscus buffer slot, body lights, orbit
  fallback positions, Grid placement math, agent state scalars, debug mode names

The renderer should retain the machinery and lose the policy.

### Shader Assets

Engine shader assets currently include Epiphany bodies:

- `D3D12SelfBody.hlsl`
- `D3D12FaceAgent.hlsl`
- `D3D12ImaginationAgent.hlsl`
- `D3D12EyesAgent.hlsl`
- `D3D12BodyAgent.hlsl`
- `D3D12HandsAgent.hlsl`
- `D3D12SoulAgent.hlsl`
- `D3D12LifeAgent.hlsl`
- `D3D12CursorBody.hlsl`
- `D3D12AgentCharacters.hlsli`

Those should move under the Epiphany client shader root once the engine can load
client-declared shaders. Engine-owned includes should remain only when they are
generic: SDF primitives, body proxy mechanics, PBR lighting, fullscreen vertex
helpers, packing helpers, and graph binding conventions.

## Target Project Shape

First stable shape inside this repo:

```text
src/
  Aquarium.Engine.Contracts/       public client-facing API
  Aquarium.Engine/                 host plus D3D12 backend implementation
  Aquarium.Epiphany/               Epiphany client app/runtime
```

Longer-term package shape:

```text
src/
  Aquarium.Engine.Abstractions/    public API, no Vortice dependency
  Aquarium.Engine.D3D12/           D3D12 backend
  Aquarium.Engine.Host/            default Win32 host executable
  Aquarium.Epiphany/               client app, eventually movable out of repo
```

Do not split projects before the API shape earns it. Premature project shrapnel
is not architecture. It is paperwork with delusions.

## Public API Model

### Client Entry

Replace `IAquariumRuntime.Frame` with a richer client lifecycle:

```csharp
public interface IAquariumClient : IDisposable
{
    void Configure(AquariumAppBuilder app);
    void Load(AquariumLoadContext load);
    void Update(AquariumUpdateContext update);
    void Render(AquariumRenderContext render);
    void FlushState();
}
```

`Configure` declares stable graph shape. `Update` mutates CPU state. `Render`
uploads per-frame data and selects dynamic pass instances. The engine compiles
and executes the graph.

Keep a compatibility adapter from the old `IAquariumRuntime` while migrating,
then remove it when Epiphany is fully on the new lifecycle.

### Handles

Expose typed handles, not D3D12 objects:

```csharp
public readonly record struct RenderTargetHandle(string Name);
public readonly record struct DepthTargetHandle(string Name);
public readonly record struct CameraHandle(string Name);
public readonly record struct ShaderHandle(string Name);
public readonly record struct BufferHandle<T>(string Name) where T : unmanaged;
public readonly record struct TextureHandle(string Name);
public readonly record struct PassHandle(string Name);
```

Names are diagnostic identity. The engine maps them to physical resources.

### Render Targets

Render target declarations should cover the common case without ceremony:

```csharp
app.RenderTargets.Create("scene")
    .MatchWindow(scale: 1.0f)
    .Format(RenderFormat.Rgba16Float)
    .Clear(Color.Black)
    .Sampled()
    .History(frames: 2);

app.RenderTargets.Create("grid-height")
    .FixedSize(128, 128)
    .Format(RenderFormat.R16Float)
    .Clear(0.0f)
    .Sampled();
```

Engine responsibilities:

- allocate and resize physical resources
- allocate RTV, DSV, SRV, UAV descriptors
- create transient descriptors per frame
- preserve history targets
- validate format and usage combinations
- transition states
- expose debug names and graph viewer data

### Cameras

The engine should provide camera math utilities and history storage. The client
chooses semantics:

```csharp
var camera = app.Cameras.Perspective("main")
    .Position(() => CameraRig.Position)
    .LookAt(() => CameraRig.Target3)
    .VerticalFovDegrees(58)
    .NearFar(0.05f, 500.0f)
    .WithPreviousFrame()
    .WithMouseRays();
```

Engine-owned:

- view/projection matrices
- inverse matrices
- viewport transforms
- previous-frame storage
- jitter policy
- screen ray generation helpers

Client-owned:

- what the camera tracks
- Grid center
- orbit controls
- mouse-to-world projection policy

### Shaders And Includes

Shader declarations should hide D3DCompile details but expose the real contract:

```csharp
var sceneShader = app.Shaders.Hlsl("scene")
    .File("Shaders/Scene.hlsl")
    .Vertex("FullscreenTriangleVS")
    .Pixel("ScenePS")
    .IncludeDirectory("Shaders/Includes")
    .HotReload();
```

Engine-owned features:

- include expansion
- compile diagnostics
- previous-good shader retention
- pipeline rebuild in the background
- root binding validation
- standard include library

Client-owned:

- shader source files
- shader pass declarations
- object-specific SDFs and materials

### Passes

Passes should declare resource use explicitly:

```csharp
app.Graph.Pass("bloom-prefilter")
    .Fullscreen(sceneShader)
    .Read("scene")
    .Write("bloom-0")
    .BindFrame()
    .When(features => features.BloomEnabled);
```

Initial pass types:

- fullscreen graphics pass
- proxy rectangle graphics pass
- instanced graphics pass
- compute pass
- copy/blit pass
- present pass
- overlay pass

Initial graph compiler:

- validate resource existence
- validate acyclic dependencies
- topologically sort by declared reads/writes
- emit D3D12 transitions
- allocate transient frame descriptors
- rebuild on graph, shader, target, or window changes

Later graph compiler:

- transient target aliasing
- pass culling
- async compute
- graph visualization
- queue scheduling

## It-Just-Works Feature Layer

The public API should include feature builders over the low-level graph. They
generate normal graph declarations, not secret renderer code.

### Presentation

```csharp
app.Features.Presentation("default")
    .From(scene.Color)
    .Exposure(settings.SceneExposure)
    .Bloom(settings.Bloom)
    .Tonemap(Tonemap.AcesFitted)
    .Present();
```

### Bloom

```csharp
app.Features.Bloom(scene.Color)
    .Levels(3)
    .FireflySafe()
    .PreTonemap();
```

### IBL

```csharp
app.Features.ImageBasedLighting("studio")
    .Pmrem("Textures/studio2_pmrem.dds")
    .Irradiance("Textures/studio2_irradiance.dds")
    .BindAs("StudioIbl");
```

### SDF Bodies

```csharp
app.Features.SdfBodies("agents")
    .Buffer<AgentVisualGpu>("AgentVisuals", maxCount: 64)
    .SharedInclude("Shaders/D3D12BodyProxy.hlsli")
    .Role("Self", "Shaders/SelfBody.hlsl")
    .Role("Imagination", "Shaders/ImaginationBody.hlsl")
    .DepthAgainst(scene.Depth)
    .Write(scene.Color, scene.Metadata, scene.Control);
```

The engine supplies proxy drawing, bounds, depth writes, common lighting, and
debug counters. The client supplies the body list and shaders.

### Debug

The debug layer should read graph metadata:

- pass list and timing
- target dimensions, formats, and history state
- shader compile status
- descriptor arena usage
- upload ring usage
- selected resource visualizers
- graph validation warnings

Debug views should be registered, not hardcoded:

```csharp
app.Debug.Views
    .Add("Final", scene.Color)
    .Add("Bloom", bloom.Output)
    .Add("Agent Steps", scene.Metadata.Channel("steps"));
```

## Migration Plan

### Phase 0: Freeze The Boundary

Goal: prevent new leaks while the old renderer still exists.

Tasks:

- Keep `docs/engine-client-boundary.md` authoritative.
- Add an architectural test or script check that `src/Aquarium.Engine` contains
  no `Aquarium.Epiphany` references.
- Add a second check that engine shader names do not include Epiphany role names
  once shaders move.
- Mark current fixed renderer path as legacy in docs, not as permanent API.

Exit criteria:

- Boundary checks run in local verification.
- New work has an obvious place to live.

### Phase 1: Rename Contracts Into Abstractions

Goal: turn `Aquarium.Engine.Contracts` into an actual engine API surface.

Tasks:

- Introduce `AquariumAppBuilder`, `AquariumClient`, graph handles, resource
  descriptors, camera descriptors, shader descriptors, pass descriptors, and
  feature descriptors.
- Keep old `IAquariumRuntime` beside the new API temporarily.
- Add XML docs or README examples for the common path.
- Keep all contracts Vortice-free.

Exit criteria:

- A tiny sample client can declare one target, one fullscreen shader, and a
  presentation pass without referencing `Aquarium.Engine.Render` or Vortice.

### Phase 2: Build The Minimal Graph Compiler Inside Engine

Goal: execute client-declared resources and passes without replacing the whole
renderer at once.

Tasks:

- Add internal `RenderGraph`, `RenderGraphCompiler`, `CompiledRenderGraph`, and
  `RenderGraphExecutor`.
- Map public descriptors to internal D3D12 resources.
- Support fullscreen graphics passes first.
- Support named render target reads and writes.
- Support one depth target.
- Generate transitions from declared pass use.
- Keep shader hot reload and previous-good pipeline behavior.
- Add graph validation errors with pass/resource names.

Exit criteria:

- Existing scene, bloom, and present path can be represented as graph passes,
  even if some callbacks still call existing private methods.

### Phase 3: Convert Presentation And Bloom To Features

Goal: move generic post-processing out of bespoke renderer flow.

Tasks:

- Express HDR scene target, bloom pyramid, history targets, and present pass as
  graph resources.
- Implement `app.Features.Bloom`.
- Implement `app.Features.Presentation`.
- Move render debug mode registration into graph/debug descriptors.
- Keep DirectWrite overlay as an engine feature.

Exit criteria:

- A non-Epiphany sample can render a color target, bloom it, tonemap it, and
  present without defining Grid, agents, cursor, or CultNet.

### Phase 4: Move Epiphany Shaders And Body Policy To Client

Goal: remove role bodies and cursor semantics from engine source.

Tasks:

- Move role shader files and `D3D12AgentCharacters.hlsli` into
  `src/Aquarium.Epiphany/Shaders`.
- Keep generic includes in engine:
  - fullscreen helpers
  - SDF primitives
  - PBR lighting
  - body proxy mechanics
  - packing and frame constants
- Replace fixed `RoleAgentCount`, fixed shader path arrays, and fixed
  `AgentVisualGpu` ownership with a client-declared SDF body feature.
- Move `BuildAgentVisualTable`, `BuildBodyLightTable`, `BuildGridHeightBrushes`,
  orbit fallback positions, cursor radius, and Self constants into
  `Aquarium.Epiphany`.
- Let Epiphany upload buffers by handle.

Exit criteria:

- `src/Aquarium.Engine` contains no role names, no cursor hibiscus, no Self
  constants, no agent orbit fallback, and no Epiphany shader paths.

### Phase 5: Move Grid Policy To Epiphany Or A Reusable Feature

Goal: decide whether Grid is a generic Aquarium feature or Epiphany client
policy.

Recommended split:

- Engine feature: height-field target, brush rendering primitive, reflective
  surface shader helper, debug visualization.
- Epiphany policy: Grid center, radius rule, Self gravity bowl, role gravity
  wells, radial wave parameters, body placement rule.

Tasks:

- Expose `app.Features.HeightField`.
- Expose CPU helper for evaluating brush stacks when the client asks for it.
- Move current brush stack construction into Epiphany.
- Move mouse-to-Grid projection out of `AquariumHost`.

Exit criteria:

- A different client can choose no Grid, a different height field, or a static
  render target without touching engine code.

### Phase 6: Replace Frame Contract

Goal: stop `AquariumFrame` from being the rendering protocol.

Tasks:

- Introduce `AquariumFrameContext` for time, window size, input, graph resource
  access, frame constants, and upload helpers.
- Let clients define their own frame constants structs.
- Keep engine standard constants optional:
  - resolution
  - time
  - camera matrices
  - previous camera matrices
  - presentation settings
- Provide `Frame.UploadConstant<T>`, `Frame.UploadBuffer<T>`, and
  `Frame.SetGraphValue`.
- Remove renderer dependence on `AquariumFrame`.

Exit criteria:

- `IAquariumRenderer.Render` consumes a compiled graph frame, not an Epiphany
  shaped frame record.

### Phase 7: Split Host From Backend When Earned

Goal: make Aquarium usable as an engine package, not only an executable.

Tasks:

- Split public abstractions from D3D12 backend if the contract has stabilized.
- Keep Win32 host as the default app host.
- Let clients choose hosted mode:
  - engine-owned window
  - headless
  - later embedded/native handle mode
- Package shader include library and default features as engine assets.

Exit criteria:

- A fresh client can reference Aquarium abstractions, implement a client class,
  run through the host, and declare render graph resources without copying
  Epiphany code.

## Order Of Attack

Do not start by moving every file. That produces thrilling amounts of rubble.

Recommended first implementation slice:

1. Add public render target, shader, pass, camera, and feature descriptor types
   to contracts.
2. Add a graph builder that can describe the current fixed pipeline without
   executing it.
3. Add a debug dump of the declared graph.
4. Convert bloom and presentation to execute from graph declarations.
5. Convert body proxy draws to a client-declared body registry.
6. Move Epiphany shaders out of engine.
7. Move Grid and agent/cursor frame construction out of renderer.

Each slice must preserve:

- shader reload keeps last good pipelines
- failed client reload keeps previous client alive
- resize rebuilds all size-bound targets
- debug overlay remains readable
- headless smoke still completes
- engine source stays free of Epiphany references

## Verification Gates

Every migration pass should run:

```powershell
dotnet build Aquarium.Engine.slnx --no-restore
.\scripts\dev-reload.ps1 -Headless -RetainSlots 4
rg -n -S -- "Aquarium\.Epiphany|Self|Imagination|CultNet|hibiscus" src\Aquarium.Engine
```

The final `rg` should shrink over time. At the end it should be empty except
for generic documentation comments if any survive, and they probably should not.

## Non-Goals For The First Graph

- no async compute
- no transient memory aliasing
- no general material system
- no visual node editor
- no automatic shader reflection dependency on day one
- no project split until the API stops moving under our feet
- no "compatibility mode" escape hatch that lets client code call D3D12 directly

The first graph exists to establish ownership and data flow. Optimization comes
after the graph tells the truth.

## Success Definition

Aquarium is clean when:

- Engine code can render a non-Epiphany sample client.
- Epiphany can add a render target, camera, shader, buffer, and pass without
  editing `D3D12Renderer`.
- Epiphany shaders live with Epiphany.
- Engine shader includes are generic.
- Resource lifetime, descriptors, barriers, resize, hot reload, presentation,
  and debug inspection are engine-owned.
- Client code describes intent. Engine code makes it work.
