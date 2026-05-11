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
traitActivations: map of canonical_state variable -> current_activation
```

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
- Selected target role lights one gate and sends a traveling pulse along the
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
- Harmonic count `k` driven by planning pressure and activity.
- Material ids: opal seed, pearlescent petals, emissive idea sparks, cool
  backlog shadow.

Movement:

- Planning readiness increases petal openness.
- Backlog/capture count increases secondary harmonic detail.
- A completed planning result collapses the flower into a cleaner symmetric
  bloom.
- Uncertainty/evidence gaps add small branching petallets that fade rather than
  becoming permanent complexity.

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

- Lens from intersection of two spheres or a flattened ellipsoid with high
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
- Graph ribs as capsule networks over the surface, using a small fixed pattern
  at first and future graph projections later.
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

- Capsule/ellipsoid core elongated along the current work direction.
- Two or three gripper lobes as smooth-union capsules around the core.
- Tool edge as a thin prism/capsule ridge with high-metalness material.
- Small impact sparks as short-lived bead SDFs near the leading edge.
- Material ids: warm enamel grip, dark metal edge, hot action stripe,
  dull blocked cap.

Movement:

- Running/continuing work leans the core toward the current target.
- Changed files or artifacts trigger quick acknowledgement pops.
- Blocked state retracts grippers and dims the tool edge.
- Verification pass or accepted result relaxes the shape back into idle.

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
- Risk seam as a thin torus/capsule ring around the crystal.
- Finding marks as tiny engraved line SDFs or bright dots on facets.
- Material ids: cool crystal facets, white inner core, red risk seam, blue
  confidence edge, dark rejected mark.

Movement:

- Verification running rotates slowly with steady, low expressiveness.
- Risk increases red seam thickness and facet sharpness.
- Confidence brightens blue edges and steadies rotation.
- Failed or blocked findings produce short angular fractures; accepted findings
  heal them into clean facet lines.

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
3. Add material-region SDF evaluation for one organ pair: Body and Imagination.
4. Bind status/activity/heartbeat fields into shader constants.
5. Add debug modes for role id, material id, and state scalar so the cut can be
   inspected without believing the prettiness.

Body and Imagination are the best first pair because they exercise opposite
grammars: grounded graph mass and generative harmonic bloom. If both can share
the same state contract without visual mush, the rest of the organ set has a
real spine.
