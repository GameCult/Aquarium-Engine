# SDF Field Renderer Plan

Aquarium's 3D renderer should converge on one field grammar: every visible 3D
thing is an SDF field that can behave as a solid surface, a participating medium,
or both. The scene is inspired by Aetheria, where clouds are not a skybox layer.
They can sit above the Grid, below it, around objects, and around the camera.
That kills optimizations that assume clouds are distant hemispherical scenery.

## Goal

Render juicy SDF solids and SDF clouds from one renderer-owned field system.
Solids and clouds are not separate tricks. They are field objects with different
material regimes.

Each field should eventually expose:

- `distance`: signed boundary for surface hits, volume masks, and empty-space
  skipping.
- `density`: participating-media occupancy, usually derived from distance plus
  erosion/noise.
- `material`: solid albedo, emission, roughness, and surface behavior.
- `medium`: extinction, scattering, phase, emission, and shadow density.
- `bounds`: conservative world-space bounds for culling and acceleration.
- `flags`: solid, cloud, hybrid, emitter, casts volume shadow, receives medium.

## Non-Negotiable Constraint

Clouds are local world objects, not atmospheric wallpaper.

This means:

- No paraboloid sky-map assumption for the core cloud system.
- No hemisphere-only cloud renderer.
- No "clouds are always behind surfaces" composition shortcut.
- No far-only temporal reprojection scheme that falls apart when the camera is
  inside the medium.
- No skybox-relative noise that swims when the Grid/camera moves.

Clouds may still be rendered into specialized buffers later, but only if the
buffer represents local SDF media correctly when clouds are under, beside, and
around the camera.

## Field Model

### Solid Field

A solid field uses the SDF zero-crossing as its visible surface.

Runtime needs:

- bounded analytic or sampled SDF
- normal from the same function that produced the hit
- stable object-local coordinates
- material evaluation
- optional fuzzy shell density around the surface

### Cloud Field

A cloud field uses the SDF as a sculptural container and traversal accelerator.

Runtime needs:

- signed distance for outside skip and silhouette control
- density inside or near the SDF boundary
- erosion/domain noise in stable field-local space
- extinction/scattering/emission terms
- light visibility or a cheap self-shadow approximation
- preservation of symbolic silhouettes when the cloud is a sign, omen, or event

### Hybrid Field

A hybrid field has both solid and medium behavior. Examples:

- a hard SDF object with a glowing cloud mantle
- a cloud creature with solid core bones
- a Grid event glyph with dense interior fog and crisp outer silhouette
- Self-adjacent aura shells

## Render Architecture

### Stage 1: Analytic Field Prototype

Start inside the current fullscreen HLSL path:

1. Keep the existing terrain/body raymarch.
2. Add a small analytic SDF cloud registry in shader code.
3. March local cloud density along the camera ray up to the nearest solid hit or
   the Grid far distance.
4. Composite as:

   ```text
   final = cloud_scattering + cloud_transmittance * surface_color
   ```

This is allowed as a prototype because it proves the field contract and visual
direction. It is not allowed to become the final volume architecture by stealth.

### Stage 2: Field Instance Contract

Move analytic field identity out of hard-coded shader loops:

- CPU packs field instances into a structured buffer.
- Each instance has transform, bounds, kind, material id, medium id, and shape
  parameters.
- The current body froxel table evolves into a general field broad phase.
- Debug views show candidate fields, bounds, and step counts.

### Stage 3: Explicit Volume Pass

Add a renderer-owned low-resolution volume pass:

- local frustum/Grid volume, not sky hemisphere
- density/extinction/scattering/emission targets
- Self as first light source
- world/Grid anchored sampling
- front-to-back accumulated transmittance and in-scattering
- surface composition through the accumulated medium

This follows Wronski/Hillaire for the pass shape, but without assuming clouds
are distant sky content.

### Stage 4: SDF Acceleration

Only after profiling proves the need:

- add coarse occupancy or distance mip fields for cloud skipping
- add sparse brick SDF storage for large/static fields
- separate static and dynamic update paths
- use toroidal partial updates for camera-centered cascades
- expose debug tracing modes before relying on the accelerator

## Visual Direction

The target is not neutral fog. Aquarium fields should feel sculpted, luminous,
and strange while staying readable.

Useful techniques:

- distance-guided cloud marching
- erosion noise in field-local coordinates
- fuzzy SDF shells around solids
- edge silverlining from light-facing gradients
- backscatter/forward-scatter blend by density and transmittance
- emissive veins and lightning inside cloud SDFs
- authored symbolic silhouettes for weather glyphs and event clouds
- Sea of Thieves-style geometry/field silhouettes when the shape is gameplay
  language
- Dreams-style willingness to throw away beautiful machinery if the image gets
  generic

## Debug Views Required

Before this grows teeth, add debug surfaces for:

- field id
- field bounds
- signed distance
- density
- accumulated extinction
- transmittance
- in-scattering
- surface normal
- ray step count
- broad-phase candidate count

If we cannot see why a pixel looks wrong, the renderer is already lying.

## First Implementation Slice

The first code slice is intentionally small:

1. Add analytic local SDF cloud functions to `Aquarium.hlsl`.
2. March them in world space along each camera ray.
3. Allow clouds below, above, and around the Grid.
4. Composite cloud scattering/transmittance with the existing surface raymarch.
5. Keep all coordinates stable in world/object space.

This starts the visual grammar without committing to the final storage or pass
layout. A tiny honest machine. How upsettingly rare.
