# D3D12 Best Practices Audit

## Verdict

The current D3D12 path is acceptable as a bring-up scaffold. It proves the right
classes of machinery: per-frame command allocators, fence-protected reuse,
shader-visible descriptors, persistent mapped constants, explicit render target
state, and an offscreen target path.

It is not yet a production renderer architecture. The next risk is letting
smoke-pass helpers become permanent resource policy by accident.

## Good Current Decisions

- `D3D12Renderer` keeps D3D11 intact as the visual reference backend.
- Command allocators are per swapchain frame and waited on before reset.
- Shader-visible descriptor slots are not overwritten while in flight.
- Constant upload memory is persistently mapped and 256-byte aligned.
- Offscreen render target state is tracked and transitions are skipped when
  redundant.
- The D3D12 smoke path exercises shader compilation, root signature, PSO,
  descriptor table, RTV binding, barriers, copy, and present.

## Temporary Acceptable Debt

- Committed resources are used directly. Replace with D3D12MA or placed-resource
  suballocation before the renderer owns many textures/buffers.
- Descriptor arena is fixed-size and not fence-reclaimed. It should split into
  static descriptors and transient per-frame ranges.
- Upload helper is one allocation per buffer. Replace with an upload ring before
  porting large per-frame scene data.
- Backbuffer state is assumed. Wrap swapchain buffers in state-tracked frame
  resources before resize and multi-pass presentation.
- The offscreen smoke target copies to the swapchain. Keep this only if the real
  post-process graph needs it; otherwise present from the final render target
  path with no ornamental copy.

## Required Before Real Pass Migration

- A named D3D12 resource registry for render targets and buffers.
- Static and transient descriptor allocation APIs.
- Upload ring with fence-based reclamation.
- Debug names for D3D12 resources, descriptors by owner, and command lists.
- PIX markers around passes.
- Backbuffer state tracking and resize-safe recreation.

## Rule

Do not port Aquarium's scene/froxel/history passes into ad hoc D3D12 resource
creation. Build the resource machine first, then move pixels through it.
