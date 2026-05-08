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
- volumetric accumulation terms
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

This is sufficient for camera motion and stochastic Grid coverage. It is not the
final answer for animated bodies, field animation, or volumetrics.

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
- Do not treat volumetrics as ordinary opaque surface history.
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

Still missing:

- separate field ids once the SDF field registry exists
- reactive/coverage masks
- richer disocclusion classification
- a general velocity buffer for non-rigid fields, deformation, and future
  volumetrics

### Gate 3: Volumetric Temporal Contract

Current Gate 3A implementation:

- scene pass writes a temporal-control target separate from color/travel and
  field/normal metadata
- temporal control currently stores:

```text
x = reactive strength
y = stochastic/composition coverage
z = medium opacity placeholder
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
- medium-opacity discontinuity reduces history, even though real volumetrics do
  not write the channel yet
- current temporal control is written into the history set for the next frame

Current Gate 3C implementation:

- temporal-control history `w` stores accepted history age in frames
- validation grows age when reprojected history survives the travel, field,
  normal, color, coverage, and medium-opacity checks
- history authority ramps with age, so newly accepted history can contribute but
  does not immediately get the same weight as stable history
- age resets to zero on validation failure

This is intentionally still a surface/stochastic coverage contract. It prepares
the resolver for volumetrics without lying that a single hit depth describes a
whole participating medium.

Still missing:

- local volume history separate from opaque/surface history
- accumulated extinction/transmittance history
- reactive reset for lighting and density changes
- dominant depth or moment pair for volume reprojection
- separate history policy for opaque surfaces, stochastic surfaces, and media

### Gate 4: TSR Features Worth Stealing In Spirit

- flicker analysis for high-frequency linework and stochastic coverage
- history resurrection for reappearing stable details
- debug visualizations for rejection, clamp, velocity, travel, and history age
