# D3D12 Best Practices Audit

## Verdict

The current D3D12 path is a solid foundation for Aquarium's focused client
renderer. It proves per-frame command allocators, fence-protected reuse,
shader-visible descriptors, persistent mapped constants, explicit render-target
state, brush-rendered Grid height, HDR/bloom presentation, temporal diagnostic
targets, async shader pipeline builds, and a narrow DirectWrite overlay bridge.

## Good Current Decisions

- Command allocators are per swapchain frame and waited on before reset.
- Shader-visible descriptor slots are not overwritten while in flight.
- Constant upload memory is persistently mapped, 256-byte aligned, and allocated
  through a per-frame upload ring.
- Offscreen render target state is tracked and redundant transitions are
  skipped.
- Backbuffer state flows through the same transition helper style instead of raw
  assumed barriers.
- D3D12 objects are named, and command-list events mark the frame for
  PIX/graphics diagnostics.
- Shader-visible descriptors are split into static and per-frame transient
  arenas. Transient arenas reset only after the owning frame fence clears.
- D3D12 resources are registered by name, and capacity diagnostics report upload,
  transient descriptor, static descriptor, and RTV usage.
- The height-field target is a proper brush pass into scalar `R16_Float`.
- Transparent event lanes are not preserved as dormant scaffolding. When Grid
  linework, particles, glyph motes, or other coverage events return, they need a
  live producer/consumer contract instead of alpha-blended final-frame hacks.
- HDR bloom is pre-tonemap, multi-level, and separable.
- The D3D11On12 bridge is overlay-only, keeping native hinted text without
  mixing overlay UI into scene rendering.

## Temporary Acceptable Debt

- Committed resources are used directly. Replace with D3D12MA or placed-resource
  suballocation before the renderer owns many textures/buffers.
- Descriptor arenas are simple bump allocators. Add free lists or descriptor
  table paging only when real resource churn demands it.
- Static shader and RTV descriptor arenas rebuild wholesale on resize. This is
  deliberately simple and correct for swapchain-dependent resources.
- Upload ring is per-frame and reset after fence wait. It is not yet a global
  transient allocator with suballocation statistics or overflow diagnostics.
- Removed-pass scaffolding should stay out of the live frame. Runtime
  volumetrics, medium/froxel targets, and medium history lanes are shelved.

## Required Before Larger Renderer Work

- Add GPU timestamp queries when frame cost starts mattering beyond CPU timing
  diagnostics.
- Add descriptor-owner diagnostics beyond heap-level capacity summaries when
  descriptor churn becomes hard to reason about.
- Keep future renderer systems tied to explicit live pass contracts before
  adding new render targets or history lanes.

## Rule

Build the resource machine first, then move pixels through it. Dead experiments
belong to Git, not to the live frame graph.
