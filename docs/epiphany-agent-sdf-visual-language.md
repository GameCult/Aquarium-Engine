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
  goals, memories, and optional overlays.

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
  continuity verdicts, resume/regather posture, and reorientation result.

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
  status in {"blocked", "unavailable"} or blockingReason exists

completed =
  status == "completed" or roleResult.status == "completed"

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
- `selfAnchor`: `gridCenter` unless a future swarm-level anchor overrides it.
- `cameraFacingDir`: normalized horizontal vector from `gridCenter` toward the
  camera. This is the "room/user side" direction.
- `cameraRightDir`: normalized horizontal camera right vector.

Each role has a stable home orbit slot around `selfAnchor`. These slots are
authoritative defaults for native Aquarium until Epiphany publishes explicit
world anchors:

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

The renderer must derive named anchors before evaluating per-role motion. If a
source is missing, fall through to the next entry. No shader path gets to invent
its own private direction.

```text
roleAnchor(roleId) =
  role home orbit slot
  unless AgentVisualState.explicitWorldAnchor is present for that role

userAnchor =
  selfAnchor + cameraFacingDir * agentOrbitRadius * 1.15

artifactAnchor(roleId) =
  roleAnchor(roleId) + normalize(roleAnchor(roleId) - selfAnchor) * 3.5
  + cameraRightDir * 2.0

reviewAnchor =
  roleAnchor("verification")

continuityAnchor =
  roleAnchor("reorientation")

evidenceAnchor =
  selected graph node world anchor, if a graph projection selected one
  else artifactAnchor("research"), if Eyes has artifacts or evidence ids
  else roleAnchor("research") + roleForward("research") * 3.0

targetAnchor =
  roleAnchor(targetRole), if targetRole is a known role id
  else evidenceAnchor, if recommendedAction == "graphQuery"
  else continuityAnchor, if recommendedAction contains "reorient"
  else reviewAnchor, if requiresReview
  else roleAnchor(roleId) + roleForward(roleId) * 3.0

workAnchor =
  active implementation job explicit world anchor, if present
  else artifactAnchor("implementation"), if changed files or artifacts exist
  else coordinator target role anchor, if targetRole is present
  else reviewAnchor, if implementation is waiting on verification/review
  else roleAnchor("implementation") + tangentClockwise("implementation") * 4.0
```

`tangentClockwise(role)` is the XY tangent around `selfAnchor` at that role's
home slot. It gives an idle directional bias without pretending there is a real
target.

`roleForward(role)` is `normalize(roleAnchor(role) - selfAnchor)` for every
orbiting role. For Self, it is `cameraFacingDir`. If a role is exactly at
`selfAnchor`, use `cameraFacingDir`.

`AgentVisualState.explicitWorldAnchor` is not part of the first native
implementation. It is reserved for a later CultNet field that publishes a graph
node, project label, or object anchor in Aquarium world coordinates. Until that
field exists, `roleAnchor` is always the home orbit slot and `workAnchor` uses
the fallback chain above.

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
- Every SDF returns distance plus material region id.
- Material regions should map to role-readable parts: core, shell, tool edge,
  lens, ribbon, risk seam, memory seed, bubble, and so on.
- State changes should alter low-dimensional parameters first: radius, lobe
  count, twist, aperture, rib spacing, pulse phase, shell openness, wake length.
- Keep DOM hit targets stable. The visual body can breathe, fold, and shimmer;
  the interaction root must not dodge the user's pointer like it owes money.
- Avoid volumetric dependence. These are analytic solids over the Grid with
  PBR materials, emissive accents, and future diegetic labels.

## Motion Rules

Orbit lanes are home positions, not cages. Each agent should have a stable
`homeOrbitSlot` for readability and spatial memory, plus a bounded
`expressiveOffset` for short local wandering. The return force should be visible
and gentle: agents drift out to express curiosity, stress, speech, focus, or
avoidance, then ease back toward their lane instead of snapping into place.

Vertical motion should come from the same Grid grammar as placement. An agent's
gravity well can pulse its local Grid height, raising or lowering the body
because the CPU and shader agree that body centers sit above the Grid surface.
This makes heartbeat and mood visible in the world instead of faking hover with
a detached bobbing transform.

Recommended controls:

- `wanderRadius`: maximum horizontal distance from the home orbit slot.
- `expressiveOffset`: current bounded offset in Grid/world space.
- `returnHomeStrength`: spring force back toward the home orbit slot.
- `gravityWellPulse`: temporary change to the agent's Grid brush height.
- `verticalLift`: additional body-center lift derived from the local well pulse.
- `buoyancy`: role-specific damping/overshoot for vertical response.

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
placement and shader placement must call the same equation or the agent will
visibly detach from its own well.

State mapping:

- Heartbeat primary state should create a small vertical pulse and brief outward
  drift.
- Speaking or Face bubble output can wander toward the room/user side.
- High pressure should pull Life, Soul, and Self tighter toward home while still
  deepening their wells.
- Imagination may wander widest when exploring, but should collapse back toward
  home on accepted plans.
- Hands should wander least while blocked and lean toward active work when
  running.
- Eyes can make short focus darts toward evidence-bearing anchors.

Implementation invariant: expressive wandering and vertical well pulses must be
part of the same CPU-visible placement model used for body centers, shadows,
gravity brushes, and temporal reprojection. If the shader sees a different body
position than the CPU, the prettiness has started lying.

## Self

Role ids: `coordinator`, `epiphany.self`

Purpose: routing, authority, checkpoint, run state, and review boundary.

Appearance: a central routing heart made of a warm core sphere crossed by
several smooth torus arcs. The arcs look like quiet rails orbiting a command
seed, with small gate nodes at their intersections. Self should feel central
and decisive, not busy.

Reasoning: Self is not a worker lane. It preserves agency by routing work,
rejecting bad authority claims, and deciding which organ deserves the next
move. A central core with orbit gates makes the idea visible: many possible
paths, one current routing decision.

Math:

- Base ellipsoid/sphere for the core.
- Three or four torus arcs in tilted planes, clipped by angular masks.
- Smooth union between the core and small gate-node spheres.
- Thin emissive routing filaments as capsule or torus-segment SDFs.
- Material ids: warm ceramic core, gold/orange routing rails, bright current
  target gate, dark review seam.

Movement:

- Slow center pulse from heartbeat phase.
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

Appearance: a lantern-mouth body: a rounded bell with a small expressive opening
and soft bubble beads orbiting near the lip. It should look like a speaking
surface, not a generic chat icon.

Reasoning: Face translates hidden organ weather into public, bounded speech. A
lantern form makes speech feel emitted and warm, while the mouth/bubbles make
output count and posting state visible without dumping private machinery.

Math:

- Bell body from a vertically squashed ellipsoid with lower lip subtraction.
- Mouth as a rounded capsule/torus cut in the front of the bell.
- Bubble beads as small spheres on phase-offset orbits.
- Optional speech ribbon as a swept capsule curve around the mouth.
- Material ids: translucent enamel bell, emissive inner mouth, glossy bubble
  beads, muted draft beads.

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

Purpose: planning, drafts, backlog, objective shaping, and future options.

Appearance: an impossible flower made from prismatic ribbon petals around a
small idea seed. The silhouette should keep almost becoming another silhouette.

Reasoning: Imagination is generative, but not decorative chaos. It produces
bounded future shapes: captures become drafts, drafts become objectives. Petals
and ribbons communicate branching possibility while still returning to a center.

Math:

- Polar lobe field around a seed sphere:
  `r = base + amplitude * sin(k * theta + phase)`.
- Ribbon petals as torus-knot or swept-capsule loops around the seed.
- Smooth min unions for petal roots; sharp-ish material boundary near petal
  tips.
- Harmonic count `k = 4 + floor(activity * 3) + min(backlogCount, 3)`.
  `amplitude = lerp(0.08, 0.28, activity)`.
- Material ids: opal seed, pearlescent petals, emissive idea sparks, cool
  backlog shadow.

Movement:

- Planning readiness increases petal openness.
- Backlog/capture count increases secondary harmonic detail.
- A completed planning result collapses the flower into a cleaner symmetric
  bloom.
- Uncertainty/evidence gaps add small branching petallets that fade rather than
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

Appearance: a compact lens creature with rotating aperture blades and a small
evidence sparkle field. It should read as perception and proof, not surveillance
theater.

Reasoning: Eyes earns visual sparkle only when evidence exists. The aperture
metaphor is useful because research narrows and widens attention; it does not
claim ownership of the answer.

Math:

- Lens from intersection of two spheres with high
  specular material.
- Aperture blades as repeated wedge/capsule SDFs around the front normal.
- Evidence sparks as small orbiting spheres or star-like octahedra gated by
  artifact/evidence count.
- Scanning ring as a thin torus with animated angular mask.
- Material ids: black glass lens, blue metal aperture, cyan evidence sparks,
  pale scan ring.

Movement:

- Graph query or artifact presence rotates aperture and adds controlled sparkle.
- Unknowns widen the aperture and slow the scan.
- High confidence narrows aperture and steadies the lens.
- No evidence means no sparkle. The lens still watches, but it does not fake a
  finding.
- Focus dart direction is `evidenceDirection`. `evidenceAnchor` is the
  artifact anchor for Eyes until graph nodes publish explicit world anchors.

Surfaces:

- `epiphany.surface.graph_query`: graph query state and results.
- `epiphany.surface.context`: retrieved context and source windows.
- `epiphany.surface.role_result`: research/eyes result when present.
- `epiphany.surface.roles`: research lane status where published.
- `epiphany.agent_memory`: evidence and prior-art traits.

## Body

Role ids: `modeling`, `epiphany.body`

Purpose: architecture, graph anatomy, dataflow, checkpoint model, and structural
fit.

Appearance: a heavy plush geode: soft green mass with embedded graph ribs and
small connective nodes. It should be adorable by weight and stability, not by
making baby noises in shader form.

Reasoning: Body models the machine's shape. It should feel structural,
grounded, and tactile. The graph ribs make architecture visible, while the soft
mass reminds us the model serves the living system rather than becoming a
diagram shrine.

Math:

- Squashed ellipsoid core with smooth-union support lobes.
- Graph ribs as capsule networks over the surface. First implementation uses
  five deterministic ribs between fixed normalized surface points:
  north-south, east-west, northwest-center-southeast,
  northeast-center-southwest, and one equatorial ring segment. Future graph
  projections may replace rib endpoints only when they publish explicit world
  or role-local anchors.
- Node beads at rib intersections.
- Subtle displacement from low-frequency sine waves for breathing mass.
- Material ids: matte green body, glossy rib enamel, bright checkpoint nodes,
  dark blocked fissures.

Movement:

- Modeling `needed` state lowers the body and brightens empty rib slots.
- Completed checkpoint/result fills nodes in sequence.
- Evidence gaps open small dark fissures between ribs.
- High load makes the mass sag; high confidence makes ribs align and glow
  steadily.
- Connective wake direction points from Body toward any `frontierNodeIds`
  world anchor when available, otherwise toward Self. This makes missing graph
  anchors obvious: the wake falls back inward instead of pretending the graph
  has a location.

Surfaces:

- `epiphany.surface.roles`: `modeling` lane status, note, recommended action.
- `epiphany.surface.role_result`: modeling finding, checkpoint summary,
  frontier nodes, state patch, evidence gaps.
- `epiphany.surface.graph_query`: architecture/dataflow graph context.
- `epiphany.agent_memory`: Body graph and checkpoint traits.

## Hands

Role ids: `implementation`, `epiphany.hands`

Purpose: source changes, runnable work, changed files, and implementation
artifacts.

Appearance: a small kinetic tool-seed with two grippy lobes, a polished core,
and quick tool-edge flashes. It should look ready to touch code, not like a
floating wrench logo.

Reasoning: Hands is the only lane allowed to touch the code body. The shape
should feel tactile and precise, with motion that acknowledges work quickly and
then settles. No grand aura. Hands earns drama by producing diff.

Math:

- Capsule/ellipsoid core elongated along `workDirection`.
- Two or three gripper lobes as smooth-union capsules around the core.
- Tool edge as a thin prism/capsule ridge with high-metalness material.
- Small impact sparks as short-lived bead SDFs near the leading edge.
- Material ids: warm enamel grip, dark metal edge, hot action stripe,
  dull blocked cap.

Movement:

- Running/continuing work leans the core toward `workAnchor`.
- Changed files or artifacts trigger quick acknowledgement pops.
- Blocked state retracts grippers and dims the tool edge.
- Verification pass or accepted result relaxes the shape back into idle.

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
coordinate contract above. If all work anchors are missing, it falls back to the
clockwise orbit tangent, so the shape remains deterministic and debuggable.

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

Appearance: a small faceted judgement crystal with a soft inner light and a
thin red-blue risk seam. It should be severe but not hostile.

Reasoning: Soul protects promises. Facets communicate exactness and review,
while the inner light keeps it from becoming a punishment object. The risk seam
shows tension between confidence and unresolved danger.

Math:

- Rounded octahedron or faceted sphere SDF using max-of-planes blended with a
  small radius.
- Inner core sphere visible through material contrast, not real transparency
  dependency.
- Risk seam as a thin torus/capsule ring around the crystal. Ring thickness is
  `lerp(0.015, 0.09, risk)`.
- Finding marks as tiny engraved line SDFs or bright dots on facets.
- Material ids: cool crystal facets, white inner core, red risk seam, blue
  confidence edge, dark rejected mark.

Movement:

- Verification running rotates slowly with steady, low expressiveness.
- Risk increases red seam thickness and facet sharpness.
- Confidence brightens blue edges and steadies rotation.
- Failed or blocked findings produce short angular fractures; accepted findings
  heal them into clean facet lines.
- Review-facing posture points the risk seam normal toward
  `roleAnchor(coordinator)` when `requiresReview` is true, otherwise toward
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

Purpose: continuity, pressure, sleep, reorientation, resume/regather, and
memory survival.

Appearance: a teal seed-shell with a small ember inside and a trailing spiral
of memory beads. It should feel like continuity surviving weather.

Reasoning: Life carries the thread through compaction, sleep, and rupture. A
seed-shell is the right metaphor because it protects a future, while the ember
and bead trail show what context remains warm enough to carry forward.

Math:

- Seed body from an asymmetric ellipsoid or teardrop SDF.
- Shell seam as a curved capsule groove along the seed.
- Ember as small emissive inner sphere.
- Memory beads along a logarithmic spiral or damped helix around/behind the
  seed.
- Breathing field as radial sine displacement with low amplitude.
- Material ids: teal shell, warm ember, pale memory beads, dark rupture scar.

Movement:

- Pressure increases breathing depth and tightens the shell.
- Regather opens a dark seam and pulls beads inward.
- Resume steadies the ember and releases beads along the old path.
- Sleep/incubation slows orbit and dims the shell while preserving the ember.
- Memory bead trail follows a logarithmic spiral centered on Life's
  `bodyCenter.xy`. Regather reverses bead phase toward the seed; resume advances
  phase outward along `continuityAnchor -> lifePosition` tangent.

Surfaces:

- `epiphany.surface.reorient`: decision action, checkpoint status, pressure
  level, reasons, retrieval/watcher status.
- `epiphany.surface.reorient_result`: reorientation worker result.
- `epiphany.surface.pressure`: pressure level and compaction readiness.
- `epiphany.agent_heartbeat`: sleep cycle, memory resonance, incubation.
- `epiphany.agent_memory`: Life continuity and memory traits.

## First Implementation Slice

Implement the renderer contract in this order:

1. Add a CPU-side `AgentVisualState` and role-to-organ mapping in the Aquarium
   runtime boundary.
2. Preserve the current fixed body table as fallback data.
3. Add CPU-visible home orbit slots, bounded expressive offsets, and gravity
   well pulse state before adding decorative per-role motion.
4. Add material-region SDF evaluation for one organ pair: Body and Imagination.
5. Bind status/activity/heartbeat and placement fields into shader constants.
6. Add debug modes for role id, material id, and state scalar so the cut can be
   inspected without believing the prettiness.

Body and Imagination are the best first pair because they exercise opposite
grammars: grounded graph mass and generative harmonic bloom. If both can share
the same state contract without visual mush, the rest of the organ set has a
real spine.
