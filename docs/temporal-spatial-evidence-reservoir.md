# Temporal Spatial Evidence Reservoir

## Objective

Aquarium owns the shared temporal evidence machine for two customers:

- fractal rendering, where SDF/detail/path candidates need bounded reuse across
  pixels, frames, and nested domains;
- Mimir/LocalCast sensor fusion, where camera and microphone features need a
  delayed coherence window before the resolved field is rendered.

The old stable-key accumulator was useful, but it was not ReSTIR. The live
architecture now splits the problem into a small resampled-importance core, a
spatial evidence track layer, typed lowerings, renderer passes, and TAA history.

## Research Spine

- NVIDIA ReSTIR DI repeatedly resamples candidate light samples, then applies
  spatial and temporal resampling to share useful samples across nearby pixels
  and frames. The paper reports equal-error speedups of 6-60x, with biased
  variants reaching 35-65x in its tested direct-lighting workloads.
  Source: https://research.nvidia.com/labs/rtr/publication/bitterli2020spatiotemporal/
- ReSTIR GI applies the same screen-space spatiotemporal reuse idea to
  multi-bounce indirect paths and reports 9.3x-166x MSE improvement at one
  sample per pixel in tested scenes.
  Source: https://research.nvidia.com/publication/2021-06_restir-gi-path-resampling-real-time-path-tracing
- GRIS generalizes RIS/ReSTIR to correlated samples, unknown PDFs, varied
  domains, and shift mappings. That is the important part for Aquarium:
  fractal domains and sensor-fusion samples are not all light samples.
  Source: https://research.nvidia.com/labs/rtr/publication/lin2022generalized/
- RTXDI's integration docs make the pass boundary concrete: acquire initial
  samples, store reservoir data, validate temporal reuse through reprojection,
  validate spatial reuse against nearby surfaces, shift candidate paths between
  domains, then shade/denoise from the final reservoir.
  Sources:
  https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/RestirGI.md
  https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/RestirPT.md

## Pipeline Map

```text
producer observations
-> candidate generator
-> target evaluator
-> ResampledImportanceReservoir<TSample>
-> temporal reuse pass
-> spatial/domain reuse pass
-> validation + shift mapping
-> TemporalSpatialEvidenceReservoir track layer when stable fields are needed
-> TemporalSpatialEvidenceLowering
-> backend packet stream
-> renderer/fusion passes
-> TAA guide/history buffers
```

## Ownership

`ResampledImportanceReservoir<TSample>` owns RIS/ReSTIR-style candidate math:

- one selected representative sample;
- summed importance weight of accepted candidates;
- number of source candidates represented by the reservoir;
- selected sample target value;
- final contribution weight `weightSum / (candidateCount * selectedTarget)`.

Candidate generators own proposal distributions. A fractal renderer may propose
SDF probe candidates from projected error, node bounds, blue-noise screen tiles,
or resident children. Mimir may propose visual/audio feature candidates from
sensor confidence and calibration state. The reservoir does not know these
domains; it only receives target and source-PDF values.

Reuse passes own validity and shift mapping. A sample may be reused only when
the target domain can explain it. For pixels this means depth/normal/material
compatibility, motion vectors, conservative visibility, and disocclusion tests.
For fractal domains it means matching domain ancestry, bounded local-frame
error, resident payload compatibility, and conservative SDF bounds. For sensor
fusion it means time delay, calibration confidence, modality agreement, and
feature reprojection error.

`TemporalSpatialEvidenceReservoir` owns stable resolved spatial tracks after
candidate selection when the output is a field with persistent identity:

- stable key identity;
- accumulation window and presentation delay;
- confidence-weighted smoothing;
- velocity prediction;
- history weight from confidence and sample age;
- expiry;
- max-track eviction by confidence and age.

`TemporalSpatialEvidenceLowering` owns packet conversion. Consumers do not pack
payload vectors by private convention when a lowering helper exists.

TAA owns pixel history validation and temporal guide buffers. It may consume
reservoir confidence, sample age, domain id, motion, and temporal-detail lanes,
but it does not own producer identity or stable spatial evidence.

## Invariants

- Reservoir math is a pure, testable core before it becomes HLSL.
- A reservoir is not a dictionary of tracks. A track layer may use reservoirs,
  but it does not replace candidate resampling.
- Reuse is invalid until a pass proves the shift/validation contract for the
  source and target domains.
- Conservative bounds remain the safety authority. Learned or stochastic
  priority may decide what to refresh first; it must not replace SDF bounds,
  visibility bounds, or calibration bounds.
- CPU, GPU, RAM, and SSD budgets are inputs to candidate generation and
  residency. They are not hidden side effects of renderer convenience code.
- Consumer repos do not grow parallel stable-key temporal caches.

## Current State

Built:

- `ResampledImportanceCandidate<TSample>` stores a sample, target value, source
  PDF, and represented candidate count.
- `ResampledImportanceReservoir<TSample>` accepts candidates by weight
  proportional to `target / sourcePdf`, merges reservoirs, preserves represented
  candidate count, and exposes the RIS contribution weight.
- `TemporalSpatialEvidenceReservoir` remains the stable track layer used by
  temporal Gaussian fields and LocalCast fallback GPU fusion.
- `FractalContributionCache` remains the node-summary LOD estimator, and now
  frames scheduled update nodes as weighted reservoir candidates with a
  per-frame reservoir snapshot for debug and tests. It is the candidate source,
  not the full temporal/spatial reuse pass.
- `FractalProbeSample` is the first typed fractal SDF/detail sample shape for
  the ReSTIR/GRIS path. It carries domain key, node key, local center, bound
  radius, target contribution, source PDF, material delta, and payload handle.
  `FractalProbeReuseValidator` currently proves domain lineage and local-shift
  compatibility; renderer-facing camera, disocclusion, material, and visibility
  checks still need to be added before temporal/spatial reuse is complete.

Not built yet:

- GPU reservoir buffers;
- camera/disocclusion/material validation for fractal/SDF reservoirs;
- spatial neighbor reuse across screen tiles and cube-sphere neighbor domains;
- GRIS-style domain shift mappings for nested `.aquageo` domains;
- expanded TAA guide-buffer storage for previous-frame reservoir confidence,
  sample age, domain validity, and invalidation reason;
- SSD/RAM residency queues driven by reservoir contribution estimates.

## Implementation Roadmap

1. Keep the CPU reservoir core pure and exhaustive under unit tests.
2. Add typed reservoir samples for fractal SDF probes: domain key, local frame,
   bound radius, target contribution, source PDF, payload handle, and material
   delta.
3. Turn `FractalContributionCache` into a candidate generator that refreshes
   nodes under CPU budget and submits candidates to the reservoir core.
4. Add temporal reuse for fractal probes using camera motion, domain ancestry,
   local-frame error, and conservative bounds.
5. Add spatial reuse across screen tiles and quadtree neighbors.
6. Lower selected reservoirs into GPU brush/SDF packets and expose debug views
   for weight sum, selected target, candidate count, confidence, age, and
   invalidation reason.
7. Weave reservoir confidence and temporal detail into TAA guide buffers so the
   history filter can distinguish stable reused evidence from fresh stochastic
   noise. The first pass uses current scene-control.w for reservoir confidence
   and keeps history-control.w as age; full previous-frame reservoir validity
   still needs an expanded guide layout.
8. Add Mimir-facing candidate adapters only after the fractal path proves the
   contract: modality features are candidates, not a second reservoir system.
9. Port the pure core to HLSL and add CPU/GPU parity fixtures.
10. Add residency scheduling: hot selected reservoirs keep GPU packets resident,
    warm reservoirs keep RAM payloads, cold reservoirs fall to SSD handles.

## Cut Line

If a new subsystem stores temporal candidates, it must state whether it owns raw
producer retention, resampled candidate selection, stable resolved tracks,
packet lowering, or TAA history. If it cannot name that authority, it is not an
architecture. It is a decorative leak.
