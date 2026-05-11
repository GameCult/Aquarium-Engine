# HLSL Renderer

Aquarium's visible world is currently a D3D12 HLSL renderer with a small,
explicit frame graph.

## Current Path

1. `D3D12Grid.hlsl` renders a 128x128 scalar Grid height target.
2. `D3D12Scene.hlsl` traces the background and Grid into scene-linear HDR
   targets.
3. `D3D12Bodies.hlsl` draws one bounded proxy quad per visible body and
   raymarches only that object. Reusable SDF functions live in
   `D3D12SdfMath.hlsli`; current role character SDFs live in
   `D3D12AgentCharacters.hlsli`.
4. `D3D12Post.hlsl` builds bloom, resolves diagnostics/history, applies
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
each body gets an instanced proxy quad whose vertex shader computes a
conservative screen rectangle from the uploaded body center and bound radius.
The proxy pixel shader raymarches only that object and writes travel-derived
depth so the Grid and nearer bodies arbitrate visibility through the depth
target.

## Solids

Self, the cursor, and role bodies are uploaded through `AgentVisualGpu`,
including current center, previous center, role id, activity, heartbeat,
pressure, expression, object kind, and LOD tier. The first visual slice keeps
the fixed orbit fallback, renders Body and Imagination as LOD 1 role SDFs inside
bounded per-object proxy draws, and keeps the remaining roles as simple distinct
fallback organs.

The cursor is a luminous three-lobed hibiscus SDF that lives on the XY plane:
its center is one cursor radius above XY, so its lower contact tip lands at
Grid z=0 instead of sampling the Grid height field. The previous MathWorld
teardrop formula remains in `D3D12SdfMath.hlsli` for later reuse. Agent visuals
carry their previous centers in the structured buffer for temporal reprojection.

Lighting is currently direct and diegetic: Self is the emitter. No ambient fill
is allowed to quietly solve a bad light story.

## Temporal Diagnostics

The temporal resolver is documented in `docs/tsr-inspired-taa-spec.md`. The live
frame currently keeps history and control targets for inspection and future
work, but projection jitter is disabled and final presentation favors current
truth over stale history.

Debug modes:

- `0` final
- `1` raw current scene
- `2` reprojected history sample
- `3` history age
- `4` history weight
- `5` current temporal control
- `6` current field identity
- `7` bloom contribution
- `8` exposed luminance
- `9` agent role identity
- `10` agent material id
- `11` agent SDF step count
- `12` agent LOD tier
- `13` agent SDF cost tier

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
