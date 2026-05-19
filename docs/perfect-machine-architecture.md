# Perfect Machine Architecture

## Objective

Build one real-time spatial evidence machine that can render and resolve:

- fast 2D SDF splats on tileable surfaces;
- fast 3D SDF splats in object/world volumes;
- 2D fields projected onto 3D domains such as cube-sphere planets and toroidal
  station decks;
- camera/microphone sensor evidence from Mimir-style capture;
- cached structural probes emitted by IFS `.aquageo` grammars;
- stochastic updates that converge under CPU, GPU, RAM, and SSD budgets.

The product requirement is not "infinite detail." It is a compact authored
description that can put detail where pixels can see it, keep enough evidence
alive to stabilize it, and refuse work that cannot justify its cost.

## Research Spine

This architecture is not invented from the floorboards:

- ReSTIR DI and GI prove reservoir reuse as a real-time sampling strategy for
  expensive candidates across time and screen space.
- GRIS generalizes reservoir resampling beyond direct light candidates through
  target functions, correlated samples, domains, and shift mappings.
- EWA surface/volume splatting and 3D Gaussian splatting prove anisotropic
  projected support as a practical rendering primitive.
- Geometry clipmaps and Nanite-style virtualized geometry prove that visible
  projected error and resident summaries beat global leaf traversal.
- TSDF fusion/KinectFusion prove that streamed sensor samples can accumulate
  into a coherent implicit spatial field.
- Instant-NGP's multiresolution hash encoding is a warning and inspiration:
  stochastic evidence can be fast when the data layout is explicit and GPU
  friendly, but Aquarium should preserve deterministic summaries and bounds as
  safety authority.

## Prime Invariants

- Authored semantic domains are the source of truth.
- Backend packets are compiled output and may be deleted/rebuilt.
- Every domain, node, claim, probe, payload, page, and reservoir has a stable
  key.
- Every reusable sample must name its target function and source PDF or state
  why it is non-probabilistic structural evidence.
- Every LOD subtree has a conservative parent summary.
- Learned/stochastic priority may rank work; it may not own SDF safety,
  visibility safety, or calibration safety.
- Missing children render through parent summaries. A frame does not wait for
  SSD.
- TAA owns pixel history, not producer identity.
- Mimir sensor fusion and fractal rendering share the evidence machinery; they
  do not share client policy.

## System Pipeline

```text
Authored intent or live sensor input
-> Domain binding
-> Evidence candidate generation
-> Target evaluation
-> Resampled importance reservoir
-> Temporal reuse validation
-> Spatial/domain reuse validation
-> Occupancy graph update
-> Residency/page scheduling
-> Backend packet lowering
-> 2D/3D/projected SDF splat passes
-> Temporal resolve guide buffers
-> Debug/evidence telemetry
```

The machine is allowed to have multiple candidate producers. The reservoir
contract makes them comparable without pretending they are the same thing.

## Module Network

```text
Aquarium.Engine.Contracts
  Stable DTOs: domains, claims, nodes, summaries, probes, reservoir guides,
  payload pages, backend packets, debug rows.

Aquarium.Engine.Fractal
  Pure CPU algorithms: domain math, grammar expansion, ownership trees,
  summaries, scoring, reservoirs, occupancy graph updates, residency planning,
  packet planning.

Aquarium.Engine.SensorFusion
  Future shared adapter layer: camera/audio feature candidates, calibration
  confidence, raw retention lowerings, Mimir-facing packet contracts.

Aquarium.Engine.Render
  D3D12 resources, page tables, structured buffers, splat/SDF passes, TAA guide
  buffers, debug visualization.

Aquarium.Zyphos
  World policy, cube-sphere/tile roots, planet grammar seeds, setting-safe
  visuals.

Aquarium.Epiphany
  Agent/body policy, role grammar selection, semantic bindings.

Mimir/LocalCast
  Sensor capture, raw rolling retention, feature extraction, calibration facts.
  It does not own resolved spatial evidence history.
```

## Data Model

### Domain

```text
DomainKey
ParentKey
Kind: solar/orbital/planet/cubeTile/torus/surface2d/object3d/volume3d/sensorRig
Frame
Projection
Bounds
Periodicity
Owner
```

Domains say where coordinates live and how they map to parent space. A domain is
allowed to be 2D, 3D, or 2D projected onto 3D.

### Claim

```text
ClaimKey
DomainKey
NodeKey
Kind: height/material/sdf2d/sdf3d/void/light/feature/confidence
LocalFrame
Envelope
Payload
Tags
CostTier
Seed
```

Claims are authored or inferred statements about a field. They are not renderer
packets.

### Probe

```text
ProbeKey
DomainKey
LocalPosition
LocalNormalOrGradient
BoundRadius
PayloadHandle
TargetContribution
SourcePdf
Confidence
ObservedFrame
ObservedTime
ProducerKind
```

A structural probe can come from an IFS grammar. A sensor probe can come from a
camera/audio feature. Both become candidates only after they have a target
function and validation rules.

### Reservoir

```text
SelectedSample
SelectedTarget
WeightSum
CandidateCount
ContributionWeight
ValidationMask
SampleAge
DomainKey
```

The reservoir owns resampling math. It does not own stable tracks, packet
lowering, or TAA history.

### Occupancy Graph

```text
NodeKey
DomainKey
Bounds
SummaryPayload
ChildPayload
MeanContribution
Variance
Confidence
SampleAge
ResidencyState
LastVisibleFrame
LastUpdateFrame
```

The occupancy graph is the shared heuristic memory of where useful field
evidence likely lives. It is structural enough for IFS trees and statistical
enough for sensor fusion.

### Page

```text
PageKey
PayloadKind
ByteRangeOrHandle
ResidentTier: gpu/ram/ssd/missing
EstimatedGpuCost
EstimatedRamBytes
EstimatedSsdBytes
LastUseFrame
```

Pages are storage decisions, not semantic identity.

## Algorithms

### 1. Domain Binding

Every candidate is first bound to a domain path. Cube-sphere terrain binds to a
face/tile path. Torus surfaces bind to periodic `u/v` patches. Agent details
bind to object-local sheets, curves, or volumes. Sensor features bind to a
sensor rig frame and then to a calibrated world or object domain.

Validation starts here. If two samples do not share a valid lineage or shift
mapping, they do not reuse each other.

### 2. IFS Grammar Expansion

The `.aquageo` DSL emits semantic claims and ownership nodes:

```text
grammar -> domains -> claims -> ownership tree -> summaries
```

Expansion is deterministic by stable key and seed. Generated leaves do not all
need to be resident. The ownership tree must summarize itself before it earns
LOD rights.

### 3. Sensor Candidate Lowering

Camera and microphone producers retain raw capture ordering, calibration, and
provenance. Aquarium receives resolved candidate observations:

```text
camera/audio feature -> calibrated local/world candidate -> target evaluator
```

Sensor adapters own modality interpretation. Aquarium owns spatial evidence
history after lowering.

### 4. Target Evaluation

Targets are comparable scalar contributions:

- projected SDF/form error;
- material delta over visible coverage;
- feature confidence times calibration confidence;
- expected pixel influence;
- expected information gain for an uncertain region.

The source PDF is the probability by which the candidate was proposed. If the
candidate is deterministic structural evidence, it uses an explicit proposal
policy rather than a magical free sample.

### 5. Reservoir Update

Each reservoir stores one representative sample plus the weight mass of the
candidates it represents:

```text
candidateWeight = target / sourcePdf
weightSum += candidateWeight
candidateCount += representedCount
select candidate with probability candidateWeight / weightSum
```

Final contribution weight follows the RIS/ReSTIR shape:

```text
weightSum / (candidateCount * selectedTarget)
```

Temporal reuse and spatial/domain reuse are explicit passes, not side effects.

### 6. Reuse Validation

Temporal reuse validates:

- camera motion/reprojection;
- previous/current domain lineage;
- disocclusion;
- field id/material class;
- local-frame error;
- conservative bounds;
- sample age and confidence.

Spatial/domain reuse validates:

- neighbor surface compatibility;
- tile adjacency or periodic wrap;
- cube-face seam mapping;
- torus seam mapping;
- object-local parent frame stability;
- sensor calibration agreement.

GRIS-style shift mappings live here. A shift is an owned object with tests, not
a helper function hiding inside a shader.

### 7. Occupancy Graph Update

The occupancy graph receives selected reservoirs and sparse high-quality probes.
It updates node statistics under budget:

```text
mean, variance, confidence, sampleAge, lastVisible, lastUpdated
```

Unvisited nodes decay. Stale uncertainty grows. Visible, uncertain, stale, and
near-threshold nodes retain nonzero exploration probability.

### 8. Selected Cut And Residency

A hierarchy cut is selected from summaries:

```text
projected error
material delta
coverage
current reservoir confidence
estimated GPU cost
residency state
```

Children stream only when the cut justifies them. Parent summaries remain
renderable. Eviction favors low-score high-cost payloads and protects
near-threshold nodes from thrash.

### 9. Backend Lowering

The same semantic field can lower to:

- 2D SDF tile pages;
- 2D height/material pages;
- 3D SDF splat packets;
- 2D-projected-to-3D splat packets;
- sensor confidence volumes;
- debug overlays.

The first renderer path should use compact-support anisotropic envelopes. They
borrow Gaussian splatting's covariance discipline without inheriting infinite
support as a default runtime tax.

### 10. TAA Guide Integration

TAA consumes guide signals:

- reservoir confidence;
- sample age;
- domain validity;
- temporal detail;
- invalidation reason;
- field id and motion.

TAA does not own reservoirs. It only decides how much pixel history may survive.

## Resource Tradeoffs

### CPU

CPU performs grammar expansion, coarse scoring, stochastic scheduling, page
requests, eviction decisions, and selected-cut planning. It must not rescore
every leaf or rebuild payload pages mid-frame.

### RAM

RAM keeps summaries, estimator state, hot/warm metadata, selected cuts, and
recent probes. It does not keep every generated child payload.

### SSD

SSD stores payload pages, summary pages, probe history, and optional training
datasets. SSD never blocks a frame; parent summaries render while requests are
pending.

### GPU

GPU evaluates selected packets, splats, SDF proxy passes, compute scoring where
profitable, page-table sampling, and debug views. It must not walk the authored
grammar tree.

## Render Frame Flow

```text
1. Gather visible domains and active sensor regions.
2. Load resident summaries and previous reservoirs.
3. Generate structural and sensor candidates under CPU budget.
4. Evaluate targets and source PDFs.
5. Update local reservoirs.
6. Validate temporal reuse.
7. Validate spatial/domain reuse.
8. Update occupancy graph statistics.
9. Select hierarchy cut under CPU/GPU/RAM/SSD budgets.
10. Queue missing pages; keep parent summaries active.
11. Lower selected evidence to 2D/3D/projected SDF splat packets.
12. Render splat/SDF passes.
13. Resolve with TAA guide buffers.
14. Emit debug telemetry and evidence logs.
```

## Test Boundaries

Pure unit seams:

- projection round trips and seam continuity;
- domain lineage and shift mapping;
- DSL expansion determinism;
- ownership bounds;
- summary conservativeness;
- reservoir math;
- stochastic scheduler convergence;
- residency selection under fake stores.

Mock boundaries:

- `IFractalRandom`;
- `IFractalClock`;
- `IContributionProbe`;
- `IFractalPayloadStore`;
- `ISensorCandidateSource`;
- `IDomainShiftMap`;
- `ITaaGuideSink`;
- `IFractalDebugSink`.

GPU parity:

- envelope evaluator;
- projection evaluator;
- packet decoding;
- selected-cut fade;
- guide-buffer packing.

Performance fixtures:

- fixed camera path;
- fixed DSL world;
- fixed sensor trace;
- fixed budget profile for GTX 1070-class hardware;
- output CPU ms, GPU ms, payload count, page requests, selected cut size,
  reservoir confidence, and convergence error.

## Waterfall Implementation Phases

### Phase A: Architecture Freeze

Deliver this document, the public thesis article, updated memory, and a cut
line for the TAA guide-buffer fork.

### Phase B: Explicit Reservoir Guide Layout

Add a dedicated reservoir guide history target rather than packing previous
reservoir validity into existing control channels. Preserve current-control.w
as the current confidence lane.

### Phase C: Fractal Probe Pipeline

Turn IFS node probes into typed reservoir candidates with renderer-facing camera
motion, disocclusion, material, and visibility validation.

### Phase D: Occupancy Graph

Promote contribution state into the heuristic occupancy graph and add
confidence decay, stale uncertainty growth, and convergence telemetry.

### Phase E: 2D SDF Tile Backend

Build cached 2D SDF/height/material pages for cube-sphere and torus domains.
Use parent summaries while child pages stream.

### Phase F: 3D SDF Splat Backend

Build compact-support 3D SDF splat packets for object/volume domains. Keep
distance safety conservative and LOD gated.

### Phase G: 2D-Projected-To-3D Backend

Project 2D SDF tile pages onto 3D domains: cube-sphere planets, torus stations,
curved sheets, and object-local surfaces.

### Phase H: Mimir Adapter

Add sensor candidate adapters after the fractal reservoir path proves the
contract. Camera/audio features become candidates; Aquarium owns resolved
evidence.

### Phase I: GPU Reservoirs And Spatial Reuse

Move hot reservoirs to GPU buffers, add spatial reuse across screen tiles and
domain neighbors, and keep CPU/GPU parity fixtures.

### Phase J: Learned Priority Gate

Only after telemetry exists, train or calibrate a predictor for update/residency
priority. Delete it if it does not beat the heuristic on held-out camera/sensor
traces.

## Cut Lines

- No learned predictor before telemetry.
- No recursive 3D form before 2D tile summaries and debug views are boring.
- No consumer-owned stable temporal cache.
- No GPU grammar traversal.
- No SSD frame stalls.
- No reuse without an explicit domain shift and validation contract.
- No debug-free recursive density.

## Sources

- NVIDIA ReSTIR DI:
  https://research.nvidia.com/publication/2020-07_spatiotemporal-reservoir-resampling-real-time-ray-tracing-dynamic-direct
- NVIDIA ReSTIR GI:
  https://research.nvidia.com/publication/2021-06_restir-gi-path-resampling-real-time-path-tracing
- GRIS:
  https://graphics.cs.utah.edu/research/projects/gris/sig22_GRIS.pdf
- RTXDI ReSTIR docs:
  https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/RestirGI.md
  https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/RestirPT.md
- EWA splatting:
  https://dash.harvard.edu/bitstream/handle/1/4138240/Zwicker_EWA.pdf
- Object-space EWA surface splatting:
  https://publications.ri.cmu.edu/object-space-ewa-surface-splatting-a-hardware-accelerated-approach-to-high-quality-point-rendering/
- 3D Gaussian Splatting:
  https://arxiv.org/abs/2308.04079
- Geometry clipmaps:
  https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry
- Nanite:
  https://advances.realtimerendering.com/s2021/Karis_Nanite_SIGGRAPH_Advances_2021_final.pdf
- KinectFusion:
  https://www.microsoft.com/en-us/research/publication/kinectfusion-real-time-dense-surface-mapping-tracking/
- Curless and Levoy volumetric fusion:
  https://graphics.stanford.edu/papers/volrange/volrange.pdf
- Instant-NGP:
  https://research.nvidia.com/publication/2022-07_instant-neural-graphics-primitives-multiresolution-hash-encoding
