# Fractal Brush Implementation Roadmap

This is the execution map for the fractal brush architecture in
`research/rendering/fractal-brush-architecture-plan.md`.

The architecture is allowed to be ambitious. This roadmap is not. It exists to
keep the work shippable, testable, and cuttable.

## Objective

Build a reusable mapped fractal field system for planetary terrain, tileable
surface detail, and later agent/body detail.

The first live proof is deliberately narrow:

```text
cube-face address + projection harness
-> shaped 2D brush envelope
-> semantic claim/tree structs
-> flatten to current height brush pass
-> debug face/tile/brush/score overlays
```

No 3D recursive body form. No learned predictor. No streaming page store. The
first pass proves address, envelope, semantic tree, flattening, tests, and
debug visibility.

## Standing Invariants

- The authored semantic tree is the source of truth.
- Backend packets are compiled output and may be replaced.
- Every domain, node, claim, and payload has a stable key.
- Every LOD subtree has a conservative parent summary.
- The cache may learn priority; it may not own SDF safety.
- Missing children render through parent summaries instead of stalling.
- Debug views are required before recursive detail density increases.
- Each module owns one invariant and exposes a mockable seam.

## Module Targets

### `Aquarium.Engine.Contracts`

Owns portable DTOs:

- `AquariumFractalDomain`
- `AquariumBrushClaim`
- `AquariumFractalNode`
- `AquariumFractalSummary`
- `AquariumContributionState`
- `AquariumSelectedCut`
- backend packet rows
- debug channel rows

Tests:

- serialization round trips;
- stable key equality;
- no renderer/Vortice dependency.

### `Aquarium.Engine.Fractal`

Owns pure CPU algorithms:

- cube-face addressing;
- projection candidates;
- local frame/domain math;
- grammar expansion;
- ownership tree bounds;
- summary building;
- contribution scoring;
- stochastic estimator;
- selected-cut planning;
- residency decisions.

Tests:

- regular unit tests with deterministic fakes;
- no D3D12 boot required.

### `Aquarium.Engine.Render`

Owns D3D12 lowering:

- GPU buffers;
- descriptor binding;
- shader packet decode;
- height/material/SDF backend passes;
- debug visualizations.

Tests:

- packet golden tests;
- CPU/HLSL parity tests;
- headless smoke renders.

### Client Adapters

`Aquarium.Zyphos` owns planet domain roots and terrain grammar policy.

`Aquarium.Epiphany` later owns agent/body grammar selection and semantic state
bindings.

Tests:

- clients emit contracts without renderer types;
- stable seeds emit stable keys.

## Mock Boundaries

Add these before the modules need them:

- `IFractalClock`: deterministic frame/time.
- `IFractalRandom`: deterministic update lottery.
- `IProjection`: projection candidate swap.
- `IContributionProbe`: observed pixel/material/SDF delta.
- `IFractalPayloadStore`: fake RAM/SSD payload availability.
- `IFractalGpuBudget`: fake GPU table/page limits.
- `IFractalDebugSink`: telemetry capture without UI.

Mock resources and observations, not private helper functions. The tests should
pin module contracts, not puppet implementation details.

## Phase 0: Test And Project Skeleton

Goal: create the module/testing skeleton before algorithm code spreads.

Tasks:

- [x] Add `src/Aquarium.Engine.Fractal`.
- [x] Add `tests/Aquarium.Engine.Fractal.Tests`.
- [x] Add project references from tests to contracts/fractal core.
- [x] Add deterministic fake clock/random/debug sink test helpers.
- [x] Add a boundary test proving `Aquarium.Engine.Fractal` has no D3D12/Vortice dependency.

Exit gate:

- `dotnet build Aquarium.Engine.sln`
- `dotnet test tests/Aquarium.Engine.Fractal.Tests`

Cut line:

- If module wiring fights the existing solution, keep the new core smaller, not
  sneak fractal logic into `D3D12Renderer`.

## Phase 1: Cube-Face Address And Projection Harness

Goal: establish stable planetary tile addresses and measured projection choices.

Tasks:

- [x] Add `CubeFace`, `CubeTileKey`, and quadtree path helpers.
- [x] Add local tile uv to cube-face coordinate conversion.
- [x] Implement identity-normalize projection as baseline.
- [x] Implement tangent projection candidate.
- [ ] Implement fifth-order odd polynomial projection candidate.
- [ ] Add placeholder/interface for COBE/QSC candidate.
- [x] Add area distortion sampler.
- [x] Add face-edge continuity tests.
- [x] Add CPU benchmark or fixture reporting candidate costs.
- [ ] Add Zyphos debug mode that colors face/tile coordinates.

Tests:

- projection round trips where inverse exists;
- face-edge continuity cases;
- deterministic tile key formatting;
- sampled area error fixtures.

Exit gate:

- projection report names current default and measured error/cost;
- Zyphos can show face/tile debug colors.

Cut line:

- Ship tangent projection first if polynomial/COBE cost delays visible work.
  Keep the harness so the choice stays measurable.

## Phase 2: Shaped 2D Brush Envelope

Goal: replace circular brush thinking with compact-support anisotropic fields.

Tasks:

- [x] Add CPU `FractalBrushEnvelope2D`.
- [x] Add HLSL packed envelope row.
- [x] Implement compact-support ellipse evaluator.
- [x] Add CPU/HLSL parity fixture.
- [x] Add brush bounds and support rejection tests.
- [x] Add a small adapter that lowers one claim into the current height brush path.
- [ ] Add debug overlay for envelope bounds and weight.

Tests:

- weight at center/edge/outside support;
- rotation parity;
- deterministic packet packing;
- old circular brush fixture still reproducible through a round envelope.

Exit gate:

- current height pass can render shaped 2D envelopes through a compatibility adapter.

Cut line:

- Do not add taper/skew/ridge controls until the basic envelope has debug views
  and parity tests.

## Phase 3: Semantic Claims And Ownership Tree

Goal: preserve authoring meaning before backend flattening.

Tasks:

- [x] Add contract DTOs for domains, claims, nodes, summaries, and selected cuts.
- [x] Add stable key builder for planet tile + grammar path.
- [x] Add in-memory ownership tree builder.
- [x] Implement operations: `Union`, `Subtract`, `Common`, `Repeat`, `Refine`, `Capture`, `Bind`.
- [ ] Implement operation bounds.
- [x] Add flattening compiler to shaped 2D brush packets.
- [x] Add debug dump for authored tree and flattened packets.
- [x] Preserve multiple `.aquageo` tile roots in the ownership tree instead of
  collapsing every claim under the final tile.
- [x] Add compact `flame` grammar primitive for deterministic curled recursive
  brush emission.

Tests:

- [x] seeded grammar determinism;
- stable keys across equivalent expansion;
- union/subtract/common bounds;
- flattening golden packets;
- client contracts contain no renderer type.

Exit gate:

- one toy terrain grammar emits semantic claims, an ownership tree, and flattened brush packets.

Cut line:

- Do not build a CAD CSG kernel. This tree adjudicates semantic ownership and
  lowering, not arbitrary mesh booleans.

## Phase 4: Node Summaries

Goal: make every LOD subtree renderable without its children.

Tasks:

- [x] Add summary builder for 2D brush tree nodes.
- [x] Track max height/form error.
- [x] Track max material delta placeholder.
- [x] Track coverage and estimated cost.
- [ ] Add parent fallback payload.
- [ ] Add summary serialization.
- [ ] Add debug overlay for summary bounds/error.

Tests:

- parent summary conservatively covers child height contribution;
- child eviction leaves parent renderable;
- summary round trip preserves error bounds;
- summary cost monotonically covers descendants.

Exit gate:

- selected parent summary can render when children are absent.

Cut line:

- Use conservative amplitude/frequency bounds before clever exact summaries.

## Phase 5: Contribution Cache And Selected Cut

Goal: choose a stable hierarchy cut without scoring every leaf.

Tasks:

- [x] Add stable-keyed contribution table.
- [x] Add projected error scorer.
- [x] Add selected-cut builder.
- [x] Add hysteresis fade weights.
- [x] Add budget clamp.
- [x] Add debug channels: score, depth, fade, estimated cost.
- [x] Add explicit resource budget plan for CPU update count, GPU estimated
  packet cost, RAM residency, and SSD request pressure.

Tests:

- [x] score decreases with distance for fixed error;
- [x] score increases with error for fixed distance;
- small camera motion keeps cut stable;
- [x] budget clamp chooses parent summaries;
- fade never changes conservative SDF/height safety fields.

Exit gate:

- toy terrain grammar selects parent at distance and children nearby.

Cut line:

- Keep scoring on CPU until data layout and debug channels are stable.

## Phase 6: Probabilistic Online Updates

Goal: refresh contribution estimates under budget and converge without
pretending an online smoother is ReSTIR.

Research authority:

- ReSTIR DI: candidate generation plus temporal and spatial reservoir reuse for
  real-time direct lighting.
  https://research.nvidia.com/labs/rtr/publication/bitterli2020spatiotemporal/
- ReSTIR GI: path reservoirs for indirect lighting, with final shading fed by a
  selected reservoir sample.
  https://research.nvidia.com/publication/2021-06_restir-gi-path-resampling-real-time-path-tracing
- GRIS: the generalized theory for correlated samples, unknown PDFs, varied
  domains, and shift mappings. This is the fractal/SDF/sensor-fusion door.
  https://research.nvidia.com/labs/rtr/publication/lin2022generalized/
- RTXDI integration docs: initial sampling, temporal reprojection, spatial
  reuse, validation, shift mapping, reservoir buffers, and denoiser guide
  buffers.
  https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/RestirGI.md
  https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/RestirPT.md

Tasks:

- [x] Add pure `ResampledImportanceReservoir<TSample>` core with selected
  sample, target, weight sum, represented candidate count, merge, and
  contribution weight.
- [x] Add estimator state: mean, variance/uncertainty, confidence, sample count, sample age.
- [x] Add stochastic scheduler with deterministic random source.
- [x] Add exploration budget.
- [x] Add live contribution cache that observes only scheduled nodes each frame
  and feeds the next resource plan.
- [ ] Convert contribution-cache refresh into a candidate generator that submits
  weighted candidates to the resampled-importance core.
- [ ] Add fractal SDF probe candidate records: domain key, local frame, bound,
  projected contribution target, source PDF, payload handle, and material delta.
- [ ] Add temporal reuse validation: camera motion, domain ancestry, local-frame
  error, bounds, and disocclusion/material compatibility.
- [ ] Add spatial reuse validation across screen tiles and cube-sphere neighbor
  domains.
- [ ] Feed reservoir confidence, selected target, candidate count, sample age,
  and invalidation reason into TAA guide/history buffers.
- [ ] Add confidence decay and stale uncertainty growth.
- [ ] Add fake contribution probe.
- [ ] Add convergence telemetry.
- [ ] Add debug channels: uncertainty, sample age, update probability,
  reservoir weight sum, selected target, candidate count, reuse validity.

Tests:

- [x] reservoir accepts candidates proportional to target/source PDF;
- [x] reservoir merge preserves represented candidate count and weight sum;
- [x] invalid zero-weight candidates do not corrupt reservoir state;
- deterministic RNG fixture repeats schedule;
- visible nodes keep nonzero update probability;
- near-threshold nodes keep nonzero update probability;
- stale wrong score recovers after observations;
- fixed camera path converges within tolerance;
- temporal reuse rejects mismatched domain ancestry;
- spatial reuse rejects invalid local-frame shifts;
- TAA history confidence drops when reservoir validity changes;
- update cost respects budget.

Exit gate:

- cache updates only a bounded subset per frame while selected cuts converge,
  and the selected detail path is driven by tested reservoir math rather than a
  frozen score table.

Cut line:

- No neural predictor until the ReSTIR/GRIS-shaped reservoir pipeline fails
  with captured telemetry. No consumer-owned temporal cache unless it clearly
  owns raw capture retention rather than resolved evidence.

## Phase 7: Residency Simulation

Goal: prove page/key semantics before real SSD/GPU streaming.

Tasks:

- [x] Add fake payload store with resident/missing states.
- [x] Add child request queue.
- [ ] Add score/cost eviction policy.
- [x] Add parent-summary fallback path.
- [x] Clamp simulated RAM residency and SSD requests through the resource plan.
- [x] Add debug channels: residency, requests, evictions.

Tests:

- [x] missing child renders parent summary;
- [x] high-score missing child enqueues request;
- low-score high-cost payload evicts first;
- [x] frame path never blocks on fake store.

Exit gate:

- selected cut can tolerate missing children without frame stalls.

Cut line:

- Simulate SSD first. Real async file IO waits until page semantics are boring.

## Phase 8: Zyphos Planet Proof

Goal: replace spherical terrain demo path with cube-sphere domain proof.

Tasks:

- [x] Add Zyphos cube-sphere domain root.
- [x] Add first tile grammar: ridges, craters, and material bands.
- [x] Add first flame-style microdetail grammar line inside the Zyphos leaf
  domain.
- [x] Lower selected cut to shaped 2D height/material brush packets.
- [x] Feed planet shader from tile-local cube-face domains for post-hit
  material/detail evaluation.
- [x] Route Zyphos render-plan construction through the resource budget planner.
- [x] Update Zyphos contribution cache every render-plan request while reusing
  compiled brush packets for equivalent selected cuts.
- [x] Feed traced planet geometry from a hard-budgeted top-influence subset of
  selected tile packets.
- [x] Add first face/tile/material influence debug view through render debug
  mode 11.
- [ ] Add LOD/cache debug overlays.
- [ ] Remove or quarantine old spherical noise path as baseline fixture.

Tests:

- face seams render continuously;
- [x] tile keys remain stable under camera movement;
- debug modes show selected depth and cache state;
- headless Zyphos smoke render passes.

Exit gate:

- Zyphos renders terrain from semantic cube-sphere tile grammar.

Cut line:

- Do not start agent-body recursive form here. Planet terrain proves the shared
  address, envelope, summary, and cache spine first.

## Phase 9: Agent/Body Pilot

Goal: prove the same grammar works for object-local detail.

Tasks:

- [ ] Pick one role/body with a stable base SDF.
- [ ] Add object-local domain root.
- [ ] Add 2D material/detail claims before traced form recursion.
- [ ] Add object-local summary tree.
- [ ] Integrate selected cut with SDF proxy packet.
- [ ] Add debug channels for body detail score and SDF cost.

Tests:

- silhouette stays coherent across LOD;
- material/detail fades by contribution;
- SDF step budget remains bounded;
- no scene-global mega-SDF path appears.

Exit gate:

- one body uses semantic detail grammar without violating proxy bounds.

Cut line:

- If form recursion destabilizes tracing, keep recursive detail in material
  payloads and leave traced form coarse.

## Phase 10: Learned Scoring Research Gate

Goal: only add learned scoring after the estimator produces data.

Tasks:

- [ ] Add probe dataset writer.
- [ ] Capture camera paths, predicted scores, observed deltas, and costs.
- [ ] Analyze heuristic error offline.
- [ ] Try calibrated regression or tiny predictor.
- [ ] Add A/B mode against heuristic estimator.

Tests:

- held-out camera path comparison;
- learned mode beats heuristic fidelity/cost clearly;
- conservative summaries still own safety;
- fallback heuristic remains available.

Exit gate:

- learned scorer improves measured fidelity/cost without removing deterministic safety.

Cut line:

- If it does not beat the estimator clearly, delete it. No ornamental oracle.

## First Work Packet

Start here:

1. Add `Aquarium.Engine.Fractal` and its test project.
2. Add cube-face/tile key types.
3. Add identity and tangent projection candidates.
4. Add area distortion sampler and face-edge tests.
5. Add a simple projection report fixture.

Definition of done:

- `dotnet build Aquarium.Engine.sln`
- new fractal tests pass;
- projection report is checked into test output or docs;
- `state/evidence.jsonl` records the chosen first projection default and why.
