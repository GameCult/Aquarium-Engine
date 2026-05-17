# Stochastic Transparent Surface Pipeline

Aquarium needs a shared path for transparent stochastic surfaces: Grid lines,
billboard particles, glyph motes, glints, future sprite cards, and any other
thing that is visible as coverage rather than as one opaque surface hit.

The important rule is that these objects do not own canonical depth. A ray may
encounter several candidates, and blue-noise/stochastic coverage may choose
different candidates across frames. Treating one event as "the" surface creates
self-occlusion, false depth rejection, and temporal history murder.

## Classes

### Opaque Solids

Examples: Epiphany agent bodies, cursor bodies, and future solid SDF objects.

Output:

- color
- travel/depth
- field id
- normal
- material id
- reactive/velocity if needed

This is the ordinary one-ray-one-hit contract.

### Stochastic Transparent Events

Examples: Grid line support, particles, billboard cards.

Output per event:

- travel
- field or batch id
- support/coverage
- premultiplied color
- approximate normal or facing axis when useful
- thickness/depth band when useful

The event stream is sorted front-to-back and clipped by opaque solid travel.
Events do not clip each other as opaque surfaces. They composite.

## Frame Shape

1. Render opaque solid scene and depth/travel.
2. Render/integrate participating media against opaque solid travel.
3. Generate stochastic transparent events up to opaque solid travel.
4. Apply blue-noise coverage decisions or alpha-to-coverage equivalent.
5. Composite selected transparent events premultiplied front-to-back.
6. Write transparent temporal descriptors separate from opaque metadata.

The Grid should not be a special scene surface. It should emit events from its
heightfield/line-support intersection. Particles emit events from their sorted
billboards or bins. Both land in the same transparent event pipe.

## Temporal Contract

Transparent history cannot validate like opaque history.

Opaque validation compares one depth, normal, field id, and color neighborhood.
Transparent validation should compare a distribution summary:

- accumulated support/coverage
- nearest event travel
- weighted mean travel
- travel variance or thickness band
- dominant transparent class id
- optional batch/field id where stable

History should survive ordinary stochastic hit/miss changes inside the same
support distribution. It should reject when the distribution moves, disappears,
becomes occluded by a solid, or changes class.

## Grid Events

The Grid is an implicit heightfield plus analytic line support, not an opaque
terrain surface.

A robust Grid event generator should:

- use the existing slope-aware `terrainGap = p.z - terrainHeight(p.xy)` march
- find refined heightfield crossings up to opaque solid travel
- evaluate line/isoline/field-line support at each crossing
- emit only crossings with meaningful support
- composite events front-to-back in the transparent pipe
- keep opaque scene metadata untouched

It must not:

- write Grid as the scene's opaque travel
- let one Grid crossing occlude later Grid crossings
- fixed-step sample a thin band and hope not to miss it
- invent a Grid-only temporal path that particles cannot use

## Particle Events

Particles should use the same pipe.

The particle system supplies candidate events from sorted/binned billboards.
Each event carries travel, coverage, premultiplied color, and a stable batch or
particle id when available. Stochastic coverage chooses visibility. Temporal
validation compares the transparent distribution rather than demanding exact
per-particle depth stability.

## Debug Views

Required before relying on the pipe:

- transparent event count
- selected stochastic event coverage
- accumulated transparent alpha
- nearest transparent travel
- weighted transparent travel
- transparent travel variance/thickness
- transparent class id
- opaque terminator travel

If these views do not agree with the final composite, the pipe is lying.
