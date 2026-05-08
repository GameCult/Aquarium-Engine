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

## Deferred Grid Pass

The renderer now has a deferred Grid height target.

Current frame order:

1. C# bins Self and planet bounds into a small Grid-space froxel table and
   uploads fixed primitive id slots as a structured buffer.
2. `GridHeightPS` renders body/gravity height into a 128x128 Grid-space
   `R32G32B32A32_Float` render target.
3. `AquariumPS` raymarches solids and terrain against that height texture.
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
