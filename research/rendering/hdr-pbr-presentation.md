# HDR/PBR Presentation Research

Aquarium needs an HDR presentation path before the SDF field renderer grows real
lighting and participating media. The target is not "more bloom." The target is a
scene-linear renderer with believable dynamic range, camera exposure, optical
spill, and a display transform that preserves contrast without boiling the frame
in arcade syrup.

## Sources

- [ACES System overview](https://docs.acescentral.com/background/overview/)
- [Epic: How Epic Games is handling Auto Exposure in 4.25](https://www.unrealengine.com/tech-blog/how-epic-games-is-handling-auto-exposure-in-4-25?lang=en-US)
- [Unreal Engine bloom documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/bloom-in-unreal-engine?application_version=5.6)
- [Jorge Jimenez: Next Generation Post Processing in Call of Duty: Advanced Warfare](https://www.iryoku.com/next-generation-post-processing-in-call-of-duty-advanced-warfare/)
- [John Hable: Filmic Tonemapping Operators](https://filmicworlds.com/blog/filmic-tonemapping-operators/)
- [John Hable: Filmic Tonemapping with Piecewise Power Curves](https://filmicworlds.com/blog/filmic-tonemapping-with-piecewise-power-curves/)
- [Hable: Uncharted 2 HDR Lighting, SIGGRAPH 2010](https://advances.realtimerendering.com/s2010/Hable-Uncharted2%28SIGGRAPH%202010%20Advanced%20RealTime%20Rendering%20Course%29.pdf)
- [Lagarde/de Rousiers: Moving Frostbite to Physically Based Rendering 3.0](https://cgvr.cs.uni-bremen.de/teaching/cg_literatur/Moving%20Frostbite%20to%20Physically%20Based%20Rendering%203.0%2C%202014%2C%20Sebastien%20Lagarde.pdf)

## Distillation

Modern HDR presentation is a chain, not a knob:

1. Render lighting and emissive terms in scene-linear HDR.
2. Apply exposure in the HDR domain, preferably from a physical-camera or EV100
   model even if Aquarium initially uses manual exposure.
3. Generate bloom/glare from exposed HDR color before tonemapping.
4. Composite bloom back into exposed scene-linear color with low intensity and
   broad support.
5. Apply a display transform or filmic tonemap with toe and shoulder.
6. Convert to the swapchain/display encoding only at the end.

The important production lesson is that bloom is not a thresholded special
effect for "pixels above 1." In a high-dynamic-range PBR scene, broad low-gain
scatter can apply to the whole image while only genuinely bright energy becomes
visibly luminous. Threshold bloom is the cheap costume version; it cuts out the
low-level optical veil and encourages artists to overdrive emissive values until
the frame looks diseased.

ACES-style and filmic transforms are useful because they give highlights a
shoulder and shadows a toe. Aquarium's current tiny ACES approximation is a
reasonable placeholder, but it is not a color-management system, not a look
authoring surface, and not enough to make the scene feel HDR by itself.

## Aquarium Gate Plan

### Gate HDR-A: Explicit Exposure

Add an exposure scalar in `AquariumFrame`, initially manual and persisted through
CultCache later.

Start simple:

- `exposedColor = sceneLinearColor * exposure`
- default exposure chosen so Self is bright but not pinned white across its
  whole disc
- debug mode for log luminance or EV bands

Avoid auto exposure until the scene has enough stable luminance content. A
volumetric/SDF scene with huge black regions and a giant Self emitter will make
naive histogram adaptation twitch like a bad witness.

### Gate HDR-B: Bloom Pyramid

Add a pre-tonemap bloom pass:

- source: exposed HDR scene color
- no hard threshold at first
- downsample pyramid with firefly-safe weighted averages
- upsample with small tent filters and per-level radius
- composite with low intensity into exposed scene color before tonemap

This follows the Jimenez production shape: pyramidal bloom/veil for stability
and robustness, not a single huge blur taped over bright pixels.

### Gate HDR-C: Display Transform

Keep the current ACES approximation only as `DisplayTransform.AcesFit`.

Add an explicit display-transform function boundary:

- ACES fit for default
- Hable/filmic curve as an alternate debug/look path
- later: LUT-based transform if Aquarium needs an authored look

Do not bury exposure, bloom, and tonemapping in one shader lump. The whole point
is to keep the light transport legible before volumetrics arrive and start
lying convincingly.

### Gate HDR-D: HDR Debug Views

Add debug views before tuning:

- scene-linear luminance
- exposed luminance
- bloom contribution
- tonemapped final
- over-range / clipped highlight mask

If Self, planets, Grid, and later clouds cannot be inspected in each stage, the
pipeline will become a little shrine to superstition. Burn the shrine early.

## Rules For Aquarium

- Bloom happens before tonemapping.
- Bloom spreads existing energy; it does not invent light.
- Prefer broad low-intensity bloom/veil over hard-threshold glow.
- Keep overlay text out of HDR and bloom unless it becomes a diegetic scene
  surface.
- Do not tune emissive values against SDR final output alone. Inspect linear and
  exposed luminance.
- Volumetrics must feed and be fed by the same exposure/display path as solids.
- If the frame needs "more HDR," first check exposure, display shoulder, and
  source radiance before turning up bloom.

## Immediate Recommendation

Before the field renderer:

1. Add explicit exposure constants.
2. Split resolve into exposed HDR color and final display transform.
3. Add a bloom pyramid between exposure and tonemap.
4. Add debug views for luminance and bloom contribution.

This gives SDF solids/clouds a real presentation pipe to land in. Otherwise the
field renderer will arrive carrying gorgeous radiance values into a cramped SDR
mail slot, and everyone will pretend the answer is blur radius. It is not.

## Aquarium Implementation Note

The first implementation now follows this shape:

- fixed manual exposure in the frame constants
- exposed scene-linear HDR source for bloom
- three-level bloom pyramid at half, quarter, and eighth resolution
- firefly-safe downsampling
- separable horizontal/vertical blur per level
- low-gain bloom/veil contribution before the ACES fit
- debug mode `7` for bloom contribution and mode `8` for exposed luminance

The remaining missing piece is a persisted/inspectable exposure control and,
later, a proper authored display-transform boundary.
