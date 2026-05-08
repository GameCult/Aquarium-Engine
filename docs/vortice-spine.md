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

The renderer currently owns the first visible Aquarium frame:

1. Camera and Grid constants come from the live runtime.
2. A Grid-space height pass renders the gravity/terrain height target.
3. A fullscreen HLSL pass raymarches solid Grid terrain and body SDFs.
4. A small primitive bin table skips irrelevant body checks per ray region.
5. DirectWrite overlay text draws after the scene pass.

No framework-owned deferred path or ambient light is hiding under the floor.
When lighting returns, it enters through Aquarium-owned fields.
