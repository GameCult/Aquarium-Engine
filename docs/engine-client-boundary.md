# Engine And Client Boundary

Aquarium Engine is not the Epiphany frontend client.

`Aquarium.Engine` owns the native machinery: Win32 windowing, D3D12 device
state, swapchain presentation, resource lifetime, shader compilation, reload
transport, input translation, render target allocation, and future render graph
execution.

`Aquarium.Engine.Contracts` owns the Vortice-free public boundary clients use to
describe runtime state, render intent, UI, audio patches, and persistent
settings. Contracts may name reusable engine concepts. They may not smuggle
Epiphany policy into the host under a nicer namespace.

`Aquarium.Epiphany` owns the client meaning: CultCache runtime state, CultNet
interpretation, camera intent, cursor semantics, agent visual state, diegetic
layout rules, and the render graph configuration that turns Epiphany state into
world pixels.

`Aquarium.Sample.Minimal` exists as the tiny smoke test. `Aquarium.Zyphos`
exists as the first real non-Epiphany demo: a planetary client with its own
runtime, controls, scene state, and SDF shaders. If a feature cannot be
explained without Epiphany or Zyphos, it is not engine API yet.

## Hard Rules

- Engine code may expose rendering abstractions. It may not know Epiphany roles,
  sub-agent identities, CultNet surface semantics, or frontend-specific layout
  policy.
- Epiphany code may configure cameras, render targets, shader passes, body
  registries, and resource bindings through engine APIs. It may not reach into
  D3D12 implementation details.
- Hot reload is transport. It is not architecture. The reload boundary exists so
  the client can change without killing the device, not so client policy can
  leak into the engine under a nicer hat.
- If a feature name includes `Self`, `Face`, `Imagination`, `Eyes`, `Body`,
  `Hands`, `Soul`, `Life`, `CultNet`, or Epiphany state semantics, the default
  home is `Aquarium.Epiphany` unless it is a data-only contract.
- If a feature owns descriptors, command lists, fences, barriers, render target
  transitions, shader object lifetime, or swapchain presentation, the default
  home is `Aquarium.Engine`.

## Next Engine API Shape

The next durable renderer cut should make the client configure a small set of
engine primitives:

- Render targets: format, dimensions, history policy, clear policy, and exported
  handles.
- Cameras: view/projection data, previous-frame data, jitter policy, and named
  consumers.
- Shader passes: shader module, root bindings, target outputs, depth policy,
  dispatch/draw shape, and reload identity.
- Scene resources: structured buffers, textures, samplers, body registries, and
  light registries behind typed handles.
- Frame graph edges: explicit read/write dependencies so pass ordering is data,
  not hidden renderer folklore.

Do not build this by piling adapters around the current monolith. First expose
the smallest real engine abstraction that lets `Aquarium.Epiphany` describe one
pass without D3D12 details. Then move one client-owned concept through it. Then
repeat.

## Repository Split Line

Aquarium should eventually be usable without carrying the Epiphany client in the
same repository. That does not mean the repo should be split before the boundary
is mechanically clean.

Keep both in this repo while the public contracts, graph declarations, shader
include layout, reload transport, and sample-client path are still changing
together. Split Epiphany out only when:

- `Aquarium.Engine` and `Aquarium.Engine.Contracts` contain no Epiphany role,
  CultNet-surface, Grid-policy, or agent-layout semantics.
- A non-Epiphany client can declare cameras, targets, shaders, UI, and
  presentation without referencing `Aquarium.Epiphany`.
- Epiphany shaders, state documents, and visual grammar live entirely on the
  client side of the contracts.
- The host can load an external client assembly without repo-local path lore.

Until those are true, a repo split would not simplify ownership. It would just
make the same leak harder to see.
