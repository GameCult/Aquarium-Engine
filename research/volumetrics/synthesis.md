# Realtime Volumetrics Synthesis

## Why This Exists

Aquarium needs volumetrics eventually: Grid fog, Self glow, light shafts, weather
fields, transport light, particles, and diegetic UI should live in one coherent
lighting story. The literature is blunt about the failure mode: separate fog,
particles, god rays, sky, and clouds become unrelated hacks unless they share a
field representation and light transport model.

## Durable Lessons

### Recent Scan: Do Not Skip The Boring First Pass

The follow-up scan in `recent-volumetrics-and-gpu-sdf.md` checked newer Advances
decks, citation trails, and GPU SDF work. The result does not overturn the first
plan. It sharpens it.

Modern production work still treats Wronski/Hillaire froxel rendering as the
baseline. Nubis Evolved and Nubis Cubed show how far cloud rendering can go once
the medium becomes enterable: voxel cloud assets, compressed SDF acceleration,
adaptive/jittered ray-marching, and cloud-specific lighting approximations.
AMD's Brixelizer-style SDF work shows the current shape of GPU SDF storage:
cascaded sparse 8x8x8 bricks, AABB trees, jump-flood/Eikonal fill, static/dynamic
update separation, and toroidal partial cascade updates. Neural Light Grid warns
that learned field storage can help offline/precompute, but runtime neural decode
cost is a trap unless the evaluation path is tiny and inspectable.

Aquarium lesson: build the explicit low-resolution volume first. Add occupancy,
SDF skipping, sparse bricks, or learned probes only after a real bottleneck asks
for them by name.

### Wronski: Frustum Volume First

Wronski's Assassin's Creed IV solution is the cleanest first production target
for Aquarium. Build a low-resolution frustum-aligned 3D texture, inject density
and lit scattering into it, integrate transmittance and in-scattering along view
depth, then apply the result with a single lookup during scene composition.

The important part is not the exact AC4 numbers. It is the pass shape:

1. Define a volume aligned to the camera frustum.
2. Inject participating-media density/extinction.
3. Light the volume from relevant scene lights and shadows.
4. Accumulate scattering/transmittance through depth.
5. Composite solids, transparent layers, particles, and UI against the same
   volume result.

For Aquarium, this means the first serious fog pass should be an explicit render
stage, not code inside `shade()`. The field should start with Self as emitter and
one density source from the Grid/weather domain.

### Hillaire/Frostbite: Unify the Medium

Frostbite's move is to make all participating media speak the same units:
extinction, scattering, emission, albedo, phase, and transmittance. Particles,
fog volumes, clouds, and atmosphere are not separate final-image tricks; they
write into or interact with a shared volume representation.

The Frostbite deck also connects volumetrics to tiled/clustered lighting. Its
"froxel" idea is directly relevant to Aquarium's existing primitive froxel table:
align volume cells with screen/light tiles when possible, reuse culling, and keep
lighting work bounded by the cells and lights that can actually matter.

Aquarium lesson: extend the current froxel concept deliberately. Body broad
phase is not the same thing as a lighting volume, but the renderer already has a
mental slot for conservative 3D bins. Use that grammar instead of inventing a
separate fog bureaucracy.

### Bruneton: Precompute Physics, Test the Math

Bruneton's atmosphere work is the heavy end of the spectrum: transmittance,
single scattering, multiple scattering, and irradiance are precomputed into LUTs
so runtime rendering becomes lookup-driven. The 2017 implementation is more
useful than the raw 2008 paper for engineering because it documents the texture
mappings, removes Earth-only ad hoc constants, supports configurable spectra and
density profiles, and tests dimensional consistency by compiling GLSL-like code
through C++ types.

Aquarium should not start here. A full planetary sky model is premature while the
Grid field is still young. But Bruneton is the north star for any future
planetary atmosphere or large-scale sky: precompute smooth high-dimensional
transport, make texture-coordinate mappings explicit and planet-parameterized,
and test physical units instead of "looks close" math.

### Unreal: Reprojection Helps, But Trails Are Real

Unreal's volumetric fog docs preserve the production compromise: low-resolution
frustum volume textures are made acceptable with heavy temporal reprojection and
sub-voxel jitter. The cost is history artifacts for fast-changing lights.

Aquarium lesson: add temporal smoothing only with debug visibility and light
history policy. Self is stable enough for reprojection. Fast diegetic flashes,
cursor beams, or short-lived UI lights may need reduced volumetric contribution
or separate non-history paths.

### Bowles/Gobo: Make Samples Stable in World/Volume Space

The Studio Gobo volume sampling talk attacks the same problem from the sample
distribution side. Undersampling in depth aliases under camera motion. Holding
samples stationary in the volume and using a `1/z`-style distribution keeps more
quality near the viewer while preserving draw distance.

Aquarium lesson: when the Grid field starts marching, avoid camera-locked random
noise that swims. Either anchor sample positions in world/Grid/volume space or
make the temporal reprojection own the instability explicitly.

### Horizon Clouds: Author Shape Separately From Lighting

Horizon's cloud work separates weather/shape authoring from lighting. Cloud
density is built from layered procedural fields and weather maps, then lit with
cheap approximations to Beer transmittance, anisotropic phase behavior, and
multiple-scattering feel.

Aquarium lesson: weather texture and visual density are not lighting. Keep
authoring fields, simulation fields, and lighting fields separate until they
deserve to merge. The current Grid weather color should not become the fog
density API by accident.

### GigaVoxels: Stream What Rays Ask For

GigaVoxels is less about fog and more about large sparse volumetric worlds. Its
central idea is ray-guided streaming: render traversal discovers missing data,
requests production/loading, and uses temporal coherence plus brick/mipmap
structures to stay interactive with huge virtual resolution.

Aquarium lesson: if the world becomes brushy, cloudy, or memory-rich enough to
need sparse volume storage, do not build a giant monolithic 3D texture. Use
bricks, mip/filter levels, and demand-driven production. This also supports the
Epiphany doctrine: persistent state should store meaningful field chunks, not one
hero JSON blob pretending to be a database.

### Dreams: Failures Are Evidence, Not Souvenirs

Alex Evans's Dreams talk is relevant because Aquarium is also tempted by
unconventional rendering. Dreams tried multiple renderers before settling on a
pipeline motivated by both aesthetic and technical constraints: CSG/SDF scene
description, compute-heavy evaluation, multi-resolution point clouds, splats,
and a painterly target that made some "incorrect" choices useful.

The preserved lesson is process discipline. Explore, measure, and throw away
systems that do not serve the final image. Aquarium should carry forward
explicit maps of each rendering experiment: input, output, artifact, cost, and
why it stayed or died. Dead branches belong in evidence notes, not live renderer
architecture.

## Suggested Aquarium Volumetric Roadmap

1. Add renderer debug modes before fog work: Grid hit, depth, terrain normal,
   body bins, light contribution, and line/fog masks.
2. Add an explicit low-resolution volume pass:
   - frustum-aligned 3D texture
   - logarithmic or perspective depth slicing
   - channels for extinction and in-scattered radiance
   - one Self light source
   - one Grid/weather density function
3. Integrate front-to-back transmittance and in-scattering into an accumulated
   volume texture.
4. Composite terrain, bodies, particles, and overlay/diegetic surfaces against
   the volume consistently.
5. Add temporal reprojection and jitter only after static debug views prove the
   transport terms are sane.
6. Add local density injectors: body wakes, object auras, weather bands, and
   diegetic UI light, all writing physical-ish volume parameters.
7. Consider Bruneton-style LUTs only when Aquarium needs a sky/atmosphere model
   with view-from-ground-to-space behavior.
8. Consider GigaVoxels-style sparse bricks only if volume data grows beyond one
   bounded field texture.
9. Consider Nubis/AMD-style compressed SDF acceleration only after volume
   traversal cost is measurable and the target field has coherent empty space.

## Non-Goals For The First Pass

- Do not implement full multiple scattering immediately.
- Do not hide bad lighting with bloom or blue fog.
- Do not make particles, Grid weather, and fog use separate light models.
- Do not use a giant persistent monolithic volume cache.
- Do not ship temporal reprojection without history rejection/debug controls.

## Aquarium Invariants To Add

- Volumetric lighting is a renderer-owned field, not a material afterthought.
- Density, extinction, scattering, emission, and transmittance should be explicit
  terms even when the first implementation is approximate.
- Self is the first volumetric light source.
- Grid/weather domains may inject density but do not own the lighting equation.
- Any shader/CPU contract for volumetric fields must be watched as a reload
  boundary, just like `AquariumFrame`.
