# Fractal Brush Architecture Plan

Aquarium needs one multiscale spatial machine for planets, Grid surfaces, and
agent bodies. The target is not "infinite detail" as a slogan. The target is a
bounded semantic tree that can always answer four questions:

- What does this detail mean?
- What pixels could it affect?
- Is it worth its CPU, RAM, SSD, and GPU cost this frame?
- Which module owns the answer, and how do tests prove that boundary?

The architecture below is the coherent machine that falls out of the research
pass.

## Core Model

```text
client intent / world seed / agent visual state
-> domain roots
-> semantic IFS brush grammar
-> ownership tree
-> node summaries
-> contribution cache and residency scheduler
-> backend packets
-> renderer passes
-> debug and evidence
```

The authored tree is the source of truth. Backend packets are compiled output.
That distinction is the spine. If the grammar is flattened too early into
anonymous shader tables, we lose meaning, invalidation, LOD, test boundaries,
and future editing handles in one cheap little bonfire.

## Module Network

The architecture should be split into modules with narrow contracts and boring
mock seams. The goal is not maximum abstraction. The goal is that every module
owns one invariant that can be tested without booting the whole renderer.

```text
Aquarium.Engine.Contracts
  Fractal domain, claim, tree, summary, packet, and debug data contracts.

Aquarium.Engine.Fractal
  Pure CPU domain math, grammar expansion, ownership tree, summaries, scoring,
  stochastic estimator, residency decisions, and backend packet planning.

Aquarium.Engine.Render
  D3D12 resource residency, structured buffers, shader bindings, render graph
  passes, and debug visualization.

Aquarium.Zyphos
  Planet-specific domain roots, world seeds, terrain grammar choices, and
  setting-safe visual policy.

Aquarium.Epiphany
  Agent/body visual state, role grammar choices, and semantic state bindings.

Tests
  Contract tests, pure CPU unit tests, renderer packet golden tests, shader
  parity tests, and smoke/integration tests.
```

### Module Boundaries

| Module | Owns | Does Not Own | Mock Boundary |
| --- | --- | --- | --- |
| Contracts | Stable data shapes and versionable packets | Projection algorithms or D3D12 objects | Plain DTO fixtures |
| Fractal Domain | Cube-face addresses, projections, local frames | Client lore or GPU resources | `IProjection`, camera fixture |
| Grammar | Semantic claims and rule expansion | Rendering or residency | seeded rule fixtures |
| Ownership Tree | Boolean/IFS topology, bounds, stable keys | Shader packet layout | tree builder fixtures |
| Summary Builder | Conservative error/material summaries | Per-frame scheduling | child-node fixtures |
| Contribution Cache | score state, stochastic updates, convergence | payload loading or drawing | deterministic RNG, fake frame clock |
| Residency Scheduler | request/evict decisions under budgets | SSD implementation or descriptors | fake payload store |
| Backend Planner | selected cut to backend packets | D3D12 upload | packet golden files |
| D3D12 Backend | buffers, descriptors, shader passes | semantic grammar | packet fixtures and GPU smoke |
| Client Adapters | Zyphos/Epiphany policy | generic renderer math | fake engine contracts |

The hard rule: a module must be defended by the invariant it owns, not by how
cleverly it forwards data.

## Testing Strategy

This system only deserves to exist if it is testable at its natural seams.

### Pure Unit Tests

These run without D3D12:

- cube-face address round trips;
- projection candidate continuity and area-error sampling;
- local frame point/vector transforms;
- envelope CPU evaluator parity against known samples;
- grammar expansion from seed to deterministic claims;
- ownership tree bounds for union/subtract/common/repeat;
- summary conservativeness against generated children;
- projected score monotonicity with distance and error;
- stochastic estimator convergence with deterministic RNG;
- residency eviction/request decisions under fixed budgets.

### Contract Tests

These protect module boundaries:

- serialized contract rows round-trip without renderer types;
- stable keys remain stable across reload and equivalent grammar expansion;
- backend packet planner emits the same packet for the same selected cut;
- clients can declare domains and grammars without referencing D3D12.

### Mock Boundaries

Required mocks/fakes:

- `IFractalClock`: deterministic frame number and time.
- `IFractalRandom`: seeded random/update lottery.
- `IProjection`: projection candidate swap for tests.
- `IContributionProbe`: fake observed pixel/material/SDF delta.
- `IFractalPayloadStore`: fake RAM/SSD payload availability.
- `IFractalGpuBudget`: fake GPU table/page budget.
- `IFractalDebugSink`: captures telemetry without UI.

Mocks should sit at resource and observation boundaries, not inside algorithms.
Mocking every helper is how tests become a puppet show for implementation
details. Good tests feed the module its owned inputs and inspect its owned
outputs.

### Shader Parity Tests

For GPU math that must match CPU decisions:

- shaped envelope weight;
- cube-face projection used by shader sampling;
- packet decoding;
- selected-cut fade weights;
- conservative SDF bound constants.

The CPU implementation is the spec. The HLSL implementation is the fast copy.
When they disagree, the test should fail before the disagreement becomes a
screenshot mystery.

### Integration Tests

Small but real:

- build one cube-face terrain grammar into current height brush backend;
- render/debug one Zyphos planet frame headless;
- render one body proxy with selected-cut packet fixtures;
- verify debug modes expose score, uncertainty, selected depth, residency, and
  cost.

### Performance Fixtures

Performance tests must be deterministic:

- fixed camera paths;
- fixed world seed;
- fixed update budget;
- fixed payload budget;
- fixed RNG seed;
- output: CPU ms, GPU ms if available, resident payload count, update count,
  evictions, selected node count, score convergence.

Passing visual tests without performance fixtures is permission to grow a slow
machine with a nice face. No.

## Ownership

### Client

The client owns semantic intent:

- planet/world seed and setting policy;
- agent visual state and role/body grammar selection;
- authored grammar parameters and state bindings;
- stable domain ids for persistence and cache keys.

For Zyphos, this means planet style, biome rules, and simulation-facing terrain
policy. For Epiphany, this means role/body visual state and semantic feature
bindings. The engine should not learn client nouns just because the first
grammar is interesting.

### Engine Contracts

Contracts own portable declarations:

- domain roots;
- semantic brush claims;
- grammar tree nodes;
- node summary schema;
- backend packet schema;
- debug channel names.

Contracts must stay Vortice-free and renderer-agnostic. They describe the
machine; D3D12 is one lowering target.

### Engine Fractal Core

The fractal core owns pure CPU algorithms:

- cube-sphere domain math;
- grammar expansion;
- ownership tree bounds;
- summary generation;
- contribution scoring;
- stochastic estimator updates;
- selected-cut and residency decisions;
- backend packet planning.

This should be the main testable body of the system.

### Engine Renderer

The renderer owns:

- GPU buffers, textures, pages, and descriptors;
- screen-space scoring when moved to compute;
- backend packet upload;
- render graph execution;
- shader evaluation;
- visual diagnostics.

## Algorithms

### 1. Cube-Sphere Surface Domain

Planetary surfaces use six quadtree cube faces projected to a sphere. This cuts
lat-long pole singularities and gives each face a simple 2D tile domain.

Projection candidates:

- tangent warp: cheap, analytic inverse, acceptable area behavior;
- fifth-order odd polynomial warp: strong GPU candidate, low area error;
- COBE/QSC-style bivariate warp: best area behavior, more expensive;
- exact equal-area reference: validation and offline bake target, not default
  hot path.

Tradeoff:

- Time: tangent/polynomial are cheapest per sample; COBE buys area quality.
- Memory: all use the same tile address space; no major residency difference.
- CPU/GPU: CPU can use exact or COBE for authoring/bakes; GPU hot shaders can
  use tangent or polynomial if measured faster.

Decision rule: measure area error, inverse lookup cost, and shader time on the
actual target hardware before choosing.

### 2. Semantic IFS Brush Grammar

The grammar emits claims, not final triangles or shader code.

```text
claim {
  stableKey
  name
  kind
  domain
  localFrame
  envelope
  payload
  tags
  lodRange
  costTier
  seed
  children
}
```

Kinds:

- `material`
- `height`
- `displacement`
- `sdf_form`
- `void`
- `mask`
- `light`
- `debug`

Required grammar operations:

- `Union`
- `Subtract`
- `Common`
- `Repeat`
- `Refine`
- `Capture`
- `Bind`

VibeGeometry's useful lesson is the split between semantic claims and CSG
adjudication. Aquarium should keep that split even when the current renderer
only supports a flattened evaluator. The authored tree remains durable; the
flattened packets are expendable.

Tradeoff:

- Time: semantic grammar costs CPU work up front but lets runtime avoid global
  interpretation in hot shaders.
- Memory: preserving tags, frames, and captures costs RAM, but buys cache keys,
  debugging, and selective invalidation.
- SSD: authored grammar is compact; generated payloads and summaries can be
  cached separately.
- GPU: shaders consume fixed compact packets instead of walking prose-shaped
  abstractions.

### 3. Ownership Tree

The ownership tree is a lightweight CSG/IFS program:

```text
Rule tree
-> semantic claims
-> boolean/IFS ownership tree
-> node summaries
-> backend lowering
```

It is not a full CAD kernel in the first version. It needs:

- stable node ids;
- parent/child topology;
- branch operation;
- child ranges;
- node bounds;
- summary error;
- payload handles;
- debug labels.

Bounds follow operation semantics:

- union bounds are child-bound unions;
- subtraction bounds are source/left bounds;
- common/intersection bounds are child-bound intersections;
- repeat/refine bounds are generated or conservative aggregate bounds.

Tradeoff:

- Time: tree bounds stop traversal before child work.
- Memory: summaries are kept resident even when children are evicted.
- SSD: child payloads can stream by stable key.
- GPU: renderer receives selected cuts and compact resident node tables.

### 4. Shaped Brush Envelope

Every brush has a compact-support anisotropic envelope. It borrows the useful
part of Gaussian splatting: covariance-like shape and projected footprint. It
does not inherit infinite support as a default tax.

Hot evaluator:

```text
q = transpose(rotation) * (p - center)
m = dot(q * invRadii, q * invRadii)
support = saturate(1 - m)
weight = support * support * (3 - 2 * support)
```

Shape controls can add taper, skew, ridge, hollow, signed lobe, or curve-local
response, but every control must preserve an explicit conservative bound.

Tradeoff:

- Time: compact support enables culling and fixed packet limits.
- Memory: envelope rows are small and SIMD/GPU friendly.
- GPU: projected ellipse scoring is better than sphere scoring when affordable;
  sphere/AABB fallback remains legal.
- Fidelity: anisotropy gives terrain ridges, rivers, petals, scars, and veins
  fewer fake circular footprints.

### 5. Node Summaries

Every tree node needs a renderable summary:

```text
stableKey
parent
childRange
domain
localFrame
worldOrTileBound
envelopeBound
maxFormError
maxMaterialDelta
normalOrGradientBound
coverageBound
frequencyBand
estimatedCost
summaryPayloadHandle
childPayloadHandle
```

The summary is both fallback and promise. If a node cannot summarize its
children with bounded error, it is not ready to be an LOD node.

Tradeoff:

- Time: summaries stop traversal and keep missing children renderable.
- Memory: summaries stay hot; child payloads can be evicted.
- SSD: summaries form the streaming index.
- GPU: fixed summary buffers feed scoring and debug views.

### 6. Contribution Scoring

The first score is heuristic and conservative:

```text
projectedRadiusPx = project_bound_radius(node.bound, camera)
projectedErrorPx = project_world_error(node.maxFormError, node.bound, camera)
materialScore = node.maxMaterialDelta * projectedCoveragePx
formScore = projectedErrorPx * visibleCoverageEstimate
score = max(formScore, materialScore) * importanceBias / estimatedCost
```

LOD cut:

```text
render summary if parent needed and node is good enough
descend if node is not good enough, children are resident, and budget allows
request children if node is not good enough and children are missing
```

Fade:

- use hysteresis around the threshold;
- fade payload amplitude, material response, or selected child contribution;
- do not fade signed-distance safety.

Tradeoff:

- Time: cuts avoid leaf scoring.
- Memory: only selected subtrees need payload residency.
- GPU: scoring can move progressively from CPU to compute.
- Fidelity: error thresholds target visible pixel impact, not arbitrary depth.

### 7. Probabilistic Online Estimator

The contribution cache cannot refresh every branch every frame. It must update
weights probabilistically under budget and converge.

Estimator state:

```text
meanContribution
varianceOrUncertainty
sampleCount
lastSampleFrame
lastVisibleFrame
confidence
decayRate
updateProbability
```

Sampling priority:

```text
priority =
  visibilityProbability
  * staleWeight
  * uncertainty
  * max(previousScore, parentScoreBias)
  / max(estimatedUpdateCost, epsilon)
```

Update:

```text
delta = observedContribution - meanContribution
meanContribution += alpha * delta
variance = lerp(variance, delta * delta, beta)
confidence = saturate(confidence + confidenceGain)
```

Non-update:

```text
confidence *= decay
uncertainty += staleGrowth
```

This is machine learning in the practical online-estimation sense. The first
version should be an exponential moving average plus variance and bandit-style
exploration. A later learned predictor may rank update priority and residency,
but it must not own conservative SDF bounds, object visibility, or safety.

Tradeoff:

- CPU: stochastic scheduling keeps per-frame management bounded.
- RAM: estimator state is a few scalars per summary.
- SSD: probes and observed deltas can become offline training/evidence data.
- GPU: high-quality probes can be sparse and amortized.
- Fidelity: exploration prevents stale branches from staying confidently wrong.

### 8. Residency And Streaming

Stable key:

```text
planet: cubeFace + quadtreePath + grammarPath
body: bodyId + grammarPath
curve: ownerId + curvePath + grammarPath
```

Memory tiers:

- CPU hot: visible roots, node summaries, estimator state, selected cuts.
- CPU warm/RAM: nearby summaries and recently used child payload metadata.
- SSD: serialized child payload pages, baked summaries, probe history.
- GPU hot: selected cut buffers, resident payload pages, tile/body packet tables.

Residency rules:

- summaries outlive child payloads;
- parent summary renders while child payload streams;
- evict low-score high-cost children first;
- keep near-threshold children longer to avoid popping;
- write payload pages append-only within a frame;
- compact only at safe sync points.

Tradeoff:

- Time: avoid global rebuilds and expensive compaction mid-frame.
- Memory: child payloads are elastic; summaries are stable.
- SSD: streaming is by stable semantic key, not transient allocation.
- GPU: residency buffers stay compact and bounded.

### 9. Backend Lowering

One authored tree can lower into several backend shapes:

- 2D tile height/material brushes;
- scalar height-field brush pass;
- object-local SDF form packets;
- anisotropic impostor/splat batches;
- offline mip summaries;
- debug overlays.

The first backend should be deliberately modest:

- shaped 2D terrain brushes on cube-face tiles;
- CPU-side node summary and contribution cache;
- fixed GPU structured buffers;
- one Zyphos planet demo path;
- one Epiphany/body proof path later.

Tradeoff:

- Time: start with the cheapest visible proof.
- Memory: defer 3D recursive form until 2D summaries and scoring are real.
- GPU: fixed tables are ugly but honest; graph-driven dynamic evaluation comes
  after debug views prove the cut.

## Resource Strategy

### CPU

CPU owns grammar expansion, coarse scoring, stochastic update scheduling,
streaming requests, eviction, selected cuts, and debug collection. CPU must
avoid per-leaf full traversal, synchronous SSD waits, and rebuilding payload
pages during rendering.

### RAM

RAM owns node summaries, estimator state, warm payload metadata, recent probe
results, and selected cuts. RAM should not keep all recursive child payloads
resident.

### SSD

SSD owns serialized payload pages, offline baked summaries, probe/evidence
datasets, and optional learned-predictor training data. SSD is never on the
critical path for a missing child this frame; the parent summary renders.

### GPU

GPU owns selected-cut evaluation, brush envelope evaluation, height/material
passes, SDF proxy evaluation, optional compute scoring, and debug visualization.
GPU must not walk the whole grammar tree or trust learned scores for
conservative distance safety.

## Data Structures

### `AquariumFractalDomain`

```text
domainId
kind
rootKey
rootFrame
projection
bound
clientOwner
```

### `AquariumBrushClaim`

```text
stableKey
kind
domainId
localFrame
envelope
payload
tags
lodRange
costTier
seed
```

### `AquariumFractalNode`

```text
stableKey
operation
claimRange
childRange
summary
debugName
```

### `AquariumFractalSummary`

```text
bound
maxFormError
maxMaterialDelta
normalOrGradientBound
coverageBound
estimatedCost
summaryPayload
childPayload
```

### `AquariumContributionState`

```text
mean
variance
confidence
sampleCount
lastSampleFrame
lastVisibleFrame
updateProbability
residentState
fade
```

### `AquariumSelectedCut`

```text
domainId
nodeKey
lodDepth
fade
payloadHandle
debugScore
```

## Render Frame Flow

```text
1. Collect visible domains from camera, cube faces, and SDF proxy bounds.
2. Load resident summaries for visible roots.
3. Score summaries against the camera and previous history.
4. Select a hierarchy cut under CPU/GPU budget.
5. Queue missing child payloads; render parent summaries meanwhile.
6. Sample a probabilistic subset of node weights for estimator convergence.
7. Upload selected cut and resident payload tables.
8. Run tile/body backend passes.
9. Resolve fade transitions and temporal history.
10. Emit debug surfaces and cache telemetry.
```

## Debug Views

No recursive detail without these:

- cube face / tile id;
- selected LOD depth;
- projected error;
- mean contribution;
- uncertainty;
- sample age;
- update probability;
- residency state;
- fade weight;
- estimated cost;
- selected backend;
- SDF step count where form detail is active.

## Implementation Roadmap

### Phase 0: Architecture Freeze

Deliverables:

- this plan;
- updated research note;
- state memory/map entries.

Exit criteria:

- the live architecture can be explained without hidden compensators;
- module boundaries and test seams are named;
- roadmap names what gets cut if complexity rises.

### Phase 1: Cube-Sphere Projection Harness

Build:

- generic cube-face address type;
- forward/inverse projection candidates;
- area distortion test harness;
- GPU shader microbench for candidate mappings;
- Zyphos debug view for face/tile coordinates.

Tests:

- projection round trips;
- face-edge continuity cases;
- sampled area distortion baselines;
- shader/CPU projection parity.

Cut line:

- if projection research blocks rendering, ship tangent first behind an
  interface and keep the measured comparison harness.

### Phase 2: Shaped Brush Envelope

Build:

- shared CPU/GPU envelope struct;
- compact-support anisotropic evaluator in HLSL;
- CPU mirror evaluator for tests;
- replacement path for current circular height brushes;
- debug view for brush bounds and weight.

Tests:

- CPU/GPU evaluator parity;
- support bound rejection;
- deterministic packet serialization;
- side-by-side old/new brush fixture.

Cut line:

- start with rotation plus radii and falloff only. Taper/skew/ridge wait.

### Phase 3: Semantic Claims And Ownership Tree

Build:

- contract structs for domains, claims, nodes, summaries;
- small in-memory grammar builder;
- union/subtract/common/repeat/refine/capture/bind operations;
- flattening compiler to current brush tables;
- debug dump of authored tree and flattened backend packets.

Tests:

- seeded grammar determinism;
- stable keys across reload;
- operation bounds for union/subtract/common;
- flattening golden packets;
- client adapter does not reference renderer types.

Cut line:

- do not implement full CAD CSG. Keep boolean ownership semantic and bounded.

### Phase 4: Node Summaries

Build:

- summary generation for 2D terrain brush trees;
- max error/material delta fields;
- parent fallback payload;
- serialization format for summaries;
- debug view for summary bounds and error.

Tests:

- summary conservativeness against generated children;
- parent fallback when children are missing;
- summary serialization round trips;
- evicted children do not remove fallback.

Cut line:

- if exact summary generation is hard, use conservative amplitude/frequency
  bounds first. Ugly but safe beats precise lies.

### Phase 5: Contribution Cache

Build:

- stable-keyed contribution table;
- projected error scorer;
- selected-cut builder;
- hysteresis fade weights;
- debug views for score, depth, fade, cost.

Tests:

- distance monotonicity;
- stable cut under small camera motion;
- budget clamp behavior;
- fade never changes conservative SDF bounds.

Cut line:

- keep scoring on CPU until the data layout is stable.

### Phase 6: Probabilistic Online Updates

Build:

- estimator state per node;
- stochastic update scheduler;
- exploration budget;
- confidence decay;
- high-quality probe hook;
- telemetry for convergence.

Tests:

- deterministic RNG schedule fixture;
- visible and near-threshold nodes have nonzero update probability;
- stale wrong score recovery;
- convergence under fixed camera path;
- update cost respects budget.

Cut line:

- no neural predictor until EMA/variance/bandit scheduling fails with evidence.

### Phase 7: Residency And Streaming

Build:

- CPU warm cache;
- GPU payload pages;
- SSD-backed payload serialization;
- child request queue;
- eviction policy;
- safe compaction points.

Tests:

- fake payload store miss renders parent summary;
- request queue ordering;
- eviction by score/cost;
- frame path never blocks on fake SSD.

Cut line:

- first version can keep payloads generated in RAM and simulate SSD requests.
  The API must still be page/key based.

### Phase 8: Zyphos Planet Integration

Build:

- cube-sphere terrain domain in Zyphos;
- first terrain grammar: continents/ridges/craters/material bands;
- selected-cut-driven tile payloads;
- atmospheric/planet shader sampling from tile domain.

Tests:

- planet renders across face edges;
- debug views identify face/tile/LOD/cache state;
- old spherical-noise path remains only as baseline fixture or is removed.

Cut line:

- if 3D body grammar tempts us here, cut it. Planet terrain proves the address,
  envelope, summary, and LOD cache first.

### Phase 9: Agent Body Grammar Pilot

Build:

- one role body grammar using semantic claims;
- object-local summary tree;
- material/detail brush payloads before traced form recursion;
- SDF proxy integration through selected cut.

Tests:

- silhouette coherent across LODs;
- fine detail fades by contribution;
- SDF step count stays inside budget;
- no scene-global mega-SDF.

Cut line:

- if form recursion is unstable, keep recursive detail in material payloads and
  leave traced form as coarse base SDF.

### Phase 10: Learned Scoring Research Gate

Build only after telemetry exists:

- probe dataset writer;
- offline analysis of predicted vs observed contribution;
- small learned predictor or calibrated regression;
- A/B mode against heuristic scheduler.

Tests:

- held-out camera paths;
- learned mode beats heuristic fidelity/cost clearly;
- conservative summaries still own safety;
- fallback heuristic remains available.

Cut line:

- if learned scoring cannot beat the estimator clearly, delete it.

## Current First Slice

The first implementation slice should be:

```text
cube-face address + projection harness
-> shaped 2D brush envelope
-> semantic claim/tree structs
-> flatten to current height brush pass
-> debug face/tile/brush/score overlays
```

This gives a coherent foundation without forcing the whole fractal object system
to exist at once. The machine earns recursion only after address, envelope,
summary, tests, and debug surfaces are real.

## Sources

This plan consolidates:

- `research/rendering/cube-sphere-fractal-brushes.md`
- local VibeGeometry `vg_grammar` and `vg_csg` tree work;
- QSC / cube-sphere projection research;
- EWA and 3D Gaussian splatting envelope ideas;
- Hart sphere tracing;
- geometry clipmaps;
- Nanite-style projected-error hierarchy cuts;
- Octree-GS / LOD-structured Gaussian splatting;
- Aquarium's existing SDF proxy, height brush, and debug-pass constraints.
