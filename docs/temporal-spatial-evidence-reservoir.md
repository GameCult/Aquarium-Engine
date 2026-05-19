# Temporal Spatial Evidence Reservoir

## Objective

Aquarium owns resolved temporal spatial evidence. Producers may capture raw
camera frames, audio blocks, point claims, SDF probes, or calibration events, but
the shared stable-key history, delayed presentation, smoothing, confidence
weighting, expiry, and backend lowering belong to the Aquarium reservoir stack.

This serves both current customers:

- fractal rendering: SDF probes and selected-cut packets need bounded,
  confidence-weighted detail that converges while the camera moves;
- Mimir/LocalCast sensor fusion: cameras and microphones need a delayed
  coherence window before the resolved field is rendered.

## Pipeline

```text
raw producer samples
-> typed producer adapter
-> TemporalSpatialEvidenceObservation
-> TemporalSpatialEvidenceReservoir
-> TemporalSpatialEvidenceSample snapshot
-> TemporalSpatialEvidenceLowering
-> backend packet stream
-> D3D12 render/fusion/TAA
```

## Ownership

Raw sample reservoirs own capture-time retention only. LocalCastBridge's native
rolling reservoir is allowed to keep time-ordered camera/audio/sample handles,
typed views, latest-for-sensor lookup, and provenance filtering. It must not own
resolved spatial confidence, smoothed motion, render packet lowering, or TAA
history.

`TemporalSpatialEvidenceReservoir` owns stable spatial tracks:

- stable key identity;
- accumulation window and presentation delay;
- confidence-weighted smoothing;
- velocity prediction;
- history weight from confidence and sample age;
- expiry;
- max-track eviction by confidence and age.

`TemporalSpatialEvidenceLowering` owns packet conversions. Current lowerings:

- `TemporalGaussianObservation` -> evidence observation;
- evidence sample -> `AquariumTemporalSdfGaussian`;
- `AquariumGpuFusionSeed` -> evidence observation;
- evidence sample -> `AquariumGpuFusionSeed`.

Adapters own domain-specific interpretation before the reservoir. For example,
LocalCast may decide how a visual point maps to radii, color, falloff, and
shape power, but it then hands the result to the shared lowering path.

Renderer passes own backend packet layout and GPU execution. TAA owns pixel
history validation, not producer identity or stable-key accumulation.

## Invariants

- Stable-key spatial history has one owner: `TemporalSpatialEvidenceReservoir`.
- Consumers do not privately implement track dictionaries, smoothing, expiry, or
  max-count eviction for resolved spatial evidence.
- Payload vectors are not ad hoc folklore. If a backend needs packed payload
  fields, add or use a named lowering helper.
- Raw retention and resolved evidence are separate layers. Do not collapse
  LocalCastBridge's raw rolling reservoir into Aquarium's resolved evidence
  reservoir.
- Conservative bounds remain safety authority. Learned scoring or stochastic
  sampling may prioritize work, but it must not replace bounds used for render
  correctness.

## Current Consumers

`TemporalGaussianAccumulator` is a compatibility facade. It maps Gaussian
observations through `TemporalSpatialEvidenceLowering`, stores them in the
shared reservoir, and lowers snapshots back to `AquariumTemporalSdfGaussian`.

`LocalCastGpuFusionAccumulator` is also a facade. It maps LocalCast fallback
GPU fusion seeds into shared evidence observations and lowers snapshots back to
`AquariumGpuFusionSeed`.

`FractalContributionCache` is adjacent, not yet unified: it owns selected-cut
contribution estimates for authored fractal nodes. Future SDF probe evidence
should enter the shared reservoir when probe results become spatial samples;
node-summary scoring remains a separate LOD planning authority.

## Cut Line

Do not build a second temporal spatial evidence cache in a client repo. If a
consumer needs different sample semantics, add a producer adapter and a named
lowering helper. If it needs different retention semantics, extend the shared
reservoir only after stating the invariant the new behavior protects.
