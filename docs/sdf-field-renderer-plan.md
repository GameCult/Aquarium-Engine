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

This renderer needs a waterfall implementation shape. The wrong abstraction here
is not a harmless intermediate state; it becomes the renderer. Build the machine
we want, then bring it up behind explicit gates.

### Gate 1: Field Contract

Define the field instance ABI before adding more visual tricks:

- CPU-owned field registry.
- Stable field ids and debug names.
- World transform plus inverse transform.
- Conservative bounds.
- Shape parameters.
- Material and medium ids.
- Field flags for solid, cloud, hybrid, emitter, shadow caster, and receiver.
- Debug mode ids and per-field visibility toggles.

Hard-coded shader field loops are temporary scaffolding only. Do not extend them
except to replace them.

Current status: the renderer now packs a first fixed-size field instance buffer
for Self, planets, and local cloud/medium ellipsoids. HLSL consumes that buffer
in a packed froxel medium pass that writes density, extinction `sigma_t`,
scattering `sigma_s`, albedo, slice transmittance, and raw emitter irradiance
before light propagation. Density noise is evaluated in field-local/world
coordinates, not animated screen space. Final composition uses Beer-Lambert
transmittance and single-scattering integration behind a persisted intensity
gate, while direct ray debug modes remain the correctness reference. This is the
first real volume pass, not the final sparse/cascaded volume renderer.

### Gate 2: Broad Phase

Move field selection out of brute-force pixel loops:

- CPU packs field instances into a structured buffer.
- Each instance has transform, bounds, kind, material id, medium id, and shape
  parameters.
- The current body froxel table evolves into a general field broad phase.
- Candidate lists are available to solid and medium passes.
- Debug views show candidate fields, bounds, rejected fields, and step counts.

### Gate 3: Explicit Volume Pass

Add a renderer-owned low-resolution volume pass:

- local frustum/Grid volume, not sky hemisphere
- density/extinction/scattering/emission targets
- Self as first light source
- world/Grid anchored sampling
- front-to-back accumulated transmittance and in-scattering
- surface composition through the accumulated medium

This follows Wronski/Hillaire for the pass shape, but without assuming clouds
are distant sky content.

### Gate 4: Surface And Medium Composition

Make composition explicit:

- solid ray hits produce surface color, depth, normal, material, and field id
- medium integration respects surface depth
- clouds in front of surfaces affect surfaces through transmittance
- clouds behind surfaces do not leak through opaque solids
- multiple cloud intervals accumulate front-to-back without sample-budget
  starvation
- debug views expose each composition term

Transparent stochastic surfaces are a separate class from opaque solids. Grid
lines and particles should emit transparent events and share the pipeline
described in `docs/stochastic-transparent-surface-pipeline.md`. They do not own
canonical opaque travel, and they must not use the scene first-hit path as their
composition model.

### Gate 5: SDF Acceleration

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

## Current Temporary Scaffold

`Aquarium.hlsl` currently contains an analytic local cloud scaffold, but it is no
longer composited into the scene. It exists as evidence and disposable machinery
while the real field renderer is built, not to define the architecture.

Rules for that scaffold:

- Fix correctness bugs when they block evaluation.
- Do not add new cloud features there.
- Do not tune around structural failures.
- Do not re-enable it as scene mass.
- Replace it with the field registry, broad phase, and explicit volume pass.
- Treat every artifact as evidence for the final machine, not as an invitation
  to add another little compromise. The compromise pile is how renderers become
  haunted furniture.
