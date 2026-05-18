# TSR-Inspired TAA Spec

Aquarium needs an owned temporal resolver, not a copied implementation. The
target is inspired by public TSR behavior: stable subpixel detail, aggressive
history validation, visible debug terms, and special handling for pixels whose
appearance is not fully described by motion vectors.

## Inputs

The resolver owns these signals:

- current HDR scene color
- current ray travel
- previous HDR history color
- previous ray travel
- current camera/Grid constants
- previous camera/Grid constants
- current and previous projection jitter
- stochastic coverage from diegetic Grid surfaces

Later gates add:

- field id
- normal
- explicit velocity
- reactive mask
- transparency/composition mask
- resurrection history

## Current Pass Contract

The scene pass renders into an HDR texture:

```text
rgb = linear scene color
a   = ray travel in world units
```

The pass does not tonemap. Tonemapping belongs after temporal resolve.

## Reprojection

For the first implementation, motion is camera-derived:

1. Reconstruct the current world hit from current camera, current jittered ray,
   and current travel.
2. Project that world hit into the previous camera basis.
3. Remove previous jitter to find the previous history UV.
4. Sample previous history if the UV is valid.
5. Compare previous history travel against the reprojected point's expected
   distance from the previous camera, not against current-frame travel.

This is sufficient for camera motion and stochastic Grid coverage. It is not the
final answer for animated bodies or field animation.

## Rejection

Reject or weaken history when:

- previous UV is outside the frame
- travel differs too much
- current or previous travel is invalid
- current neighborhood color bounds do not contain the reprojected history

The first pass uses a 3x3 neighborhood min/max clamp and travel-difference
weighting. Later passes add field/material ids, normals, disocclusion masks, and
reactive masks.

## Resolve

The resolver outputs both:

- tonemapped final backbuffer color
- linear HDR history for the next frame

Current blend target:

```text
resolved = lerp(current, clamped_history, history_weight)
```

History weight starts conservative. Stochastic Grid coverage needs accumulation,
but ghosting is worse than shimmer while the renderer lacks full velocity,
material id, and reactive masks. The resolver should earn trust before taking
more history.

## Non-Negotiables

- Do not use Playdead-style TAA as the final model; it ghosts too easily for the
  scene we are building.
- Do not vendor Unreal TSR code. Study behavior and build a clean-room resolver.
- Do not hide missing renderer signals with larger blend weights.
- Do not make the Grid a final-image HUD layer; it is diegetic scene UI and must
  respect nearer solids.

## Implementation Gates

### Gate 1: Owned Temporal Pass

- HDR scene target
- history ping-pong
- camera jitter
- current/previous camera reprojection
- travel rejection
- 3x3 color clamp
- final tonemap after resolve

### Gate 2: Renderer Signals

- material/field id buffer
- normal buffer
- explicit velocity for animated SDF bodies
- coverage/reactive masks for stochastic Grid and future particles

Current Gate 2A implementation:

- scene metadata target stores stable field id plus surface normal
- history metadata ping-pongs alongside color/travel history
- field id mismatch rejects history
- normal mismatch weakens/rejects history
- Grid stochastic surfaces write Grid travel/normal even when the current
  dither sample does not draw color, so the temporal resolver accumulates the
  diegetic surface rather than reprojecting from the solid behind it

Current Gate 2B implementation:

- Self, Grid, and each orbiting planet have distinct field ids
- orbiting planet hits map their current world hit back to the planet's previous
  center before camera reprojection
- this gives analytic SDF bodies object motion without a full velocity buffer
  yet

Current Gate 2C implementation:

- `AquariumSceneState.TemporalGaussianField` carries world-space temporal SDF
  Gaussian splats with stable keys, current/previous center, velocity,
  confidence, history weight, field id, and compact-kernel controls
- `TemporalGaussianAccumulator` buffers sensor observations in world space before
  rendering, using a presentation delay plus accumulation window instead of
  asking final-pixel TAA to discover identity after projection
- D3D12 uploads the field into a structured buffer and renders it through
  `D3D12TemporalGaussian.hlsl`, writing color/travel, field metadata, normals,
  and temporal-control coverage into the existing scene/resolve path

Still missing:

- separate field ids once the SDF field registry exists
- reactive/coverage masks
- richer disocclusion classification
- a general velocity buffer for non-rigid fields and deformation
- per-Gaussian previous-center reprojection in the resolve pass

### Gate 3: Stochastic Event Control

Current Gate 3A implementation:

- scene pass writes a temporal-control target separate from color/travel and
  field/normal metadata
- temporal control currently stores:

```text
x = reactive strength
y = stochastic/composition coverage
z = reserved
w = reserved
```

- Grid coverage feeds the coverage channel
- low-coverage Grid samples raise reactive strength and reduce history weight
- the resolve combines reactive and coverage weights with travel, field, normal,
  and neighborhood-color validation

Current Gate 3B implementation:

- temporal-control history ping-pongs alongside color/travel and field/normal
  history
- resolve samples previous-frame temporal control at the reprojected UV
- coverage discontinuity reduces history
- current temporal control is written into the history set for the next frame

Current Gate 3C implementation:

- temporal-control history `w` stores accepted history age in frames
- validation grows age when reprojected history survives the travel, field,
  normal, color, and coverage checks
- history authority ramps with age, so newly accepted history can contribute but
  does not immediately get the same weight as stable history
- age resets to zero on validation failure

Current Gate 3D implementation:

- travel validation uses the reprojected previous-world position's distance from
  the previous camera
- raw current-frame travel is no longer compared directly to previous history
  travel, because camera motion changes the ray distance even when the world hit
  is stable

Current Gate 3E implementation:

- stochastic Grid history bypasses opaque-surface neighborhood color clamping
  and current-frame color-delta rejection
- Grid validation still uses field id, travel, normal, coverage continuity,
  reactive weight, and history age
- this prevents a current dither miss from declaring the true neighborhood to be
  background and crushing the very history that should accumulate stochastic
  coverage

Current Gate 3F implementation:

- stochastic Grid history uses a high-retention blend policy distinct from
  opaque surfaces
- Grid history weight no longer scales down with current coverage or reactive
  strength; low coverage is exactly where temporal accumulation is needed
- opaque surfaces keep the stricter color/reactive validation path
- debug mode `2` should now show a smoother history signal than raw scene mode
  `1` when the Grid is stable

Current Gate 3G implementation:

- identity/control buffers are point-loaded, not linearly sampled
- field id equality is exact after point-loading, so Grid/Self/planet boundaries
  hard-reject mismatched history
- Grid history color is sampled with the same nearest pixel used for metadata so
  bright foreground silhouettes do not bilinearly bleed into background Grid
  history
- Grid max history weight is reduced from the too-soft `0.975` to `0.94`

Current Gate 3H implementation:

- Grid temporal `coverage` is now line/contour/field-line support, not broad
  overlay alpha
- broad weather/field tint can still render through stochastic coverage, but it
  does not keep history age alive across the whole Grid surface
- Grid history is gated by current support, so stale stochastic hits cannot
  billow away from the analytic line support
- Grid max history weight is reduced again to `0.90` with a lower fresh-history
  scale for crisper line response

Current Gate 3I implementation:

- Grid resolve reconstructs the analytic Grid overlay at the current world hit
  and uses premultiplied overlay color as the current color estimate
- the scene pass may still use stochastic coverage, but the temporal resolver
  no longer treats the current binary hit/miss sample as the true radiance
- debug mode `1` can remain noisy because it shows the raw stochastic scene;
  mode `0` should now use the analytic Grid estimate plus validated history

Current Gate 3J implementation:

- debug mode `1` is restored to raw current scene color, before Grid analytic
  reconstruction
- Grid final color no longer blends previous color history; it uses the analytic
  current Grid estimate and writes that estimate into history
- history/age/weight debug modes remain useful diagnostics, but stale Grid color
  history no longer drives the final visible Grid

Current Gate 3L/3M implementation:

- projection jitter uses a small Halton sequence, currently scaled below half a
  pixel so the resolve gets sample diversity without visible whole-frame wobble
- current and previous jitter are both uploaded through frame constants; previous
  history projection removes the previous jitter before sampling history
- final presentation blends validated temporal color history in mode `0`
- SDF object highlights have a narrow hot-current path: when the current SDF
  sample is much brighter than valid reprojected history, color rejection is
  relaxed so history can damp transient specular fireflies while travel, field,
  normal, coverage, and coverage-continuity validation still apply
- bloom presentation is attenuated when the resolved scene luminance is much
  lower than the raw current scene luminance, so current-frame fireflies do not
  keep blooming after temporal history has damped the underlying scene color
- bloom prefilter clamps unsupported single-pixel HDR spikes against local
  neighborhood luminance before downsample/blur, so one-frame fireflies do not
  spread into neighboring pixels before the temporal resolve sees them
- modes `2` through `4` show the sampled history, accepted history age, and
  history weight

Still missing:

- reactive reset for lighting and density changes
- separate history policy for opaque surfaces and stochastic surfaces

### Gate 4: TSR Features Worth Stealing In Spirit

- flicker analysis for high-frequency linework and stochastic coverage
- history resurrection for reappearing stable details
- debug visualizations for rejection, clamp, velocity, travel, and history age

Current debug view controls:

- `F1` cycles temporal debug modes in the running window
- `0` final resolved color
- `1` raw current scene color
- `2` reprojected history color used by the resolver
- `3` accepted history age
- `4` history weight
- `5` current temporal control: reactive in red and Grid support/coverage in
  green
- `6` current field identity palette for Grid, Self, and planets
- `7` bloom/veil contribution after exposure and before tonemapping
- `8` exposed luminance bands
- `--render-debug` and `AQUARIUM_RENDER_DEBUG_MODE` set the startup mode
