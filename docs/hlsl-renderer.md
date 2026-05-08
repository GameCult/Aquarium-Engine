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

## Missing Grid Pass

The current Vortice renderer does **not** yet have the deferred gravity/Grid
height render texture that Aetheria and the Bevy prototype both assume. The
single HLSL pass still evaluates analytic Grid height directly while marching.

That is temporary. The real Grid path should be:

1. Render gravity/body sources into a Grid-space height target.
2. Sample that height target in the raymarch pass.
3. Build terrain normals from adjacent Grid texels in world space.
4. Keep the Grid target centered on the camera target while body anchors stay in
   world space.

Until that pass exists, the shader approximates the same normal construction by
sampling `terrainHeight` at adjacent world-space Grid texel offsets.
