# Aquarium Engine Instructions

## Purpose

Aquarium Engine is the native C# runtime for the Epiphany Aquarium client. It
owns the window, renderer, input loop, live-reload boundary, persistent client
state, and eventual diegetic UI machinery.

This repo is not the Epiphany harness. The visible frame belongs to Aquarium.

## Operating Doctrine

- The renderer is the source of truth for visible world pixels.
- Keep the frame legible: explicit passes, explicit ownership, explicit state.
- Do not leave lies in the walls. When building engine systems meant to last,
  refuse half-assed shortcuts, hidden compensators, mystery state, and
  prototype-shaped production code. Prefer full-ass, research-backed,
  steel-plated resilience with padded failure boundaries so users do not cut
  themselves on sharp internals.
- This is game engine work: every millisecond counts. Do not spend runtime
  casually on avoidable JSON churn, spaghetti state, redundant ray work,
  unbounded full-resolution filters, or convenience abstractions that hide
  performance costs. If a shortcut is temporary, mark it as scaffolding and cut
  it before it becomes architecture.
- Before inventing renderer, simulation, storage, or post-processing machinery,
  check standard literature, production talks, engine docs, or proven local
  patterns. Build the durable machine, not a sequence of shy approximations that
  accrete into Jenga.
- The camera target is the Grid center. Grid radius follows zoom distance.
  Pitch, yaw, and screen projection do not resize the Grid.
- Body anchors are world-space simulation state. They do not ride with the Grid
  visibility window.
- Lighting is diegetic. Self, field sources, and Aquarium-owned transport light
  the scene; ambient convenience light is guilty until proven useful.
- Overlay UI and diegetic UI are different beasts. Overlay text can use
  DirectWrite. Scene text should become renderer-owned MSDF/SDF billboards when
  it belongs in the world.
- CultCache is the runtime memory organ. Camera state, reload state, renderer
  knobs, UI state, body state, and future Epiphany connection state should be
  typed Cult documents, not loose JSON or process-only folklore.
- CultNet is the communication surface for Epiphany agents. Add transport
  deliberately and keep authority boundaries typed.

## Persistent State

- `state/map.yaml` is the canonical project map.
- `state/memory.json` is durable engine doctrine and taste.
- `state/evidence.jsonl` stores distilled lessons that should change future
  behavior.
- `state/scratch.md` is disposable working context for the active slice.

Update these when the engine learns something durable. Delete stale guidance
instead of embalming it.

## Verification

Use focused checks:

```powershell
dotnet build Aquarium.Engine.sln
.\scripts\dev-reload.ps1 -Headless -RetainSlots 4
```

For normal iteration:

```powershell
.\scripts\dev-watch.ps1
```

For shipping:

```powershell
.\scripts\publish-win-x64.ps1 -Clean -Zip
```

## Style

Prefer small, intentional commits. Push completed work. Keep docs about the live
system; put old branch scars only in evidence when they alter future decisions.
