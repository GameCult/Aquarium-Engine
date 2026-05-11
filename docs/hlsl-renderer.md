# HLSL Renderer

Aquarium's visible world is currently a D3D12 HLSL renderer with a small,
explicit frame graph.

## Current Path

1. `D3D12Grid.hlsl` renders a 128x128 scalar Grid height target.
2. `D3D12Scene.hlsl` traces the Self emitter, planets, cursor locator, and Grid
   event lane into scene-linear HDR targets.
3. `D3D12Post.hlsl` builds bloom, resolves diagnostics/history, applies
   exposure and ACES, then presents.
4. `DirectWriteOverlay` draws crisp final-pixel debug UI after scene rendering.

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

The Grid is not an opaque surface. The scene shader traces it before the nearest
solid and emits premultiplied event radiance plus event metadata. The opaque
surface packet belongs to Self, planets, cursor, or empty space.

## Solids

Self and planets are analytic sphere hits. The cursor is a revolved MathWorld
teardrop SDF with SDF ripples and brass GGX material. It carries current and
previous cursor world anchors so temporal reprojection can account for object
motion.

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
