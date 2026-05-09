# D3D12 Synthesis

## Core Model

D3D12 is an explicit ownership API. The renderer owns command memory, resource
states, descriptor lifetime, upload lifetime, and GPU/CPU synchronization. The
driver stops hiding most of that work. Good D3D12 code is therefore less about
calling the right incantations and more about making ownership impossible to
misread.

The source material converges on a few hard rules:

- Keep root signatures stable and few. Bind a root signature once per command
  list or pass family, not as routine draw noise.
- Use descriptor heaps deliberately. Shader-visible CBV/SRV/UAV heaps are
  application-versioned memory; never overwrite descriptors still referenced by
  in-flight GPU work.
- Treat RTV/DSV descriptors differently from shader-visible descriptors. RTV/DSV
  bindings are versioned during command recording, but shader-visible heap
  contents are not protected by the runtime.
- Reset command allocators only after the GPU has completed the work recorded
  into them.
- Use fences to reclaim transient upload/descriptors/resources. A frame fence is
  the coarse tool; finer fences can protect suballocations later.
- Prefer ring/suballocation systems for transient uploads and descriptors. Fixed
  one-resource-per-use committed allocations are acceptable scaffolding, not the
  renderer we keep.
- Track resource states explicitly and emit the minimum correct barriers. Avoid
  broad generic states and redundant transitions.
- Batch or group barriers when that becomes practical; barriers and fences can
  reduce GPU parallelism.
- Keep command submissions reasonably chunky. Tiny command lists and frequent
  queue submissions pay CPU and GPU scheduling costs.
- Use PIX and vendor validation early. D3D12 mistakes are often legal-looking
  until the GPU catches fire behind a curtain.

## Descriptor Doctrine

Microsoft's binding model puts descriptor heap content lifetime squarely on the
application for shader-visible heaps. The long-term Aquarium shape should be:

- One large shader-visible CBV/SRV/UAV heap for the frame, or one persistent
  heap plus suballocated dynamic ranges.
- A CPU-visible/staging descriptor store for reusable descriptors.
- Per-frame transient descriptor ranges that are reclaimed by fence value.
- Stable descriptor ranges for long-lived textures/buffers.
- Minimal `SetDescriptorHeaps` calls. Heap changes can invalidate descriptor
  table bindings, so pass code should bind the heap once before draws.

Current Aquarium D3D12 status:

- Good: shader-visible descriptor arena exists; per-frame CBV descriptors are
  not overwritten while in flight.
- Temporary: arena is fixed and smoke-only. It must become a fence-reclaimed
  allocator before real pass/material resources arrive.

## Upload Doctrine

Transient CPU-to-GPU data belongs in persistent mapped upload memory, typically
suballocated as a ring and reclaimed by fences. Constant buffers require 256-byte
alignment. Reading directly from upload memory is acceptable for small constant
data, but large frequently-read resources should be copied to default heap
memory before shader use.

Current Aquarium D3D12 status:

- Good: upload buffer is persistently mapped and 256-byte aligned for constant
  buffer use.
- Temporary: helper is still one allocation per buffer. It should become an
  upload ring with typed allocations and fence reclamation.
- Guardrail: keep direct shader reads from upload memory limited to small
  constants. Structured/large scene data should move to default memory.

## Memory Doctrine

Committed resources are fine while bringing up a backend. They are not a
production memory manager. Microsoft recommends classifying resources and
budgeting/streaming residency; AMD recommends large heaps and suballocation,
with D3D12 Memory Allocator as the practical library path.

Current Aquarium D3D12 status:

- Good: committed default resources make ownership explicit during bring-up.
- Temporary: once scene/history/froxel resources grow, add D3D12MA or an
  equivalent placed-resource allocator instead of keeping one committed resource
  per texture/buffer.

## Barrier Doctrine

Barrier correctness is not optional, but barrier volume is a performance risk.
The renderer should track state per resource, use the narrowest valid states,
avoid redundant transitions, and group barriers when pass complexity grows.

Current Aquarium D3D12 status:

- Good: the offscreen render target tracks its current state and transitions
  only on change.
- Temporary: backbuffer state is still assumed around present/copy. That is fine
  in the current single path, but should become tracked or wrapped once resize,
  multiple passes, or alternate present paths exist.
- Watch item: rendering to an offscreen target then copying to the swapchain is
  useful for proving texture ownership, but final composition should avoid
  unnecessary full-frame copies unless a post-process graph requires them.

## Command/Fence Doctrine

Per-frame command allocators must not be reset until the GPU is done with their
recorded work. Frame resources should carry fence values, and transient
descriptor/upload/resource allocations should tie reclamation to those values.

Current Aquarium D3D12 status:

- Good: one command allocator per swapchain frame and fence wait before reuse.
- Temporary: the renderer still uses a single graphics command list and a coarse
  frame fence. That is fine until multi-threaded recording or async copy/compute
  becomes real.

## Aquarium Next Steps

1. Add a D3D12 frame graph/resource map for named render targets and buffers.
2. Replace the fixed descriptor arena with static + transient descriptor
   allocation, reclaimed by frame fence.
3. Replace one-off upload buffers with a per-frame or global upload ring.
4. Add D3D12MA or a deliberate placed-resource allocator before the resource
   count becomes serious.
5. Add debug names and PIX markers while the graph is still small.
6. Track backbuffer resources with the same state wrapper used for offscreen
   targets.
7. Port the first real pass only after descriptors, upload, render targets, and
   state tracking have durable ownership.
