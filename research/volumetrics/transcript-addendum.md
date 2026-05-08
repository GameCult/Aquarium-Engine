# Transcript Addendum

Follow-up correction completed 2026-05-08 after checking video subtitles with
`python -m yt_dlp`. The important correction: presenter notes and slides are not
transcripts. Treating them that way loses engineering context.

## Cached Transcript Sources

- Dreams / Alex Evans, "Learning From Failure", Umbra Ignite 2015:
  `sources/transcripts/dreams-umbra-ignite-2015.en.vtt`
- AMD GPUOpen / Lou Kramer, "Real-time Sparse Distance Fields for Games", GDC
  2023: `sources/transcripts/gpuopen-sparse-sdf-gdc2023.en.vtt`

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

## Video Search Notes

I also searched for public original recordings/captions for Nubis Evolved, Nubis
Cubed, and Neural Light Grid. The Advances pages state that recordings may be
posted, but public YouTube search did not surface the original Nubis or Neural
Light Grid talks in this environment. For those, the durable notes should keep
using the official Advances pages, PDFs, and slide notes, while marking the lack
of transcript as an evidence gap rather than pretending the slides are complete.
