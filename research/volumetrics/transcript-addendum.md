# Transcript Addendum

Follow-up correction completed 2026-05-08 after checking video subtitles with
`python -m yt_dlp`. The important correction: presenter notes and slides are not
transcripts. Treating them that way loses engineering context.

## Cached Transcript Sources

- Dreams / Alex Evans, "Learning From Failure", Umbra Ignite 2015:
  `sources/transcripts/dreams-umbra-ignite-2015.en.vtt`
- AMD GPUOpen / Lou Kramer, "Real-time Sparse Distance Fields for Games", GDC
  2023: `sources/transcripts/gpuopen-sparse-sdf-gdc2023.en.vtt`
- Sea of Thieves / Valentine Kozin, "Tech Art and Shader Development", GDC
  2019: `sources/transcripts/sea-of-thieves-tech-art-gdc2019.en.vtt`

Readable working text extracts live under `extracted-text/`. Both cache folders
are gitignored.

## Dreams Transcript Correction

The Dreams slides alone undersell the talk. The spoken version is not just a
gallery of abandoned renderers; it explains why each one died and what survived.

### Actual Technical Arc

1. The project starts from SDF-based constructive sculpting: primitives such as
   splines, cones, spheres, and other platonic edit pieces are combined,
   subtracted, colored, and soft-blended.
2. The team originally expected ZBrush-like push/pull/smudge editing, but the
   SDF evaluator and desired shape grammar made those tools a poor fit. This is
   a key design lesson: editor affordances must match the representation, not
   the user's memory of another tool.
3. The first serious implementation becomes a large stack of compute shaders
   evaluating edit lists into volume data. The talk calls out how hard this was
   to debug on early PS4 hardware.
4. Dual contouring and hard-edge extraction were attractive, but edge handling,
   complexity, and the evolving art target kept pushing the team away from a
   conventional mesh result.
5. GigaVoxels-style sparse bricks were compelling because they provided
   filtering, detail where needed, and a way to think about mipmapped/cone-like
   traversal. But rendered hard-surface results could feel like untextured mesh
   models; the technique did not automatically justify itself aesthetically.
6. Hybrid brick experiments tried to blend hard cores with fuzzy edges and
   transparency. Order-independent transparency and fill/cost pressure killed
   that path.
7. The team explored froxel-like lighting: spatial cells in frustum space and
   compute dispatches in shells from lights. The spoken failure is sharp: the
   lighting machinery began to work, but the image had drifted back toward
   generic untextured engine output. The art direction was being forgotten.
8. The final useful survivor was the evaluator. It produced small blocks of
   distance-ish volume data, then spawned many tiny splats/points from them.
   Those were grouped, put in a BVH, compressed with vector quantization, and
   rendered as painterly point clouds/quads.
9. Temporal antialiasing, simple ambient occlusion, and hybrid screen-space plus
   voxel queries helped the final image hold together.

### Aquarium Lessons

- Do not preserve a representation just because it is mathematically elegant.
  Preserve the evaluator, cache, or field if that is the part that actually
  contributes to the final image.
- SDF editing, sparse bricks, froxels, and splats are not rivals in the abstract.
  They are stages in a pipeline only if each stage has a reason to exist.
- If a renderer experiment starts looking like a generic engine viewport, stop.
  The Dreams talk is explicit that technical success can still be visual failure.
- Debuggability is not optional. A pile of compute shaders without observability
  is not advanced; it is just expensive fog around your own ignorance.
- The art style can be the optimization. Dreams won because its final painterly
  representation turned approximation into signal instead of hiding it.

For Aquarium, this strengthens the existing rule: build the Wronski/Hillaire
volume pass first, but keep the output tied to Aquarium's field identity. If the
Grid fog only looks like stock engine haze, the pass has failed even if the math
compiles.

## GPUOpen Transcript Correction

The GDC 2023 Brixelizer transcript confirms and slightly sharpens the slide
distillation:

- Brixelizer starts from triangle geometry plus transforms and AABBs, then
  builds sparse local distance fields near surfaces.
- Empty space is represented by a three-level AABB tree; traversal starts there
  and only switches into brick SDF traversal at leaf bricks.
- Bricks are 8x8x8 brixels and also store surface AABBs used to build the tree.
- Cascades are typically camera-centered. Each cascade owns its AABB tree but
  shares a common 3D brick atlas.
- Updates are per-cascade and can be partial. Moving the cascade center
  virtually and addressing data toroidally avoids copying the whole volume when
  the camera moves.
- Static geometry is not rebuilt every frame. New or deleted static geometry
  invalidates possibly intersecting voxels. Dynamic geometry uses separate
  cascades that rebuild on update and merge with static data before jump-flood /
  Eikonal fill.
- There are explicit caps on references processed per frame and bricks allocated
  per bake. This is not decorative; it prevents pathological update spikes.
- The integration surface includes debug tracing modes. That matters. If
  Aquarium adopts GPU SDF storage later, debug views are part of the feature.

Aquarium lesson: sparse SDF is an update system before it is a tracing trick.
The hard part is not sampling a distance. The hard part is knowing what changed,
what can stay resident, how to cap work, and how to see when the structure lied.

## Sea of Thieves Cloud Correction

Sea of Thieves belongs in the cloud notes because it is an unusually good
counterweight to photoreal raymarched clouds. Rare wanted cloudscapes, storms,
and skull/ship-shaped cloud signals to feel like world objects, not distant
painted skybox cards. The production solution was deliberately not a full
raymarcher.

The SIGGRAPH 2018 abstract and GDC 2019 transcript describe the cloud renderer:

1. Render opaque polygonal cloud geometry with cheap forward/per-vertex lighting
   approximating subsurface scattering.
2. Write it to an off-screen buffer.
3. Downsample to quarter resolution.
4. Blur color and depth separately.
5. Composite back with a camera-facing quad.
6. Use blurred depth to reconstruct approximate world position for fogging,
   translucency, and sky blending.
7. Add distortion plus low/high-frequency noise to break hard geometry into
   fluffy cloud contours.
8. Sharpen far clouds with alpha thresholding so distant silhouettes stay
   cartoon-readable.
9. Pre-distribute cloud meshes over a several-mile square, move them by wind,
   and wrap them around the player so the sky feels continuous.
10. Synchronize the wind/offset across clients so all players see matching cloud
   placement.
11. Let level artists author radial high/low-pressure zones that push cloud
   meshes away or concentrate them.

The GDC transcript adds useful production detail. They tried billboards with
normal maps and also raymarching through 3D textures for storms. The shipped path
won because it gave strong authored geometry, cheap rendering, and readable
gameplay silhouettes. Lighting was squeezed into very few channels: sunlight and
skylight were encoded separately, which later made glowing skull eyes awkward.
That is a perfect little pipeline tax: compression decisions become feature
constraints.

The transcript also describes a Houdini prototype for per-vertex cloud lighting:
convert polygonal cloud geometry to an SDF, raymarch through that SDF in tooling,
then bake per-vertex lobe/occlusion data so runtime does not need the heavy SDF.
The result loses some thin-detail accuracy but preserves the broad brightness and
subsurface feel well enough for stylized clouds.

### Aquarium Lessons

- Not every cloud-like thing should be volumetric raymarching. If the design
  needs readable symbolic cloud shapes, geometry plus filtered compositing may
  be the better machine.
- Clouds can be diegetic signage. Sea of Thieves uses skulls, ships, storms, and
  world-event cloud silhouettes as navigational/social signals. Aquarium field
  events could use authored volumetric/geometry silhouettes the same way.
- Use tool-time SDF/raymarching to bake cheap runtime terms when exact runtime
  volume traversal is not buying enough.
- Preserve approximate world position/depth through the cloud composite. Without
  that, clouds cannot participate coherently in fog, sky blending, or scene
  depth.
- Multiplayer/shared-world clouds need a synchronized field/offset, not
  per-client random sky decoration.
- Channel budgets are design budgets. If we pack light terms too aggressively,
  later features such as colored event glows will come back with a knife.

## Video Search Notes

I also searched for public original recordings/captions for Nubis Evolved, Nubis
Cubed, and Neural Light Grid. The Advances pages state that recordings may be
posted, but public YouTube search did not surface the original Nubis or Neural
Light Grid talks in this environment. For those, the durable notes should keep
using the official Advances pages, PDFs, and slide notes, while marking the lack
of transcript as an evidence gap rather than pretending the slides are complete.
