# Cube-Sphere Fractal Brush Research

Aquarium needs one scalable surface/object grammar, not separate tricks for
planets, Grid brushes, and agent bodies.

The working target is a cube-sphere domain: six square faces, each subdivided by
quadtree addresses, projected onto a sphere with bounded distortion. Each tile
owns a local 2D chart for terrain and surface detail. The same grammar extends
inward to 3D object detail through bounded implicit brush nodes.

This is not a license to build a universal registry-shaped fog machine. The
authority should be small:

- Tile address owns where a surface patch lives.
- Projection owns how a face chart maps to the sphere.
- Brush grammar owns what detail exists at a scale.
- Brush envelope owns where that detail can affect rendering.
- Renderer LOD/sampling owns how much of the grammar is evaluated this frame.

## Surface Domain

The established name is the quadrilateralized spherical cube, often shortened to
QSC or quad sphere. It was designed as a discrete global grid for spherical
data, including Earth/sky data, and avoids the polar singularities of lat-long.
PROJ describes QSC as equal-area, treating all cube sides equally, and explicitly
mentions planetary-scale terrain rendering with quadtree data per cube side.

The older COBE/Chan-O'Neill family is approximately equal-area and historically
important. For engine use, the more relevant graphics framing is cube-to-sphere
projection for procedural texturing: generate a point or tile on a cube face,
warp the face coordinates to reduce area distortion, then normalize/project to
the sphere.

The cheap mapping ladder:

1. Identity cube map then normalize: fastest, worst area stretch near cube
   corners.
2. Tangent/univariate warp: still simple, analytic inverse, much better area
   distribution.
3. Fifth-order odd polynomial warp: fastest high-quality univariate candidate
   in the JCGT comparison; good for GPU hot paths, inverse is approximate.
4. COBE-style bivariate polynomial: best area preservation in that comparison,
   but more expensive.
5. Exact equal-area constructions: useful reference, not automatically the right
   hot shader choice.

For Aquarium, the first serious candidate should be the fifth-order odd
polynomial warp from the JCGT paper, measured against tangent and COBE on the
actual 1070 target. It is the right shape of compromise: cheap scalar math,
separable face axes, low area error, and no lat-long singularity. If inverse
address lookup dominates, tangent may win despite worse area preservation. If
offline baking or CPU authoring needs maximum area regularity, COBE/QSC can be
used there without forcing it into every pixel path.

## Multiscale Detail

Every visible primitive should be able to answer: what detail exists below this
scale? The proposed answer is an iterated function system brush grammar.

For terrain:

- Each cube-face quadtree tile has a deterministic grammar seed.
- The grammar emits bounded 2D brushes at octave/LOD levels.
- Coarse levels define form: continents, basins, ridges, crater families,
  river-like masks, weathered bands.
- Fine levels define material/texture response first; traced geometry only when
  its bound is visible enough to deserve SDF cost.

For 3D bodies:

- Each agent/body starts from one coherent base SDF form.
- Its grammar emits bounded 3D brush nodes in that local form domain.
- A brush may carry texture/material response, displacement, negative space, or
  form contribution, but it must share an explicit envelope.
- The grammar is LOD-gated and budgeted by proxy bounds. No scene-global
  mega-SDF. No evaluating all organs because one pixel got curious.

IFS here means a deterministic recursive emitter of child transforms and brush
parameters, not necessarily a textbook fractal exposed directly to the user. The
important invariant is that detail exists at every scale because the primitive
can generate it locally, not because a finite table happened to be hand-filled.

## IFS DSL Shape

VibeGeometry is the nearest local evidence for the authoring layer Aquarium
needs. It is not about SDFs, but it has spent real work on spatial mapping,
procedural graph translation, CSG claims, and keeping geometry machines
inspectable.

The lesson is not "copy Blender nodes." The lesson is that a spatial DSL needs
more than primitives and transforms:

- explicit local frames and frame transitions;
- deterministic seeded variation;
- named fields/attributes that survive domain changes;
- tags explaining why a generated thing exists;
- repeat/foreach-style rule composition;
- authoring handles/gizmos for parameters humans and agents will tune;
- asset/material binding outside the hot geometry grammar;
- debug/evidence outputs for each layer, not only the final pretty object.

Aquarium's IFS DSL should emit semantic brush claims before it emits shader
work. A claim is a bounded statement in a local domain:

```text
claim {
  name
  kind: material | height | displacement | sdf_form | void | mask | light
  frame
  envelope
  payload
  tags
  lod_range
  cost_tier
  seed
  children
}
```

For terrain, the claim domain is a cube-face tile chart plus height/material
space. For bodies, the claim domain is the object-local anatomical/form frame.
For both, rule output stays semantic until a backend lowers it into one of the
live render paths: height brush pass, material field, SDF evaluator, impostor
splat, debug view, or offline bake.

Do not flatten this into a bag of anonymous brushes too early. VibeGeometry's
`vg_grammar` layer keeps `Solid` and `Void` claims tagged as room, corridor,
door, wall, floor, or ceiling before compiling to CSG. Aquarium needs the same
shape of truth for craters, ridges, rivers, veins, ribs, petals, apertures,
scars, and agent state features. The renderer can be brutal and cheap later;
the authoring layer should still know what each brush means.

## CSG Tree Lesson

The really useful VibeGeometry path is the CSG tree attempt. It separates three
authorities:

- grammar/rules decide which semantic claims should exist;
- the CSG tree decides boolean ownership of space;
- the output mesh or brush stream is a compiled verdict, not the source of
  truth.

`vg_csg` currently has a small `CsgTreeArena` with brush leaves and branch ops:
addition, subtraction, and common/intersection. Branch bounds follow boolean
meaning: addition unions child bounds, subtraction keeps the left/source bound,
and common intersects child bounds. The tree can compile down to the current
ordered brush stream, where the first child of a subtraction remains additive
and later children become subtractive operators.

That flattening bridge matters. Aquarium can preserve an authored IFS/CSG tree
even if the first GPU path lowers it to fixed-size brush tables or per-object
SDF includes. The tree is the durable design. The shader packet is a backend.

For Aquarium, the tree should probably not be a full CAD boolean kernel in the
hot path. It should be a semantic spatial program that lowers into cheap
bounded evaluators:

```text
rule tree
-> semantic brush/void/material claims
-> boolean/IFS ownership tree
-> backend lowering:
   - 2D tile material/height brushes
   - 3D object-local SDF form brushes
   - compact anisotropic splat/impostor batches
   - offline baked summaries and mips
```

The important tree operations for the first Aquarium DSL:

- `Union`: multiple claims contribute in the same domain.
- `Subtract`: voids, apertures, channels, scars, doorways, river cuts.
- `Intersect/Common`: masks, biome regions, anatomical zones, tile clipping.
- `Repeat`: emit child rules over a count, curve, grid, face, or stochastic set.
- `Refine`: emit lower-scale children when screen/error budget asks for them.
- `Capture`: name a value, station, mask, or frame for later child rules.
- `Bind`: attach material/asset/state payload outside the form rule.

The tree must carry bounds at every node. Bounds are not an acceleration garnish;
they are the contract that keeps a grammar from becoming a frame-eating sermon.
Branch bounds also define LOD and invalidation: if a tile/body query does not
touch a subtree's bound, that subtree does not get to spend shader time.

## VibeGeometry Distillation

Local source paths worth treating as evidence:

- `E:\Projects\VibeGeometry\docs\research\procedural-doctrine.md`
- `E:\Projects\VibeGeometry\docs\research\translations\ircss-french-houses-node-atlas.md`
- `E:\Projects\VibeGeometry\docs\research\translations\ircss-french-houses-node-mechanisms.md`
- `E:\Projects\VibeGeometry\docs\research\dream-machine-grammar.md`
- `E:\Projects\VibeGeometry\docs\research\realtime-csg-doctrine.md`
- `E:\Projects\VibeGeometry\crates\vg_grammar\src`
- `E:\Projects\VibeGeometry\crates\vg_csg\src\tree.rs`
- `E:\Projects\VibeGeometry\crates\vg_csg\src\frontier.rs`

Distilled rules:

- Build the host form first; ornament grows on captured structure.
- Attributes/fields are not bookkeeping. They are how identity survives
  instancing, deletion, scattering, and later detail passes.
- Groups can pass batons: running values such as angle, offset, frame, count,
  seed, aperture, or path state should be explicit outputs.
- Scatter has layers: nursery surface, point field, instance body, realization
  boundary. Aquarium's brush grammar should mirror this with tile/body domain,
  spawn field, brush claim, and backend lowering.
- Paths are topology first. Draw cracks, veins, river paths, routes, or rails
  after solving connectivity or stations.
- Boolean thickets often want tables. Repeated near-clone rules should become
  one rule plus rows of parameters, not copied child subtrees.
- CSG is an adjudicator, not the authoring language. Domain rules should be
  written in terrain, anatomy, tile, curve, or habitat coordinates, then lowered
  into boolean/brush evaluation late.
- Demand frontiers beat world rebuilds. The query already says which tile,
  proxy, dirty brush, screen cone, or body bound matters; use that before
  building generic indexes.

## Brush Envelope

The current cheap circular/Gaussian brush idea is too blunt. The next primitive
should look more like a shaped Gaussian splat: an anisotropic bounded kernel
with rotation, scale, falloff, and optional local shape controls.

Relevant splatting lineage:

- EWA/surface/volume splatting uses elliptical Gaussian kernels and screen-space
  filtering to avoid aliasing and excess blur.
- 3D Gaussian Splatting models scenes with explicit anisotropic 3D Gaussians,
  each carrying position, opacity, scale, rotation/covariance, and appearance,
  then bins/sorts/rasterizes them efficiently.

Aquarium should borrow the cheap structural idea, not the whole radiance-field
machine. A brush envelope can be stored as:

```text
center
orientation basis or compressed rotation
radii / inverse covariance diagonal
falloff exponent or compact support radius
shape parameters for taper, skew, ridge, hollow, or signed lobe response
payload type: material, height/displacement, SDF form, mask, light, debug
```

Hot shader evaluation should prefer compact-support anisotropic kernels over
infinite Gaussians:

```text
q = transpose(rotation) * (p - center)
m = dot(q * invRadii, q * invRadii)
support = saturate(1 - m)
weight = support * support * (3 - 2 * support)
```

That is not a true Gaussian. Good. Infinite support is expensive folklore unless
the renderer can cull it. The useful inheritance from Gaussian splatting is
anisotropic covariance-like shape control, tile/proxy binning, and LOD-aware
visibility, not religious devotion to exp().

## Sampling Strategy

SDF performance hinges on conservative bounds and choosing the right evaluator
late. Hart's sphere tracing framing is still the base law: steps are safe when
the distance estimate respects a derivative/Lipschitz bound. Aquarium already
learned the local version of this for thin agent SDF organs.

Migration target:

- Keep proxy bounds first. A tile or body proxy decides whether any expensive
  grammar can matter.
- March conservative macro distance before micro detail.
- Evaluate grammar detail only after the ray enters a tile/body budget zone.
- Carry LOD tier, cost tier, step count, proxy-hit coverage, and grammar node
  count debug views before trusting the system.
- Use cone/pixel-footprint-aware sampling for material/height detail; do not
  trace subpixel grammar detail as geometry.
- Use prefiltered/mip summaries per tile and per grammar family where possible.
- Use stochastic/blue-noise selection for dense fine detail when exact full
  evaluation would alias or exceed budget.

The hard rule: form SDF detail must have a conservative envelope and a measured
cost. Texture/detail brushes can be abundant; traced form brushes must earn
their steps.

## LOD And Contribution Caching

The LOD problem is not "pick a depth." The real question is: would this
subtree change currently visible pixels enough to justify its cost?

The model should combine three proven ideas:

- Geometry clipmaps: keep nested, incrementally updated caches around the
  viewer for terrain-like domains, and fade between levels in transition
  regions.
- Nanite-style hierarchy cuts: select exactly one useful representation along
  each ancestor/descendant chain using projected error. Parent summaries let
  traversal stop without asking every leaf for permission.
- Gaussian splat/octree LOD: score an anisotropic primitive by projecting its
  covariance/envelope footprint into screen space, then choose coarser or finer
  anchors based on pixel impact and budget.

For Aquarium's IFS brush tree, every node should cache a compact summary:

```text
nodeId
parentId / firstChild / childCount
domain: cubeFaceTile | bodyLocal | curveLocal | volumeLocal
localFrame
worldOrTileBound
envelopeBound
maxDisplacementOrDistanceError
maxMaterialDelta
normalConeOrGradientBound
opacityOrCoverageBound
frequencyBand / octave
estimatedCost
lastContributionScore
lastVisibleFrame
residentState
summaryPayloadHandle
childPayloadHandle
```

The projected contribution score can start simple:

```text
projectedRadiusPx = project_bound_radius(node.bound, camera)
projectedErrorPx = project_world_error(node.maxError, node.bound, camera)
materialScore = node.maxMaterialDelta * projectedCoveragePx
formScore = projectedErrorPx * visibleCoverageEstimate
score = max(formScore, materialScore) * importanceBias / estimatedCost
```

This is only a heuristic, but it protects the right invariant: children fade in
when their worst possible visible contribution rises above a pixel/error
threshold, and fade out when the parent summary is good enough. For anisotropic
brushes, use the projected ellipse/covariance footprint rather than a sphere
whenever that data is already cheap; otherwise fall back to a conservative
sphere/AABB.

The runtime cut:

```text
parentGoodEnough = parent.projectedErrorPx <= threshold
nodeNeeded = node.projectedErrorPx > threshold
renderNodeSummary = parentNeeded && !nodeNeeded
descend = nodeNeeded && budgetAllows && childrenResidentOrRequestable
```

For smooth transitions, each node gets a fade weight based on hysteresis around
the threshold:

```text
fade = smoothstep(thresholdLow, thresholdHigh, projectedErrorPx)
```

The parent summary fades out as children fade in. Geometry clipmap transition
regions are the terrain precedent; for object/body brushes this becomes
cross-fading material contribution or displacing geometry only after the
micro-form is stable enough not to pop. Never fade signed distance itself in a
way that breaks conservative marching. Fade payload amplitude, material
response, or selected child contribution after the macro hit is established.

### Dynamic Cache

The cache should be a demand cache, not a tree-shaped shrine in memory.

Inputs:

- camera and previous camera;
- visible cube-face tile windows or body proxy hits;
- node summaries resident on CPU/GPU;
- per-node contribution score from the previous frame;
- request budget for new child payloads;
- eviction budget for stale low-score payloads.
- update budget for how many node weights may be refreshed this frame.

Per frame:

1. Traverse only visible roots: active cube faces/tiles and body proxies.
2. Project each node summary and compute contribution score.
3. If the summary is enough, draw/evaluate it and stop.
4. If children are needed and resident, descend.
5. If children are needed and missing, render the parent summary, enqueue child
   payloads, and keep a fade-in reservation.
6. Update a small score cache with temporal smoothing:

```text
score = lerp(oldScore, currentScore, currentVisible ? 0.35 : 0.08)
```

7. Evict children whose smoothed score stays below threshold for enough frames,
   starting with high-cost low-score nodes.

### Probabilistic Weight Updates

The contribution cache cannot refresh every branch every frame. A large IFS tree
will always have more latent branches than budget. The cache therefore needs an
online estimator: every frame it samples a subset of nodes, updates their
weights, and lets the whole system converge over time.

The first version can be a stochastic scheduler, not a neural net:

```text
priority =
  visibilityProbability
  * staleWeight
  * uncertainty
  * max(previousScore, parentScoreBias)
  / max(estimatedUpdateCost, epsilon)
```

Nodes enter the update lottery from visible roots, near-threshold cut nodes,
recently visible descendants, and a small exploration set. The scheduler spends
the frame's update budget on weighted random samples, not only on the current
top scores. Greedy-only updates are how stale branches stay confidently wrong.

Each node should carry estimator state:

```text
meanContribution
varianceOrUncertainty
sampleCount
lastSampleFrame
lastVisibleFrame
decayRate
confidence
```

On update:

```text
delta = observedContribution - meanContribution
meanContribution += alpha * delta
variance = lerp(variance, delta * delta, beta)
confidence = saturate(confidence + confidenceGain)
```

On non-update:

```text
confidence *= decay
uncertainty += staleGrowth
```

This is machine-learning territory in the plain sense: an online model predicts
which branches matter, samples under budget, updates from observations, and
should converge toward stable weights for stable views. Start with exponential
moving averages, variance, and bandit-style exploration. Do not jump straight to
a bespoke neural predictor unless the simpler estimator cannot hit fidelity or
budget.

The convergence contract:

- Every resident visible or near-threshold node must have nonzero probability of
  being resampled.
- High-uncertainty nodes get exploration budget even when their old score is
  low.
- Scores decay when nodes go unseen, but summaries remain available.
- Parent and child scores must be monotonic enough for stable cuts: a parent
  summary cannot claim less possible contribution than all of the children it
  hides.
- Debug views must expose mean, variance/uncertainty, sample age, confidence,
  and update probability.

The higher-fidelity path is learned scoring:

```text
features:
  projected bound, projected anisotropic footprint, depth, view angle,
  normal/gradient cone, material delta, motion, parent score, tile/body state,
  historical score/variance, previous visibility, estimated cost

prediction:
  expectedContribution
  uncertainty
  updatePriority
```

Training signal can come from occasional high-quality probes: render/evaluate a
more detailed subtree, compare against the parent summary, and record the
observed pixel/material/SDF delta. That makes the cache a budgeted fidelity
estimator rather than a pile of thresholds pretending to be wise.

Cut line: the runtime still needs deterministic safety. A learned predictor may
rank update and residency priority; it must not be the only guard for SDF
marching bounds or object/tile visibility. Conservative summaries remain law.

The contribution cache should be keyed by stable domain address, not by transient
allocation:

```text
cube face + quadtree path + grammar rule path
body id + local grammar rule path
```

For a GTX 1070-class target, keep the first implementation embarrassingly
simple:

- CPU scores coarse roots and streaming decisions.
- GPU evaluates fixed-size resident node tables per visible tile/body.
- Node summaries are small structured-buffer rows.
- A stochastic CPU/GPU scheduler refreshes only a bounded subset of node weights
  each frame, using uncertainty and stale age so weights converge instead of
  freezing.
- Child payload pages are append-only within a frame and compacted only at safe
  sync points.
- Debug views show projected error, contribution score, uncertainty, sample age,
  confidence, update probability, selected depth, cache residency, fade weight,
  and estimated cost.

The cache must preserve summaries even when child payloads are evicted. A parent
summary is the fallback representation and the promise that the tree can always
render something coherent. If a subtree cannot summarize its children with a
bounded error/material delta, it is not ready to be an LOD subtree.

## Migration Shape

1. Add the cube-sphere domain math as generic renderer math, not Zyphos policy.
2. Convert Zyphos planet surface addressing from lat-long/spherical noise toward
   six face quadtrees.
3. Replace height brushes with shaped anisotropic envelopes while preserving the
   existing scalar height target until a better pass contract exists.
4. Define a minimal semantic brush-claim format: name, kind, frame, envelope,
   payload, tags, LOD range, cost tier, seed, and children.
5. Preserve an authored IFS/CSG ownership tree even when the first backend
   lowers it into flattened brush tables or SDF shader code.
6. Make one terrain grammar and one agent/body grammar share the same envelope
   evaluator.
7. Add node summaries and contribution-score caching before allowing recursive
   detail to expand at runtime.
8. Add debug visualizations before expanding detail density.

If the first implementation needs a cut, cut 3D recursive form detail first.
Keep the cube-sphere address space and shaped 2D brush envelope. Those are the
foundation stones.

## Sources

- PROJ QSC documentation: https://proj.org/en/stable/operations/projections/qsc.html
- NASA NTRS, "A quadrilateralized spherical cube Earth data base": https://ntrs.nasa.gov/citations/19810002572
- Clarberg, "Cube-to-sphere Projections for Procedural Texturing and Beyond,"
  JCGT 2018: https://jcgt.org/published/0007/02/01/paper.pdf
- Kerbl et al., "3D Gaussian Splatting for Real-Time Radiance Field Rendering,"
  SIGGRAPH/TOG 2023: https://arxiv.org/abs/2308.04079
- Hart, "Sphere tracing: A geometric method for the antialiased ray tracing of
  implicit surfaces," The Visual Computer 1996:
  https://experts.illinois.edu/en/publications/sphere-tracing-a-geometric-method-for-the-antialiased-ray-tracing/
- Zwicker et al., EWA surface/volume splatting lineage:
  https://vcg.seas.harvard.edu/publications/20011001-ewa-volume-splatting
- Local VibeGeometry procedural and CSG research:
  `E:\Projects\VibeGeometry\docs\research\procedural-doctrine.md`,
  `E:\Projects\VibeGeometry\docs\research\dream-machine-grammar.md`,
  `E:\Projects\VibeGeometry\docs\research\realtime-csg-doctrine.md`,
  `E:\Projects\VibeGeometry\crates\vg_grammar`, and
  `E:\Projects\VibeGeometry\crates\vg_csg`
- Losasso and Hoppe / Asirvatham and Hoppe geometry clipmaps:
  https://hhoppe.com/proj/gpugcm/
- Karis et al., Nanite virtualized geometry:
  https://advances.realtimerendering.com/s2021/Karis_Nanite_SIGGRAPH_Advances_2021_final.pdf
- Ren et al., Octree-GS LOD-structured Gaussian splatting:
  https://arxiv.org/abs/2403.17898
