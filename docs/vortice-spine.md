# Vortice Spine

Aquarium starts from a thin native spine:

- `Platform.Win32Window` owns window creation and the message pump.
- `Render.D3D11Renderer` owns the D3D11 device, immediate context, swapchain,
  and first render target.
- `AquariumRuntime` owns simulation state and emits one `AquariumFrame` per
  tick.
- `OrbitCameraRig` and `GridFrame` preserve the first invariant: the camera
  target is the Grid center, and Grid radius derives from zoom distance rather
  than pitch, yaw, or screen projection.

## Why Vortice

Vortice gives us direct access to Direct3D/DXGI without forcing an engine
protocol on top. It is low ceremony enough to build the renderer we actually
want, while still keeping C# and Rider as the everyday working environment.

## Current Renderer

The renderer currently clears the swapchain backbuffer. That is intentionally
small. The next layers should be added in this order:

1. Camera constants and world-to-clip math.
2. One full-screen raymarch pass for solid SDF bodies.
3. Grid-space heightfield surface.
4. HDR render target, ACES tonemap, and gentle pre-tonemap bloom.
5. Diegetic labels and debug UI as separate, explicit systems.

No framework-owned deferred path or ambient light is hiding under the floor.
When lighting returns, it enters through Aquarium-owned fields.
