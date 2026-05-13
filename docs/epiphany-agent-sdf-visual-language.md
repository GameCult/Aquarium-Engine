# Epiphany Agent SDF Visual Language

This document defines the first renderer-facing visual grammar for Epiphany's
standing organs in Aquarium.

The goal is not eight cute mascots with arbitrary paint. Each body should expose
what its organ does, how it is feeling, and what Epiphany surface currently owns
its visible state. Identity comes from durable memory. Motion comes from
heartbeat physiology. Posture comes from operator-safe surfaces. Affordance
comes from intents, receipts, and review state.

## Shared Inputs

Aquarium should derive an `AgentVisualState` from the CultNet-discoverable
Epiphany documents instead of hard-coding private app folklore.

Stable identity and temperament:

- `epiphany.agent_memory`: `roleId`, `agent.agent_id`,
  `agent.identity.name`, `agent.identity.roles`, `agent.canonical_state`,
  goals, memories, and perceived overlays.

Physiology and animation clock:

- `epiphany.agent_heartbeat`: `sceneClock`, `participants`, `history`,
  `sleepCycle`, `memoryResonance`, `incubation`, `thoughtLanes`, `bridge`,
  `appraisals`, and `reactions`.

Visible operational posture:

- `epiphany.surface.roles`: lane status, note, jobs, authority scopes, and
  recommended action.
- `epiphany.surface.role_result`: status, job, finding, patch, risk, evidence
  gaps, and reviewable result state.
- `epiphany.surface.coordinator`: Self route, target role, review requirement,
  automation eligibility, reason, source signals, and lane summaries.
- `epiphany.surface.face`: Face bubbles, drafts, posts, and latest public
  artifacts.
- `epiphany.surface.jobs`: job count, owner role, progress, blocking reason,
  and active work.
- `epiphany.surface.pressure`: context pressure and compaction readiness.
- `epiphany.surface.reorient` and `epiphany.surface.reorient_result`:
  continuity verdicts, resume posture, regather posture, and reorientation result.

Useful normalized visual fields:

```text
roleId, agentId, displayName, organKind
status, activity, readiness, load, blocked, completed
heartbeatPhase, wakeIntensity, sleepDepth, memoryResonance
pressure, continuityRisk, reviewState, hasPatch
risk, confidence, evidenceGaps, recommendedAction
targetRole, hasBubble, speaking, jobCount
homeOrbitSlot, expressiveOffset, wanderRadius, gravityWellPulse
verticalLift, buoyancy, returnHomeStrength
traitActivations: map of canonical_state variable -> current_activation
```

## Normalization Rules

The renderer should convert loose surface strings into bounded scalars before
the shader sees them. First implementation rules:

```text
statusWeight("running")     = 1.00
statusWeight("ready")       = 0.72
statusWeight("needed")      = 0.66
statusWeight("review")      = 0.62
statusWeight("waiting")     = 0.38
statusWeight("completed")   = 0.34
statusWeight("blocked")     = 0.18
statusWeight("unavailable") = 0.08
statusWeight(other)         = 0.22

activity =
  saturate(statusWeight(status) + min(jobCount, 3) * 0.08)

blocked =
  status in {"blocked", "unavailable"} || blockingReason exists

completed =
  status == "completed" || roleResult.status == "completed"

hasPatch =
  roleResult.finding.statePatch exists

risk =
  saturate(0.2 * count(finding.risks)
         + 0.16 * count(finding.evidenceGaps)
         + (blocked ? 0.25 : 0.0)
         + pressureWeight(pressure.level))

confidence =
  finding.confidence if numeric
  else 1.0 - saturate(0.5 * risk)
```

`pressureWeight` maps `unknown -> 0.15`, `low -> 0.1`, `medium -> 0.35`,
`high -> 0.65`, `critical -> 0.9`. Unknown is not zero; missing telemetry is
itself a small continuity smell.

## Coordinate Contract

All role placement and role-local SDF orientation uses Aquarium world space:

- `+X`: Grid right.
- `+Y`: Grid forward.
- `+Z`: Grid up.
- `gridCenter`: camera target on the XY plane.
- `selfAnchor`: `gridCenter`.
- `cameraFacingDir`: normalized horizontal vector from `gridCenter` toward the
  camera. This is the room-facing direction.
- `cameraRightDir`: normalized horizontal camera right vector.

Each role has a stable home orbit slot around `selfAnchor`. These slots are
Aquarium-owned spatial projections of semantic Epiphany roles:

| Organ | Role id | Angle | Ring | Home formula |
| --- | --- | ---: | ---: | --- |
| Self | `coordinator` | 0 deg | 0.00 | `selfAnchor` |
| Face | `face` | toward camera | 0.58 | `selfAnchor + cameraFacingDir * r` |
| Imagination | `imagination` | 135 deg | 0.78 | `selfAnchor + polar(135) * r` |
| Eyes | `research` | 35 deg | 0.86 | `selfAnchor + polar(35) * r` |
| Body | `modeling` | 215 deg | 0.66 | `selfAnchor + polar(215) * r` |
| Hands | `implementation` | 270 deg | 0.82 | `selfAnchor + polar(270) * r` |
| Soul | `verification` | 325 deg | 0.78 | `selfAnchor + polar(325) * r` |
| Life | `reorientation` | 80 deg | 0.62 | `selfAnchor + polar(80) * r` |

`r` is the current agent-orbit radius, independent of Grid visual radius. A
good first value is `min(gridRadius * 0.48, 22)`, clamped to `[10, 24]`.

`polar(angle)` means `float2(cos(angle), sin(angle))` in the XY plane. The Face
slot is camera-relative because Face is literally the public mouth; it should
prefer the room side. All other slots are world-stable so spatial memory does
not rotate every time the camera does.

## Derived Spatial Anchors

The renderer must derive named anchors before evaluating per-role motion.
Epiphany does not publish world-space graph, job, artifact, nor role anchors.
It publishes semantic state. Aquarium owns the visible frame and projects that
semantic state into deterministic local anchors around role bodies.

```text
roleAnchor(roleId) =
  role home orbit slot

userAnchor =
  selfAnchor + cameraFacingDir * agentOrbitRadius * 1.15

artifactAnchor(roleId) =
  roleAnchor(roleId) + normalize(roleAnchor(roleId) - selfAnchor) * 3.5
  + cameraRightDir * 2.0

evidenceAnchor =
  artifactAnchor("research"), when Eyes has evidence ids, artifacts, graph
  query results, plus retrieved context
  else roleAnchor("research") + roleForward("research") * 3.0

implementationArtifactAnchor =
  artifactAnchor("implementation"), when Hands has changed files,
  implementation artifacts, plus active implementation jobs
  else roleAnchor("implementation") + roleForward("implementation") * 3.0

reviewAnchor =
  roleAnchor("verification")

continuityAnchor =
  roleAnchor("reorientation")

targetAnchor =
  roleAnchor(targetRole), if targetRole is a known role id
  else evidenceAnchor, if recommendedAction == "graphQuery"
  else continuityAnchor, if recommendedAction contains "reorient"
  else reviewAnchor, if requiresReview
  else roleAnchor(roleId) + roleForward(roleId) * 3.0

workAnchor =
  implementationArtifactAnchor, if changed files, implementation artifacts,
  plus active implementation jobs exist
  else coordinator target role anchor, if targetRole is present
  else reviewAnchor, if implementation is waiting on verification review
  else roleAnchor("implementation") + tangentClockwise("implementation") * 4.0
```

`tangentClockwise(role)` is the XY tangent around `selfAnchor` at that role's
home slot. It gives an idle directional bias without pretending there is a real
target.

`roleForward(role)` is `normalize(roleAnchor(role) - selfAnchor)` for every
orbiting role. For Self, it is `cameraFacingDir`. If a role is exactly at
`selfAnchor`, use `cameraFacingDir`.

These anchors are not data contracts with Epiphany. They are Aquarium's
diegetic layout rules for semantic surfaces. If a later Aquarium feature
creates local graph-node and artifact objects, those objects may become anchors
inside Aquarium, but Epiphany still does not own their spatial arrangement.

State-derived vectors:

```text
homeVector       = homeOrbitSlot - currentPosition
outwardVector    = normalize(homeOrbitSlot - selfAnchor)
targetVector     = targetAnchor - currentPosition
targetDirection  = normalizeOr(targetVector.xy, outwardVector)
workDirection    = normalizeOr(workAnchor.xy - handsPosition.xy,
                               tangentClockwise("implementation"))
evidenceDirection = normalizeOr(evidenceAnchor.xy - eyesPosition.xy,
                                roleForward("research"))
routeDirection   = normalizeOr(roleAnchor(targetRole).xy - selfPosition.xy,
                               roleForward("coordinator"))
roleExpressionDirection =
  normalizeOr(targetAnchor.xy - currentPosition.xy, roleForward(roleId))
```

`normalizeOr(value, fallback)` returns the fallback when the vector length is
below epsilon. This rule matters; near-zero direction vectors should not turn
into flickering NaNs with a nice color palette.

## SDF Rules

- Each organ gets a distinct SDF family, not only a color swap.
- Each SDF object shader exposes `sdfDistance` for the hot raymarch loop and
  `sdfSurface` for the refined hit. `sdfSurface` returns PBR material values:
  base color, metallic, roughness, and emission. The shared Aquarium shading
  function derives dielectric/metal diffuse and specular response from those
  values; role anatomy and region choice stay inside the Epiphany object shader.
- Material regions may exist inside an object shader when they help choose PBR
  values for readable parts: core, shell, tool edge, lens, ribbon, risk seam,
  memory seed, bubble, and so on. They are not part of the shared hot surface
  packet.
- State changes should alter low-dimensional parameters first: radius, lobe
  count, twist, aperture, rib spacing, pulse phase, shell openness, wake length.
- Keep DOM hit targets stable. The visual body can breathe, fold, and shimmer;
  the interaction root must not dodge the user's pointer like it owes money.
- Avoid volumetric dependence. These are analytic solids over the Grid with
  PBR materials, emissive accents, and planned diegetic labels.

## Motion Rules

Orbit lanes are home positions, not cages. Each agent should have a stable
`homeOrbitSlot` for readability and spatial memory, plus a bounded
`expressiveOffset` for short local wandering. The return force should be visible
and gentle: agents drift out to express curiosity, stress, speech, focus, and
avoidance, then ease back toward their lane instead of snapping into place.

Vertical motion should come from the same Grid grammar as placement. An agent's
gravity well can pulse its local Grid height, raising and lowering the body
because the CPU and shader agree that body centers sit above the Grid surface.
This makes heartbeat and mood visible in the world instead of faking hover with
a detached bobbing transform.

Recommended controls:

- `wanderRadius`: maximum horizontal distance from the home orbit slot.
- `expressiveOffset`: current bounded offset in Grid space.
- `returnHomeStrength`: spring force back toward the home orbit slot.
- `gravityWellPulse`: temporary change to the agent's Grid brush height.
- `verticalLift`: additional body-center lift derived from the local well pulse.
- `buoyancy`: role-specific damping and overshoot for vertical response.

Baseline placement integration:

```text
desiredOffset =
  roleExpressionDirection * expressionAmount * wanderRadius

expressiveOffset =
  dampedSpring(expressiveOffset, desiredOffset, returnHomeStrength, damping)

brushCenter =
  homeOrbitSlot + expressiveOffset

gridHeight =
  sampleSharedGridBrushes(brushCenter.xy)

bodyCenter =
  float3(brushCenter.xy, gridHeight + 2 * bodyRadius + verticalLift)
```

Baseline role constants:

| Organ | Wander radius | Return strength | Buoyancy |
| --- | ---: | ---: | ---: |
| Self | 1.2 | 0.90 | 0.45 |
| Face | 3.2 | 0.62 | 0.85 |
| Imagination | 4.4 | 0.48 | 0.95 |
| Eyes | 2.4 | 0.78 | 0.52 |
| Body | 1.8 | 0.86 | 0.38 |
| Hands | 2.2 | 0.80 | 0.48 |
| Soul | 1.6 | 0.92 | 0.32 |
| Life | 2.0 | 0.84 | 0.55 |

The Grid brush uses the same `brushCenter` and `gravityWellPulse`. Vertical lift
is not an unrelated bob:

```text
gravityWellPulse =
  heartbeatPulse * heartbeatLiftScale
  + actionPulse * actionLiftScale
  - pressureSink * pressureSinkScale

verticalLift =
  gravityWellPulse * buoyancy
```

The exact brush equation can change with the Grid implementation, but CPU
placement and shader placement must call the same equation; otherwise the agent will
visibly detach from its own well.

State mapping:

- Heartbeat primary state should create a small vertical pulse and brief outward
  drift.
- Speaking and Face bubble output can wander toward the room-facing side.
- High pressure should pull Life, Soul, and Self tighter toward home while still
  deepening their wells.
- Imagination may wander widest when exploring, but should collapse back toward
  home on accepted plans.
- Hands should wander least while blocked and lean toward active work when
  running.
- Eyes can make short focus darts toward Aquarium's local evidence anchor.

Implementation invariant: expressive wandering and vertical well pulses must be
part of the same CPU-visible placement model used for body centers, shadows,
gravity brushes, and temporal reprojection. If the shader sees a different body
position than the CPU, the prettiness has started lying.

## Self

Role ids: `coordinator`, `epiphany.self`

Purpose: routing, authority, checkpoint, run state, and review boundary.

Appearance: a central routing heart made of a black void command sphere with a
sharp golden Fresnel rim, one gold core band, and nested orichalcum rail bands.
The rails form a compact orrery lattice around the void, with compact gate nodes
at true triple-band crossings and narrow emissive strips along the inner rail
faces. Self should feel central and decisive, not busy. The orrery comparison is
a motion and topology metaphor: the routing rails should have nested orbital
order and slow phase motion rather than static, intersecting hoops. The angelic
read should come from overwhelming but coherent symmetry and radiance, not
literal symbolic anatomy.

Reasoning: Self is not a worker lane. It preserves agency by routing work,
rejecting bad authority claims, and deciding which organ deserves the next
move. A void core inside an ordered routing lattice makes the idea visible:
many possible paths, one current routing authority.

Math:

- Exact stable sphere core rendered as a black void with golden Fresnel
  edge-lighting, bypassing the shared PBR shader for that core region. Keep one
  gold lattice band on the core surface; do not stack multiple crossing bands
  whose material regions can overtake the sphere.
- A compact log-spherical lattice maps local direction and logarithmic radius
  into stable nested shells. Orbital rails are level sets where fixed radial
  shell strata cross angular harmonic bands; gate nodes are compact crossings
  where all angular bands agree in that same lattice. Pairwise shallow rail
  intersections should remain rail material, not stretch into gate-colored
  slabs.
- Shell radius is structural, not animated. Phase may rotate each shell's
  angular frame and add mild band distortion, but it must not move radial zero
  crossings or create rings by expanding points into circles. Multiple
  concentric shells should occupy orthogonal orbital frames so complexity nests
  cleanly instead of colliding on one surface.
- Smooth union is reserved for joining the core to lattice-derived rails,
  gates, inlays, and seam marks. Do not hand-place independent hoops or
  separate torus inventories when the form wants one coherent orbital grammar.
- Material regions: black void core, one gold core band, metallic
  gold/orichalcum routing rails, inner rail emission strip, bright current
  target gate, and dark review seam. Rails should be readable bands rather than
  hairline emitters. Bloom-driving emission is controlled: core Fresnel, active
  gates, and narrow inner rail strips; the broad rail body uses PBR material
  contrast so it does not turn into fireflies.
- Implementation note: complexity should emerge from the coordinate
  abstraction. If the shader grows by adding more named hoop equations, stop and
  rebuild the field grammar instead.

Movement:

- Slow center pulse from heartbeat phase.
- Orbital rail families rotate at different slow phase rates so the orrery is a
  motion metaphor, not static decoration.
- `targetRole` resolves through `roleAnchor(targetRole)`. The active routing
  gate is the torus arc whose tangent points closest to `routeDirection`.
- Selected target role lights that gate and sends a traveling pulse along the
  matching arc.
- `requiresReview` adds a narrow dark seam around the core.
- `canAutoRun` sharpens and brightens the active gate.
- High pressure makes the arcs tighten around the core instead of expanding.

Surfaces:

- `epiphany.surface.coordinator`: `action`, `targetRole`,
  `recommendedSceneAction`, `requiresReview`, `canAutoRun`, `reason`,
  `sourceSignals`.
- `epiphany.surface.roles`: lane summaries.
- `epiphany.agent_heartbeat`: Self participant readiness, reaction, and wake.
- `epiphany.surface.pressure`: compaction and routing pressure.

## Face

Role ids: `face`, `epiphany.face`

Purpose: public expression, bubbles, drafts, posts, and room-native speech.

Appearance: a speech anemone / phoneme lantern: a translucent bell with a
continuous living mouth aperture, warm throat glow, and syllable pearls carried
by a small flow around the lip. It should look like bounded speech becoming
public, not a generic chat icon.

Reasoning: Face translates hidden organ weather into public, bounded speech. A
lantern form makes speech feel emitted and warm, while the mouth flow makes
drafts, posts, and bubble count visible without dumping private machinery.
Face is a public aperture: it shapes what leaves the system.

Math:

- Governing domain: local mouth-flow coordinates around the bell opening.
  `u` is angle around the lip, `v` is depth through the throat, and `breath`
  is driven by speech/activity state. The lip, throat glow, speech ribbon, and
  beads derive from this one aperture field.
- Bell body from a translucent superquadric dome with exponent `0.42`, softened
  into a flared lower lip by the mouth-flow aperture instead of clipped by a
  separate decorative cut.
- Mouth aperture as a continuous signed field in `(u, v)`: closed silence is a
  narrow crescent, speaking opens toward an oval, and uncertainty introduces a
  small asymmetric curl without changing the bell's root topology.
- Draft pearls occupy short inner streamlines inside the lip. Posted bubbles
  escape to wider outer streamlines. Bead distance from the mouth carries state;
  do not use arbitrary orbit count as the main read.
- Speech ribbon is a swept lemniscate/current line around the mouth-flow field,
  with radius and brightness modulated by heartbeat and public-output activity.
- Material regions: translucent enamel bell, warm emissive throat, glossy
  syllable pearls, muted draft pearls, brighter posted pearls.

Topology invariants:

- The mouth is an aperture field in the bell, not a pasted-on icon or two torus
  cuts.
- Drafts stay inside the bell; posts move outside the lip.
- No speech surface may expose private machinery. Face shows public output
  pressure, not internal chain-of-thought weather.
- Forbidden shortcut: chat bubble logo, decorative orbit beads, or a generic
  smiling blob.

Movement:

- `hasBubble` opens the mouth and releases a small bead pulse.
- Drafts orbit close and dim; posts orbit farther and brighter.
- Speaking drift direction is `normalize(userAnchor - facePosition)`, capped by
  Face's `wanderRadius`.
- Heartbeat primary state makes the lantern brighten from inside.
- Silence closes the mouth but keeps a slow listening breath.

Surfaces:

- `epiphany.surface.face`: `bubbles`, `drafts`, `posts`, `latestArtifacts`,
  `status`.
- `epiphany.agent_heartbeat`: bridge decisions, thought lanes, selected role,
  Face participant readiness.
- `epiphany.agent_memory`: Face public-description and presentation traits.

## Imagination

Role ids: `imagination`, `epiphany.imagination`

Purpose: planning, drafts, backlog, objective shaping, and candidate options.

Appearance: an impossible flower made from translucent prismatic ribbon-sheets
around a luminous opal idea seed. It should read like glass, soap film, and
aurora caught in a botanical topology: broad curling sheets, raised glowing
rims, nested negative-space apertures, and a vertical fountain of candidate
forms. The silhouette should keep almost becoming another silhouette.

Reasoning: Imagination is generative, but not decorative chaos. It produces
bounded candidate shapes: captures become drafts, drafts become objectives. Petals
and ribbons communicate branching possibility while still returning to a center.

Math:

- Central opal bud: a faceted egg/superquadric seed, vertically upright, with a
  small calyx-like cradle at its base. This is the topological root; all ribbons
  either emerge from it or visibly curl around it.
- Primary bloom: six large ribbon-sheets arranged as three mirrored pairs around
  the seed. Each sheet has one continuous center curve rising from the lower
  cradle, flaring outward, curling back inward to form a visible aperture, then
  tapering to a lifted lip. The sheet is a thin slab/film around that curve, not
  a fat ellipsoid or simple tube.
- Sheet parameterization:
  `u in [0,1]` moves from root to lip.
  `center(u)` is a cubic Bezier or equivalent curve with upward lift, outward
  flare, and inward curl.
  `width(u)` grows from a narrow root to a broad blade, then narrows at the lip.
  `thickness(u)` stays thin and nearly constant; visible mass comes from the
  curled sheet area and raised rim, not from petal bulk.
  `curl(u)` rotates the sheet normal around the center curve so the blade forms
  a loop/aperture.
- Raised rim: every primary sheet has two brighter rim tubes running along its
  long edges. Rims define the readable silhouette and may use tapered capsule
  chains. The translucent film between rims may be approximated with bounded
  sheet/slab fields or material shimmer, but the rim paths must be actual traced
  geometry.
- Nested secondary curls: inside each primary aperture, add one smaller echo
  ribbon or rim arc that follows the same curl direction. These secondary forms
  are lower priority than the primary silhouette and should be LOD-gated before
  they become a compile/runtime burden.
- Vertical candidate filaments: a sparse set of golden emissive tendrils rises
  from the seed through the center gap. These are thin tapered curves with small
  bead sparks, not a particle swarm.
- Harmonic layout controls: the overall bloom uses rotational harmonics only to
  place and phase the ribbon families:
  `pairCount = 3`, `primaryCount = 6`, `openness = activity`,
  `curl = lerp(0.55, 1.25, activity)`, and
  `secondaryCurlCount = min(backlogCount, 3)`.
  Do not use a decorative polar radius field as the primary surface unless it
  produces the same sheet, rim, and aperture topology.
- Smooth unions are allowed at structural joins: seed-to-root cradle, root
  bundles, rim-to-sheet contact, and bead/tendril bases. Do not smooth-union the
  whole bloom into a blob; negative space is part of the form.
- Material regions: opal seed, translucent pearlescent film, bright cyan/gold
  rim highlights, emissive idea sparks, warm candidate filaments, and cool
  backlog shadow.

Topology invariants:

- The first read is a luminous central seed wrapped by broad translucent curling
  ribbons.
- At least three clean interior apertures must remain visible from the default
  camera angle.
- Primary ribbons must be sheets with rim structure, not merely tubes. Tubes may
  carry rims and tendrils, but they do not satisfy the petal requirement alone.
- The bloom is vertically biased: lower roots gather near the seed, upper lips
  lift and curl outward like a fountain.
- Fine glitter, internal color streaks, and star specks are material/shading
  detail unless they are sparse bead sparks or candidate filaments.

Movement:

- Planning readiness increases petal openness.
- Backlog and capture count increases secondary harmonic detail.
- A completed planning result collapses the flower into a cleaner symmetric
  bloom.
- Uncertainty and evidence gaps add small branching petallets that fade rather than
  becoming permanent complexity.
- Exploratory wander direction is the weighted sum of `outwardVector` and
  `tangentClockwise("imagination")`; accepted plans set `expressionAmount`
  toward zero so the flower returns home.

Surfaces:

- `epiphany.surface.roles`: `imagination` lane status and note.
- `epiphany.surface.role_result`: imagination result, finding, patch, evidence
  gaps.
- `epiphany.surface.planning`: captures, backlog, roadmap streams, objective
  drafts.
- `epiphany.agent_memory`: planning temperament and objective-shaping traits.
- `epiphany.agent_heartbeat`: Imagination wake, rumination, and incubation.

## Eyes

Role ids: `research`, `epiphany.eyes`

Purpose: evidence, prior art, graph query, artifacts, and outside-source sight.

Appearance: an evidence astrolabe: a compact black-glass lens held inside a
rotating proof lattice, with aperture petals, meridian lines, and small cyan
evidence sparks snapped to validated source directions. It should read as
perception and proof, not surveillance theater.

Reasoning: Eyes earns visual sparkle only when evidence exists. The aperture
metaphor is useful because research narrows and widens attention; the astrolabe
read makes that narrowing feel like constraint by evidence, not answer-claiming
or watching-for-its-own-sake.

Math:

- Governing domain: a lens hemisphere facing `evidenceDirection`. Directions
  are projected into tangent disk coordinates on the lens; aperture blades,
  meridians, scan arcs, and evidence sparks share this domain.
- Lens from an intersection of two spheres or a compact biconvex SDF with black
  glass material and cyan edge caustics.
- Aperture petals as logarithmic spiral shutters in the tangent disk. Unknowns
  widen the aperture; confidence narrows and steadies it.
- Proof lattice as a sparse set of meridian and latitude grooves over the lens
  hemisphere. These are constraint lines, not decoration.
- Evidence sparks occupy validated source directions on the hemisphere or their
  nearest meridian positions. No evidence means no sparks.
- Scanning ring is a pale arc in the same tangent disk, animated by query state
  and masked by aperture openness.
- Material regions: black glass lens, blue-metal aperture petals, pale proof
  meridians, cyan evidence sparks, soft unfocused unknown halo.

Topology invariants:

- The first read is a dark lens constrained by an instrument lattice.
- Evidence lights must attach to evidence-bearing directions or meridian lines;
  random sparkle is a lie.
- Unknowns soften and widen the optical field; confidence sharpens and narrows
  it.
- Forbidden shortcut: robot eye, camera iris icon, or generic sparkly orb.

Movement:

- Graph query and artifact presence rotates aperture and adds controlled sparkle.
- Unknowns widen the aperture and slow the scan.
- High confidence narrows aperture and steadies the lens.
- No evidence means no sparkle. The lens still watches, but it does not fake a
  finding.
- Focus dart direction is `evidenceDirection`. `evidenceAnchor` is Aquarium's
  local projection for evidence-bearing surfaces; Epiphany only supplies the
  semantic evidence, artifact, and context state.

Surfaces:

- `epiphany.surface.graph_query`: graph query state and results.
- `epiphany.surface.context`: retrieved context and source windows.
- `epiphany.surface.role_result`: research result and Eyes result when present.
- `epiphany.surface.roles`: research lane status where published.
- `epiphany.agent_memory`: evidence and prior-art traits.

## Body

Role ids: `modeling`, `epiphany.body`

Purpose: architecture, graph anatomy, dataflow, checkpoint model, and structural
fit.

Appearance: a living load-bearing graph stone: a heavy soft geode mass with
embedded stress ribs, enamel graph veins, checkpoint pearls, and occasional dark
fault seams. It should be adorable by weight and stability, not by making baby
noises in shader form.

Reasoning: Body models the machine's shape. It should feel structural,
grounded, and tactile. The graph ribs make architecture visible as load paths,
while the soft mass reminds us the model serves the living system rather than
becoming a diagram shrine.

Math:

- Governing domain: superellipsoid surface coordinates. `u` and `v` parameterize
  the geode surface; ribs are geodesic, loxodrome, meridian, equator, or fault
  curves in that domain, not tubes glued onto arbitrary world positions.
- Superellipsoid/geode core with exponent `0.72`, soft flattened base, and
  optional support lobes only when they are part of the same surface field.
- Graph ribs as embedded enamel stress paths over the surface. First
  implementation uses a small deterministic grammar: one meridian path, one
  equator path, two diagonal loxodromes, and one fault-line family. Semantic
  graph size and frontier count brighten, fill, or split these paths; Epiphany
  graph nodes do not define their world positions.
- Checkpoint pearls are embedded at rib intersections and fill in sequence.
- Evidence gaps open dark fault seams between ribs. Blocked state widens faults
  rather than spawning unrelated symbols.
- Subtle displacement from low-frequency breathing mass must preserve the rib
  domain so veins do not slide over the body.
- Material regions: matte green-black soft stone, glossy rib enamel, bright
  checkpoint pearls, dark blocked fissures, faint warm interior where ribs meet.

Topology invariants:

- The first read is heavy soft mass with structural load paths.
- Ribs are surface-domain stress lines, not arbitrary graph overlays.
- Checkpoint nodes are embedded pearls, not free-floating beads.
- Forbidden shortcut: soft blob plus random graph lines.

Movement:

- Modeling `needed` state lowers the body and brightens empty rib slots.
- Completed checkpoint and result fills nodes in sequence.
- Evidence gaps open small dark fissures between ribs.
- High load makes the mass sag; high confidence makes ribs align and glow
  steadily.
- Connective wake direction points from Body toward Self when modeling is
  regathering and checkpointing, and toward `artifactAnchor("modeling")` when a
  modeling result plus state patch exists. `frontierNodeIds` affect rib count and
  brightness, not world direction.

Surfaces:

- `epiphany.surface.roles`: `modeling` lane status, note, recommended action.
- `epiphany.surface.role_result`: modeling finding, checkpoint summary,
  frontier nodes, state patch, evidence gaps.
- `epiphany.surface.graph_query`: architecture and dataflow graph context.
- `epiphany.agent_memory`: Body graph and checkpoint traits.

## Hands

Role ids: `implementation`, `epiphany.hands`

Purpose: source changes, runnable work, changed files, and implementation
artifacts.

Appearance: a compact code-working mandible tool: a polished dark tool-seed
with three continuous gripper lobes, a warm enamel grip field, and a hot forward
cutting seam. It should look ready to touch code, not like a floating wrench
logo.

Reasoning: Hands is the only lane allowed to touch the code body. The shape
should feel tactile and precise, with motion that acknowledges work quickly and
then settles. No grand aura. Hands earns drama by producing diff.

Math:

- Governing domain: prolate tool coordinates aligned to `workDirection`.
  Longitudinal coordinate `u` runs from rear grip to forward edge; angular
  coordinate `theta` wraps the core; radial coordinate gives the gripper field.
- Tool core is an elongated superquadric seed, exponent `0.58`, with a subtle
  helical groove cut along the same prolate domain.
- Three gripper lobes are phase lobes of one continuous tri-jaw chuck field
  around the core. They open, retract, and twist through `theta`; do not build
  disconnected claws unless a future measured constraint forces that cut.
- Tool edge is a hot ridge on the forward side of the same field, not a separate
  logo blade. Its cutting line may use a sine notch, but it remains locked to
  `workDirection`.
- Changed files and implementation artifacts appear as tiny metal filings or
  sparks near the forward seam, then decay quickly.
- Blocked state retracts jaws and adds a dull cap over the edge rather than
  spawning an unrelated warning badge.
- Material regions: warm enamel grip, dark polished core, high-metal edge, hot
  action stripe, dull blocked cap, brief file-spark flecks.

Topology invariants:

- The first read is a precise working tool-seed with a continuous tri-jaw field.
- All grasping geometry derives from `workDirection` and the prolate tool
  domain.
- Changed-file sparks are receipts, not decoration, and should fade.
- Forbidden shortcut: wrench icon, floating detached claws, or generic sparks.

Movement:

- Running and continuing work leans the core toward `workAnchor`.
- Changed files and artifacts trigger quick acknowledgement pops.
- Blocked state retracts grippers and dims the tool edge.
- Verification pass and accepted result relaxes the shape back into idle.

Hands spatial rule:

```text
handsForward = workDirection
handsRight   = float2(-handsForward.y, handsForward.x)
coreLength   = lerp(1.15, 1.65, activity)
coreRadius   = lerp(0.46, 0.38, activity)
toolEdgePos  = handsCenter.xy + handsForward * coreLength * 0.48
gripPosA     = handsCenter.xy - handsForward * 0.18 + handsRight * 0.34
gripPosB     = handsCenter.xy - handsForward * 0.18 - handsRight * 0.34
```

`workDirection` is not semantic mood. It is derived from `workAnchor` in the
coordinate contract above. `workAnchor` is Aquarium's local projection of
implementation artifacts and jobs, coordinator target, and review dependency. If
none of those semantic states exist, it falls back to the clockwise orbit
tangent, so the shape remains deterministic and debuggable.

Surfaces:

- `epiphany.surface.roles`: `implementation` status, note, jobs.
- `epiphany.surface.jobs`: job ownership, progress, blocking reason.
- `epiphany.surface.role_result`: implementation receipts if surfaced.
- `epiphany.runtime.job` and `epiphany.runtime.job_result`: native runtime
  work state.
- `epiphany.agent_memory`: Hands implementation discipline traits.

## Soul

Role ids: `verification`, `epiphany.soul`

Purpose: verification, confidence, risk, findings, invariants, and review.

Appearance: a verdict polyhedron / oath crystal: a compact faceted body with a
soft white inner oath light, blue confidence edges, red risk faults, and engraved
finding lines. It should be severe but not hostile.

Reasoning: Soul protects promises. Facets communicate exactness and review,
while the inner light keeps it from becoming a punishment object. The risk seam
shows tension between confidence and unresolved danger; the form is constraint,
not punishment.

Math:

- Governing domain: a family of signed half-space planes. The body is the
  rounded intersection of constraint planes; confidence, risk, and review state
  adjust plane offsets, bevel radius, and fault intensity.
- Rounded octahedron or small polyhedron SDF using max-of-planes blended with
  radius `0.08`; higher confidence sharpens bevels, lower confidence softens
  and clouds them.
- Inner oath core is a small white sphere or glow visible through material
  contrast, not a transparency dependency.
- Risk seam is a red-blue great-circle fault chosen in the crystal's plane
  domain. Ring thickness is `lerp(0.015, 0.09, risk)`, but it should follow
  facet intersections when possible instead of reading as a decorative torus.
- Finding marks are engraved grooves along facet intersections. Accepted
  findings become clean blue lines; failed or blocked findings create short
  jagged red fractures that can later heal.
- Material regions: cool crystal facets, white oath core, blue confidence
  edges, red risk faults, dark rejected marks.

Topology invariants:

- The first read is a faceted constraint object with an inner promise light.
- Facets own the form. Seams, marks, and fractures are plane-domain features,
  not decals floating on a gem.
- Risk makes the crystal tenser; confidence makes it clearer and steadier.
- Forbidden shortcut: generic gemstone with a random red ring.

Movement:

- Verification running rotates slowly with steady, low expressiveness.
- Risk increases red seam thickness and facet sharpness.
- Confidence brightens blue edges and steadies rotation.
- Failed and blocked findings produce short angular fractures; accepted findings
  heal them into clean facet lines.
- Review-facing posture points the risk seam normal toward
  `roleAnchor(coordinator)` when `requiresReview` is true, else toward
  `outwardVector`.

Surfaces:

- `epiphany.surface.roles`: `verification` status and review posture.
- `epiphany.surface.role_result`: finding verdict, risks, evidence gaps,
  self-persistence review, status.
- `epiphany.surface.pressure`: compaction and context pressure.
- `epiphany.surface.coordinator`: `requiresReview` and source signals.
- `epiphany.agent_memory`: Soul evidence and invariant traits.

## Life

Role ids: `reorientation`, `epiphany.life`

Purpose: continuity, pressure, sleep, reorientation, resume posture, regather posture, and
memory survival.

Appearance: a memory nautilus seed: a teal protective shell wrapped around a
small warm ember, with a logarithmic seam and memory beads traveling along the
same spiral path. It should feel like continuity surviving weather.

Reasoning: Life carries the thread through compaction, sleep, and rupture. A
seed-shell is the right metaphor because it protects a candidate continuation,
while the ember and bead trail show what context remains warm enough to carry
forward. The spiral is continuity under pressure, not a decorative tail.

Math:

- Governing domain: a revolved seed/cardioid shell with logarithmic spiral
  coordinates. The shell seam, memory bead trail, regather motion, and resume
  release all use the same spiral grammar.
- Seed body from a revolved cardioid SDF with a flattened base and protective
  shell thickness. Pressure tightens the shell rather than scaling it randomly.
- Spiral seam as a logarithmic groove carved along the seed. Regather opens this
  groove; sleep narrows it while keeping the ember visible.
- Ember as a small warm emissive sphere held inside the shell aperture.
- Memory beads are samples along the same logarithmic spiral, trailing toward
  `continuityAnchor`. Regather pulls bead phase inward toward the shell; resume
  advances phase outward along the old path.
- Breathing field as low-amplitude radial displacement locked to the shell
  domain so the seam and beads remain coherent.
- Rupture scar as a dark interruption in the seam, not a separate wound symbol.
- Material regions: teal shell, warm ember, pale memory beads, dark rupture
  scar, faint pearlescent seam edge.

Topology invariants:

- The first read is a protective seed-shell with a visible ember.
- Seam and bead trail share one logarithmic spiral. If they disagree, continuity
  has become decoration.
- Pressure tightens protection; resume releases memory; sleep preserves ember.
- Forbidden shortcut: seed plus unrelated orbiting dots.

Movement:

- Pressure increases breathing depth and tightens the shell.
- Regather opens a dark seam and pulls beads inward.
- Resume steadies the ember and releases beads along the old path.
- Sleep and incubation slows orbit and dims the shell while preserving the ember.
- Memory bead trail follows a logarithmic spiral centered on Life's
  `bodyCenter.xy`. Regather reverses bead phase toward the seed; resume advances
  phase outward along `continuityAnchor -> lifePosition` tangent.

Surfaces:

- `epiphany.surface.reorient`: decision action, checkpoint status, pressure
  level, reasons, retrieval status, watcher status.
- `epiphany.surface.reorient_result`: reorientation worker result.
- `epiphany.surface.pressure`: pressure level and compaction readiness.
- `epiphany.agent_heartbeat`: sleep cycle, memory resonance, incubation.
- `epiphany.agent_memory`: Life continuity and memory traits.

## First Implementation Slice

## Realtime Rendering Constraints

The role bodies are mathematically rich enough to become expensive if treated
as one heroic scene SDF. Aquarium must render them as bounded per-agent
implicit objects.

Literature-grounded rules:

- Sphere tracing advances safely only when the step distance is an exact
  distance or conservative distance bound. Fancy algebraic fields must never be
  used as raw march steps unless wrapped by a conservative bound.
- Every agent has a cheap proxy bound. Rasterize that proxy and raymarch only
  inside the proxy pixels.
- Never evaluate all role SDFs in one scene-global march. Select one role SDF
  from `AgentVisualState.organKind` after the proxy is hit.
- Each role SDF starts with a cheap bound distance. If the point is outside the
  detailed region, return that bound without evaluating petals, claws, ribs,
  beads, seams, sparks, and grooves.
- Every expensive feature has its own local bound. Feature distance functions
  are evaluated only when the ray point is near that feature's region.
- Normals are computed only after a hit. Use analytic gradients for simple
  primitives, and tetrahedral four-tap finite differences for full-detail
  composite organs.
- Shadows are budgeted separately. Default agents use direct body lights, IBL,
  and Grid contact shadow. A selected hero agent may get a four-to-eight step
  cone shadow. No nested shadow march per light.
- Antialiasing starts with pixel-footprint-aware hit epsilon. Later edge
  refinement can use selective cone-style supersampling near SDF boundaries.

LOD contract:

| Tier | Use | Geometry |
| --- | --- | --- |
| LOD 0 | selected, hovered, speaking, primary heartbeat, near camera | full role SDF with material regions and feature animation |
| LOD 1 | ordinary visible agents | core shape plus major silhouette features |
| LOD 2 | distant agents | single recognizable mathematical primitive plus emissive accents |
| LOD 3 | very distant swarm context | billboard, sparkle, or project label |

The initial renderer budget target is one to two milliseconds total for all
visible agent bodies on a midrange GPU. The debug UI must expose step count,
proxy-hit coverage, state scalars, LOD tier, and SDF cost tier before the shader
grows beyond the first two role bodies. If a role needs material-region
inspection, expose it through an explicit debug lane rather than adding a
permanent region field to the shared PBR surface packet.

Implement the renderer contract in this order:

1. Add a CPU-side `AgentVisualState` and role-to-organ mapping in the Aquarium
   runtime boundary.
2. Preserve the current fixed body table as fallback data.
3. Add CPU-visible home orbit slots, bounded expressive offsets, and gravity
   well pulse state before adding decorative per-role motion.
4. Add per-agent proxy bounds and rasterize those proxies before raymarching.
5. Add LOD 1 material-region SDF evaluation for one organ pair: Body and
   Imagination.
6. Bind status, activity, heartbeat, and placement fields into shader constants.
7. Add debug modes for role id, state scalar, step count, proxy hit, LOD tier,
   and SDF cost tier so the cut can be inspected without
   believing the prettiness.

Body and Imagination are the best first pair because they exercise opposite
grammars: grounded graph mass and generative harmonic bloom. If both can share
the same state contract without visual mush, the rest of the organ set has a
real spine.
