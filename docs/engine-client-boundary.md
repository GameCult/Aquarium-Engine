# Engine And Client Boundary

Aquarium Engine is not the Epiphany frontend client.

`Aquarium.Engine` owns the native machinery: Win32 windowing, D3D12 device
state, swapchain presentation, resource lifetime, shader compilation, reload
transport, input translation, render target allocation, and future render graph
execution.

`Aquarium.Epiphany` owns the client meaning: CultCache runtime state, CultNet
interpretation, camera intent, cursor semantics, agent visual state, diegetic
layout rules, and the render graph configuration that turns Epiphany state into
world pixels.

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
