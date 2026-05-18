# Temporal SDF Gaussian Field

## Objective

Aquarium owns the renderer feature for live volumetric point-cloud/splat input:
clients provide stable world-space Gaussian observations, and the engine turns
them into a buffered, reprojectable, D3D12-rendered field. LocalCastBridge can
then feed sensor-fusion output into Aquarium as a normal scene contract instead
of smuggling renderer policy through client code.

## Current Mechanism

The client-facing contract is `AquariumSceneState.TemporalGaussianField`.
Each `AquariumTemporalSdfGaussian` carries stable identity, current and previous
center, velocity, oriented radii, color/opacity, confidence, history weight,
compact-kernel controls, and field id.

`Aquarium.Engine.Fractal.Temporal.TemporalGaussianAccumulator` consumes
`TemporalGaussianObservation` rows and owns the live buffering policy:

- stable keys identify tracks across camera/sensor frames
- presentation delay lets late sensor data converge before rendering
- accumulation window expires old tracks
- smoothed velocity predicts the presented center
- history weight is confidence scaled by age inside the window

The D3D12 renderer lowers the field into `D3D12TemporalGaussianPacket`, uploads
the active packet span into a 1,048,576-entry structured buffer, and renders an
instanced proxy-quad pass from
`D3D12TemporalGaussian.hlsl`. The pixel shader evaluates a compact anisotropic
Gaussian kernel in world space, writes HDR scene color/travel, field metadata,
normal, and temporal-control coverage so the existing resolve sees the field as
diegetic scene content.

`Aquarium.LocalCast` is the first concrete live client for this contract. It
reads `localcast.visual.render_frame` from LocalCastBridge's typed CultCache
MessagePack document. That point/seed route is now explicitly a fallback and
debug harness: it proves the million-slot temporal Gaussian draw and keeps OBS
fed while the live capture path moves into Aquarium. The production ownership
line is `AquariumGpuSensorFrame`: camera and Leap inputs arrive as calibrated
sensor records plus shared GPU texture handles, the D3D12 backend owns their
metadata buffers, and fusion kernels lower those GPU-resident inputs into the
temporal Gaussian buffer consumed by the SDF Gaussian draw.

LocalCast GPU fusion also has its own live-history accumulator. New frames
update stable-key tracks, stale tracks expire after a bounded horizon, and the
whole retained set is sent to the GPU seed buffer every frame. The default
history horizon is 18 seconds and may be overridden with
`LOCALCAST_GPU_HISTORY_SECONDS` from 1 to 120 seconds. That is the reconstruction
buffer: long enough to harvest samples, bounded enough to remain a machine
instead of a scrapbook with a power cord.

## Invariants

- Field accumulation happens in world space before pixel history. TAA is the
  resolver, not the owner of sensor identity.
- Stable keys belong to the producer/accumulator boundary; shader packets are
  backend output and do not invent identity.
- The compact support kernel has a finite bound. Renderer cost must scale from
  declared bounds, not from infinite translucent fog.
- Client code may construct observations or a field for diagnostics, but
  Aquarium owns live sensor texture import, packet layout, root binding, shader
  evaluation, fusion, and temporal-control metadata.
- JSON is not a renderer boundary. CultCache/CultNet producers should lower into
  typed contract rows before Aquarium sees the data.

## Cut Line

This cut deliberately claims million-slot ingestion, not a finished million-splat
renderer architecture. The live D3D12 path can draw up to 1,048,576 temporal
Gaussians through instanced proxy quads, only uploads the active seed/packet
span, and now owns the GPU lowering step. Python/LocalCastBridge may still
produce calibration artifacts and reference captures, but it must not own
per-frame dense stereo, feature tracking, or reconstruction compute. The next
scaling cut belongs to the Aquarium renderer: shared texture import, packed
camera planes, Leap packed-map channel extraction, selected-cut residency,
tiled/bin dispatch, GPU accumulation, and clustered visibility.

## GPU Fusion Spine

The active GPU boundary is deliberately narrow:

```text
LocalCast calibration/device metadata
-> AquariumGpuSensorFrame { calibrated cameras + shared GPU textures }
-> D3D12 sensor metadata buffers + imported texture SRVs
-> D3D12 LocalCast fusion compute shader
-> RWStructuredBuffer<TemporalGaussian>
-> instanced SDF Gaussian draw
-> TAA/resolve
```

Ownership:

- `AquariumGpuSensorFrame` is the live renderer contract for GPU-owned fusion
  input.
- `AquariumGpuFusionField` remains a temporary fallback/debug contract for
  already-derived point claims.
- `LocalCastGpuFusionMapper` converts typed LocalCast point claims into compact
  seeds only for that fallback path.
- `LocalCastGpuFusionAccumulator` owns the bounded history buffer for fallback
  stable seeds before GPU lowering.
- `D3D12LocalCastFusion.hlsl` owns the first compute lowering pass.
- `D3D12Renderer` owns GPU sensor camera metadata storage, UAV-capable temporal
  Gaussian storage, dispatch, and the transition back to shader-resource state
  for the draw.

Current Aquarium cut: the contract, D3D12 camera metadata buffer, external
texture importer, and sensor SRV table exist. A producer may provide either a
duplicated shared handle or a named shared handle for each camera/Leap plane.
When sensor textures are present, Aquarium can dispatch fusion without fallback
seeds and write RGB-derived Gaussian samples into the temporal buffer. Next cut:
replace the first-pass per-texture sampling with calibrated stereo/flow/feature
kernels, Leap packed-map channel extraction, and confidence-weighted surface
correspondence.

The first shader pass also uses camera-facing proxy planes to evaluate each
kernel. True ray-integrated volume compositing, Gaussian depth sorting, and
per-Gaussian previous-center reprojection remain the next renderer cuts.
