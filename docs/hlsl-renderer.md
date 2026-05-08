# HLSL Renderer

Aquarium now has a first shader-owned image path.

## Current Path

- `D3D11Renderer` compiles `Render/Shaders/Aquarium.hlsl` at startup with
  `Vortice.D3DCompiler`.
- The renderer draws one fullscreen triangle.
- The pixel shader raymarches the Grid heightfield, Self sun, and orbiting
  planets.
- C# feeds only frame constants: resolution, time, Grid radius, and camera
  position.
- CultMath is referenced as the CPU-side math grammar. Vortice math stays at the
  graphics API membrane.

## Current Limits

This is still a first image path, not the final renderer. The shader keeps
planet broad-phase cheap by avoiding terrain samples inside every planet anchor.
Terrain-hugging orbit cups should come back through a real object table or
precomputed frame constants, not repeated procedural work inside every SDF
sample. That was the tiny furnace. It has been told to sit down.

## Ported Renderer Lessons

The first native renderer pass carries the pieces that fit a single HLSL
raymarch path:

- body-driven `powerPulse` Grid wells
- world-space Grid weather with domain warping and filament layers
- stable planet-local displaced SDFs
- material-specific normals so terrain and body shading do not re-evaluate the
  entire scene six times per visible pixel

Volumetric lighting, brick maps, HDR bloom, and debug controls are not one-pass
pixel-shader jobs. They need engine systems around the shader rather than more
stuffing in `AquariumPS`.

The SDF field renderer plan lives in `docs/sdf-field-renderer-plan.md`. Its
central constraint is that Aquarium/Aetheria-style clouds are local SDF media:
above, below, around, and sometimes enclosing the camera. They are not a fancy
skybox, so sky-dome/paraboloid-only cloud optimizations are not valid as the core
cloud architecture.

## Deferred Grid Pass

The renderer now has a deferred Grid height target.

Current frame order:

1. C# bins Self and planet bounds into a small Grid-space froxel table and
   uploads fixed primitive id slots as a structured buffer.
2. `GridHeightPS` renders body/gravity height into a 128x128 Grid-space
   `R32G32B32A32_Float` render target.
3. `AquariumScenePS` raymarches solids and the Grid against that height texture,
   writing linear HDR color plus ray travel.
4. Body SDF checks read only the primitive ids binned into the current froxel.
   Empty froxels skip body SDF work entirely.
5. Displaced body SDFs report a noisy hit distance separately from a conservative
   envelope step distance, so high-frequency surface detail can shape the
   silhouette without letting rays skip over it. When a ray is inside the
   conservative envelope but outside the noisy surface, it takes a shell escape
   step instead of starving on the minimum stride.
6. Terrain marching uses heightfield crossing and slope-aware steps instead of
   pretending `z - height(xy)` is a true Euclidean SDF. Body objects still use
   SDF distance steps.
7. Terrain normals sample adjacent Grid texels in world space instead of
   re-evaluating analytic height per normal tap.
8. The Grid target is centered on the camera target; body anchors remain in
   world space.
9. Direct2D/DirectWrite draws overlay text onto the swapchain backbuffer after
   the shader image is complete and before `Present`. Overlay text is therefore
   final-pixel UI, not HDR scene material, and should stay out of bloom,
   tonemapping, and raymarching unless a specific diegetic surface asks for the
   trouble.

The shader far distance is frame-derived, not a fixed constant. C# sends the
distance from the camera to the Grid origin plus the Grid radius, which matches
the circular Grid fade boundary and avoids wasting raymarch work past the
visible field. Changes to the `AquariumFrame` shader constant buffer are host
contract changes; `dev-watch.ps1` fingerprints that block and restarts the host
instead of treating it as ordinary shader hot reload.

The target currently stores height in `.r` only. The next useful expansion is to
store packed gradients in `.g/.b` during the Grid pass so the raymarcher can use
one texture fetch for height and a cheaper normal path when fidelity allows it.

Grid linework is derivative-aware in the final raymarch pass. Cartesian
Gridlines use screen-space `fwidth` against their world-domain coordinate so
minor and major lines stay the same pixel width across zoom distances. Terrain
height isolines and gradient-angle field lines follow the older Aetheria UI
shader pattern: height contours are derived from the Grid height field, while
angle lines quantize the terrain gradient direction and fade out on flat
surfaces.

## SDF Cloud Scaffold

`Aquarium.hlsl` still contains an analytic SDF cloud-medium scaffold. It defines
four bounded local cloud fields in world/Grid space, including clouds below and
around the Grid, but `AquariumPS` no longer composites those clouds into the
scene. The scaffold is retained only as evidence and throwaway machinery while
the waterfall field renderer is built.

```text
color = cloud_scattering + cloud_transmittance * surface_color
```

That composition shape belongs to the future explicit field/volume renderer, not
to the current fullscreen shader.

Current prototype traits:

- ellipsoid SDF cloud containers
- stable world/Grid-space placement
- analytic ray/ellipsoid interval stepping, so empty space jumps to real cloud
  entries instead of spending samples on SDF shells
- procedural erosion/noise inside each field
- feathered interior density, without positive-distance absorbing shells around
  the ellipsoid boundary
- Self-lit scattering and simple forward/back phase shaping
- no skybox/paraboloid assumption
- no sparse brick storage yet
- no debug view yet

## Transparent Grid Overlay

The Grid now renders as a transparent schematic overlay. `AquariumScenePS` traces
bodies separately from the Grid surface, shades solid bodies first, then
intersects the Grid height field. The Grid only draws when its surface is closer
than the nearest solid body, so it behaves like diegetic scene UI rather than a
final-image HUD layer over planets.

Grid coverage follows Aetheria's dithered particle transparency pattern:
screen-space stochastic alpha test instead of conventional alpha blending. This
uses Aetheria's `LDR_LLL1_0` blue-noise texture bound as a wrapping `R8_UNorm`
shader resource, following the `_DitheringTex` + `_FrameNumber *
1.61803398875` `ditherClip` path from `Dither Functions.cginc`.

This keeps the current scene legible while leaving future volumetrics to provide
the real spatial mass.

## Temporal Resolve

The first clean-room TSR-inspired temporal pass is documented in
`docs/tsr-inspired-taa-spec.md`.

Aquarium now renders the scene into an HDR/travel target first:

```text
rgb = linear scene color
a   = ray travel in world units
```

`AquariumResolvePS` then resolves that target against a ping-pong HDR history
texture. The resolver reconstructs the current world hit from the jittered camera
ray and current travel, projects that point into the previous camera basis,
samples history, clamps it to the current 3x3 neighborhood, rejects by travel
delta, and only then tonemaps to the backbuffer.

This started as Gate 1 of the TAA plan: camera reprojection, jitter, travel
rejection, neighborhood clamping, and history ping-pong. Gate 2A now adds a
scene metadata target and matching history metadata, storing field id and
surface normal so history is rejected across field/normal discontinuities.
The Grid also records its own travel and normal when it is the nearest diegetic
surface, even if the current stochastic coverage sample does not draw color.
Gate 2B adds stable field ids for Self/Grid/each planet and maps orbiting planet
hits back to their previous center before camera reprojection.
Gate 3A adds a current-frame temporal-control target. It carries reactive
strength, stochastic/composition coverage, and a reserved medium-opacity slot;
the resolve now reduces history for low-coverage reactive Grid samples before
future volumetrics arrive.
Gate 3B ping-pongs that control target as history too, so the resolver can
compare previous/current coverage and reserved medium opacity at the reprojected
UV.
Gate 3C stores accepted history age in the control-history `w` channel and
ramps history authority as that age grows.
Gate 3D fixes travel validation to compare previous history travel against the
reprojected point's expected distance from the previous camera, not the current
frame's camera-to-hit travel.
Gate 3E gives stochastic Grid history its own validation path: it bypasses
opaque-surface neighborhood color clamp/color-delta rejection while still using
field, travel, normal, coverage, reactive, and age checks.
Gate 3F gives that stochastic Grid path a higher-retention history blend and no
coverage/reactive penalty, because low coverage is the signal that needs
temporal accumulation rather than the signal that should lose history.
Gate 3G point-loads identity/control history and nearest-loads Grid history
color, preventing bright foreground silhouettes from bilinearly bleeding into
background Grid accumulation. Grid retention is also pulled back from `0.975` to
`0.94` to keep lines less anesthetized.

Temporal debug modes are available in the running window: `F1` cycles modes and
number keys `0` through `4` select them directly. Mode `0` is final resolve,
`1` is raw current scene, `2` is the reprojected history sample, `3` is history
age, and `4` is history weight. The startup mode can still be set with
`--render-debug` or `AQUARIUM_RENDER_DEBUG_MODE`.

It deliberately does not pretend to solve general non-rigid field velocity,
volumetric history, or history resurrection yet.

## Overlay Text

Readable overlay text is handled by `DirectWriteOverlay`, using DirectWrite for
font layout/rasterization and Direct2D for the final draw. The D3D swapchain uses
`B8G8R8A8_UNorm` so the same backbuffer can be shared with Direct2D without
format drama. This path is for debug UI, terminal text, inspectors, and future
CultUI overlay surfaces. Diegetic labels that live in the scene still belong in
the renderer as MSDF/SDF billboards.

The overlay owns a private DirectWrite font collection built from bundled Google
Fonts files in `Assets/Fonts`: Montserrat for thin small-cap display text and
Ubuntu Sans for body/debug copy. The runtime does not depend on those fonts
being installed in Windows.

Startup status text is drawn by the Win32 splash path before D3D exists. That
path uses GDI with the same bundled font files registered privately for the
process, then reports coarse initialization stages while the renderer creates
the device, swapchain targets, shaders, and buffers.
