# HLSL Renderer

Aquarium's visible world is currently a D3D12 HLSL renderer with a small,
explicit frame graph.

## Current Path

1. Engine-owned `D3D12Grid.hlsl` renders a 128x128 scalar Grid height target.
2. Engine-owned `D3D12Scene.hlsl` traces the background and Grid into
   scene-linear HDR targets.
3. Epiphany-owned body shaders each get their own proxy pipeline:
   `D3D12FaceAgent.hlsl`, `D3D12ImaginationAgent.hlsl`,
   `D3D12EyesAgent.hlsl`, `D3D12BodyAgent.hlsl`, `D3D12HandsAgent.hlsl`,
   `D3D12SoulAgent.hlsl`, `D3D12LifeAgent.hlsl`, `D3D12SelfBody.hlsl`, and
   `D3D12CursorBody.hlsl`. Shared proxy mechanics live in
   `D3D12BodyCommon.hlsli` and `D3D12BodyProxy.hlsli`.
4. Engine-owned `D3D12Post.hlsl` builds bloom, resolves diagnostics/history, applies
   exposure and ACES, then presents.
5. `DirectWriteOverlay` draws crisp final-pixel debug UI after scene rendering.

C# feeds explicit frame constants: resolution, time, Grid radius, camera
position, presentation controls, previous frame state, cursor anchors, and debug
mode. CultMath is the CPU-side math grammar. Vortice math stays at the graphics
API membrane.

## Grid

The Grid height target is centered on the camera target; body anchors remain in
world space. The target stores height in `.r` only. A future useful expansion is
packed gradients in `.g/.b` if normal cost becomes worth the extra bandwidth.

Grid linework is derivative-aware in the final scene pass. Cartesian lines use
screen-space `fwidth` against their world-domain coordinate so minor and major
lines stay the same pixel width across zoom distances. Terrain isolines derive
from the Grid height field, while field lines quantize terrain gradient angle
and fade out on flat surfaces.

The fullscreen scene pass owns environment and Grid only. Bodies are not folded
into that pass. After the Grid writes scene color and travel-derived depth,
each body gets its own proxy draw and shader pipeline. The shared proxy vertex
shader computes a conservative screen rectangle from the uploaded body center
and bound radius. The body-specific pixel shader raymarches only that object
and writes travel-derived depth so the Grid and nearer bodies arbitrate
visibility through the depth target.

## Solids

Epiphany uploads Self, Face, Imagination, Eyes, Body, Hands, Soul, Life, and the
cursor through the engine `AquariumBodyVisual` buffer, including current center,
previous center, and state scalars. Aquarium owns the D3D12 upload, proxy draw,
depth, and presentation machinery; Epiphany owns which bodies exist, where they
are, and which shader files describe them.

The cursor is a luminous three-lobed hibiscus SDF that lives on the XY plane:
its center is one cursor radius above XY, so its lower contact tip lands at
Grid z=0 instead of sampling the Grid height field. The previous MathWorld
teardrop formula remains in `D3D12SdfMath.hlsli` for later reuse. Agent visuals
carry their previous centers in the structured buffer for temporal reprojection.

Lighting is currently direct and diegetic: Self is the emitter. No ambient fill
is allowed to quietly solve a bad light story.

## Temporal Diagnostics

The temporal resolver is documented in `docs/tsr-inspired-taa-spec.md`. The live
frame keeps color, metadata, and control history. Projection jitter uses a small
Halton sequence, and final presentation blends validated history. SDF object
highlights get a narrow hot-current history path so transient specular spikes
can be damped without relaxing travel, field, or normal validation. Bloom is
attenuated when the resolved scene has damped a much hotter current sample, so
transient fireflies do not keep blooming through the current-frame bloom path.
Dormant event lanes are not preserved without a producer and pass contract.

Debug modes:

- `0` final
- `1` raw current scene
- `2` reprojected history sample
- `3` history age
- `4` history weight
- `5` coverage and step ratio
- `6` current field identity
- `7` bloom contribution
- `8` exposed luminance
- `9` agent identity
- `10` agent SDF step count

`F1` cycles modes. Number keys select the first modes directly. Startup mode can
be set with `--render-debug` or `AQUARIUM_RENDER_DEBUG_MODE`.

## HDR

Presentation applies explicit exposure before display transformation and adds a
low-gain pre-tonemap bloom/veil pyramid. The bloom pass renders exposed
scene-linear HDR color into half-, quarter-, and eighth-resolution targets, uses
firefly-safe downsampling, blurs each level with separable horizontal/vertical
passes, then contributes gently before the ACES fit. This is not threshold glow.

## Overlay Text

Readable overlay text is handled by `DirectWriteOverlay`, using DirectWrite for
font layout/rasterization and Direct2D for final draw. D3D12 reaches that path
through D3D11On12 only after the scene frame is rendered. The bridge is
overlay-only.

The debug controls live in `Render/Ui/DebugUi.cs`. The grammar is a native
version of CultLib's generator API: panels, sections, sliders, toggles, options,
and buttons are declared in code and bound to engine state with explicit read
and write delegates. The visual language is compact, flat, and allowed to have
taste. Frighteningly controversial, apparently.

The overlay owns a private DirectWrite font collection built from bundled Google
Fonts files in `Assets/Fonts`: Montserrat for thin small-cap display text and
Ubuntu Sans for body/debug copy. Runtime must not depend on system-installed
fonts.
