# Recent Volumetrics and GPU SDF Notes

Follow-up scan completed 2026-05-08. This extends the first Wronski,
Bruneton, Hillaire/Frostbite, Dreams, and GigaVoxels pass with newer citation
trails and Advances material.

## Scope

This is not a complete bibliography. It is the useful spine for Aquarium:
papers and production decks that either cite the older work directly or continue
the same line of engineering.

## Sources Added

- Hillaire 2020, "A Scalable and Production Ready Sky and Atmosphere Rendering
  Technique": cites the Bruneton atmosphere family and replaces the heavy 4D
  atmosphere lookup shape with smaller LUTs plus aerial perspective volume
  evaluation.
- Schneider 2022, "Nubis, Evolved": continues Horizon/Nubis after the 2015 and
  2017 cloud talks, pushing clouds from distant skybox effect toward flyable
  environments and VFX.
- Schneider 2023, "Nubis Cubed": replaces the widely copied 2.5D cloudscape
  approach with voxel cloud assets, compressed SDF acceleration, adaptive/jittered
  ray-marching, voxel-based lighting, and production cloud packs.
- Kramer/AMD 2023, "Real-time Sparse Distance Fields for Games": shows a
  practical sparse SDF generator based on cascades, 8x8x8 bricks, AABB trees,
  jump-flood/Eikonal fill, toroidal partial cascade updates, and separate
  static/dynamic geometry update paths.
- Iwanicki et al. 2024, "The Neural Light Grid": not volumetric fog directly,
  but very relevant for field storage: learned weighting functions for probe
  influence, hierarchical probe bricks, and a sober account of neural decode
  cost failing the real-time budget before a production-ready compromise.
- Monzon et al. 2024, "Real-Time Underwater Spectral Rendering": demonstrates a
  narrower medium model where real measured attenuation data and analytic
  approximations beat brute-force simulation for a specific participating medium.
- Winter/Stadlbauer/Tatzgern/Mueller et al. 2025, "Adaptive Multi-view Radiance
  Caching for Heterogeneous Participating Media": cites modern froxel practice
  through Wronski/Hillaire and moves radiance storage toward shared hierarchical
  SH probes for heterogeneous/anisotropic media.

## What Changed Since The Older Decks

### Froxel Fog Is Still The First Engine Step

Wronski 2014 and Hillaire 2015 still define the practical game-engine baseline:
frustum-aligned cells, light each cell, integrate transmittance along view depth,
and hide low resolution with careful temporal behavior. Newer papers still use
that baseline as the thing to compare against. That is useful evidence, not
nostalgia.

For Aquarium, this keeps the first volumetric pass boring on purpose:

1. Allocate a low-resolution view volume.
2. Inject Grid/Self density and extinction.
3. Light it with Self first.
4. Integrate front-to-back.
5. Composite scene surfaces and future particles through the same result.

Do not jump directly to voxel clouds, sparse bricks, or neural probes while the
basic lighting contract is still unproven. That is how a renderer gets expensive
and stupid at the same time.

### Atmosphere Moved From Huge LUTs To Smaller Runtime-Friendly Tables

Bruneton's original atmosphere work remains the rigorous reference, but Hillaire
2020 is the better production template. The key shift is reducing reliance on
large high-dimensional LUTs and using smaller tables for transmittance, sky view,
multi-scattering approximation, and aerial perspective. The technique still keeps
physical terms explicit, but accepts that low-frequency atmospheric signals can
be evaluated at reduced resolution and reused.

Aquarium implication: when sky/planet atmosphere becomes real, do not bolt a
full Bruneton clone onto Grid fog. Add a separate atmosphere module with explicit
LUT ownership, tested texture-coordinate mappings, and a bridge into the same
composition path used by the volumetric field.

### Nubis Evolved Away From 2.5D Clouds

The 2015/2017 Nubis-style cloudscape became widely copied because it is cheap:
weather fields, procedural density, ray-march, light approximations. Nubis
Evolved and Nubis Cubed are the correction from the people who had to ship the
next version. Once the player can enter the cloud, the distant-layer illusion
breaks. Guerrilla moved to authored voxel cloud assets and then accelerated
their ray-march with compressed SDFs.

The practical pieces worth stealing later:

- Treat cloud shape as authored/simulated volumetric assets, not only procedural
  weather math.
- Store coarse occupancy/distance separately from density so traversal can skip.
- Compress distance/occupancy aggressively; memory bandwidth is the villain.
- Use adaptive and jittered samples, but keep them subordinate to stable volume
  coordinates.
- Keep lighting approximations cloud-specific: dark edges, inner glow, ambient
  scattering, and long-distance shadows are authoring/lighting tools, not generic
  fog defaults.

Aquarium implication: Grid fog should not become a cloud renderer by accident.
If field storms or volumetric beings appear later, give them their own volume
asset path and let the shared volumetric compositor consume them.

### GPU SDF Has Become A Sparse Brick Problem

Epic's 2015 SDF occlusion work is still valuable for tracing: local object SDFs
near the surface, global SDF farther away, cone visibility from closest approach,
and the warning that SDFs represent surfaces/visibility better than full
volumetric radiance.

AMD's 2023 Brixelizer-style deck is the more current storage/update lesson:

- Divide world SDF into cascades around the camera.
- Store only occupied regions as small bricks; 8x8x8 is the production example.
- Build AABB trees over bricks for traversal.
- Fill empty brixels with Eikonal/jump-flood style propagation.
- Expand brick bounds enough for continuous borders.
- Keep static and dynamic geometry in separate update paths, then merge before
  fill.
- Move cascade centers virtually and address data toroidally so camera movement
  updates slices instead of copying whole volumes.

Aquarium implication: if we need GPU SDF for bodies, field occlusion, GI, or
volume traversal, the first serious design should be a sparse brick/cascade
structure. Do not persist one giant 3D texture or one monolithic CPU-side blob.

### Learned Fields Are Useful, But Decode Cost Is The Trap

Neural Light Grid is a useful warning because it documents failure as well as
the final system. Per-voxel neural decoders were too expensive and too hard to
make stable. The production compromise is still field/probe based: compact
learned weighting functions, hierarchical probe bricks, explicit influence
regions, and cheap runtime evaluation.

Aquarium implication: model-assisted preprocessing can eventually help author
or compress fields, but runtime lighting needs simple lookups and explicit
debuggability. A tiny neural toy that cannot explain a bad pixel is not yet a
renderer feature.

### Transcripts Are Evidence, Not Decoration

The follow-up `transcript-addendum.md` corrected an earlier mistake: slides and
presenter notes are not interchangeable with transcripts. The Dreams Umbra
transcript materially changes the engineering lesson by explaining why the SDF,
brick, hybrid transparency, froxel-lighting, and splat paths lived or died.
GPUOpen's Brixelizer transcript also clarifies update/integration constraints
that are easy to underweight when skimming slides.

Aquarium implication: when a deck has a talk recording, preserve subtitles or
transcripts before distilling. If no recording is found, mark that as an evidence
gap.

### Recent Participating-Media Caching Points Toward Shared Radiance Fields

The 2025 adaptive multi-view radiance-caching paper is useful because it treats
modern froxel rendering as the baseline and tries to share radiance work across
views using hierarchical SH probes. It also names the same hard parts we will
hit: anisotropic media, self-shadowing cost, temporal lag, dynamic lighting, and
ray-march bottlenecks.

Aquarium implication: once the first froxel/Grid volume works, the next upgrade
is not "more samples everywhere." It is shared field storage: radiance probes or
bricks that can feed multiple views/passes and preserve temporal stability
without per-camera artifacts.

### Narrow Medium Models Can Beat Generality

The underwater spectral rendering work is not Aquarium's immediate use case, but
the lesson is sharp: for a medium with known physical structure, measured data
and a domain-specific analytic approximation can produce real-time physical-ish
results without a heroic general solver.

Aquarium implication: if the Grid has a signature medium, define its physics and
art controls directly. Aquarium fog does not have to pretend to be planetary
air, smoke, underwater haze, and clouds all at once. One good medium beats five
generic knobs arranged like a confession.

## Updated Aquarium Roadmap

1. Keep the first pass Wronski/Hillaire-style: view volume, explicit physical-ish
   terms, Self light, front-to-back integration, debug modes.
2. Make world/Grid-space anchoring non-negotiable. Temporal reuse cannot swim
   when the camera pans.
3. Add a field debug view before adding temporal accumulation: density,
   extinction, lit scattering, transmittance, and per-slice contribution.
4. If skipping becomes necessary, add a coarse occupancy or distance field to the
   Grid volume before adopting sparse bricks.
5. If full SDF storage becomes necessary, design it as cascaded sparse bricks
   with separate static/dynamic update paths and toroidal movement.
6. Keep sky/planet atmosphere separate from local Grid fog, but route both
   through the same final composition contract.
7. Treat neural/learned field work as offline authoring/compression until a
   runtime path is cheap, inspectable, and deterministic enough for hot reload.

## Rejected For Now

- Full voxel-cloud asset pipeline. Useful later, wrong first move.
- Real-time sparse SDF generator in the renderer core before we have a use case
  stronger than "seems cool."
- Neural lighting decode in-frame.
- Multi-view radiance cache before the single-view volumetric contract exists.
- Spectral renderer for Grid fog. Use the underwater paper as a reminder to pick
  the medium, not as a demand to drag wavelengths into pass one.
