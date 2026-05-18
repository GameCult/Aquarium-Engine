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
MessagePack document, maps every point claim into `TemporalGaussianObservation`
rows, and lets the accumulator own smoothing, history, and presentation delay.
It does not downsample the document in client space.

## Invariants

- Field accumulation happens in world space before pixel history. TAA is the
  resolver, not the owner of sensor identity.
- Stable keys belong to the producer/accumulator boundary; shader packets are
  backend output and do not invent identity.
- The compact support kernel has a finite bound. Renderer cost must scale from
  declared bounds, not from infinite translucent fog.
- Client code may construct observations or a field, but Aquarium owns packet
  layout, root binding, shader evaluation, and temporal-control metadata.
- JSON is not a renderer boundary. CultCache/CultNet producers should lower into
  typed contract rows before Aquarium sees the data.

## Cut Line

This cut deliberately claims million-slot ingestion, not a finished million-splat
renderer architecture. The live D3D12 path can draw up to 1,048,576 temporal
Gaussians through instanced proxy quads and only uploads the active packet span.
The next scaling cut belongs to the Aquarium renderer: selected-cut residency,
tiled/bin dispatch, GPU accumulation, and clustered visibility. Do not push that
scheduler into LocalCastBridge.

The first shader pass also uses camera-facing proxy planes to evaluate each
kernel. True ray-integrated volume compositing, Gaussian depth sorting, and
per-Gaussian previous-center reprojection remain the next renderer cuts.
