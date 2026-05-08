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

## Bevy Feature Port

The first Bevy feature port brings across the pieces that fit a single HLSL
raymarch pass:

- body-driven `powerPulse` Grid wells
- world-space Grid weather with domain warping and filament layers
- stable planet-local displaced SDFs
- material-specific normals so terrain and body shading do not re-evaluate the
  entire scene six times per visible pixel

The Bevy SH/froxel lighting, brick maps, HDR bloom graph, and debug terminal are
not directly ported yet. Those need engine systems around the shader rather than
more one-pass pixel shader stuffing. The machete remains on the wall, tastefully
lit.

## Deferred Grid Pass

The renderer now has a deferred Grid height target.

Current frame order:

1. C# bins Self and planet bounds into a small Grid-space froxel table and
   uploads fixed primitive id slots as a structured buffer.
2. `GridHeightPS` renders body/gravity height into a 128x128 Grid-space
   `R32G32B32A32_Float` render target.
3. `AquariumPS` raymarches solids and terrain against that height texture.
4. Body SDF checks read only the primitive ids binned into the current froxel.
   Primitive bins are padded by one froxel so rays can see nearby SDFs before
   they cross the solid bounds. Empty froxels skip body SDF work entirely.
5. Terrain marching uses heightfield crossing and slope-aware steps instead of
   pretending `z - height(xy)` is a true Euclidean SDF. Body objects still use
   SDF distance steps.
6. Terrain normals sample adjacent Grid texels in world space instead of
   re-evaluating analytic height per normal tap.
7. The Grid target is centered on the camera target; body anchors remain in
   world space.

The target currently stores height in `.r` only. The next useful expansion is to
store packed gradients in `.g/.b` during the Grid pass so the raymarcher can use
one texture fetch for height and a cheaper normal path when fidelity allows it.
