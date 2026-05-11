# Vortice Spine

Aquarium starts from a thin native spine:

- `Platform.Win32Window` owns window creation and the message pump.
- `Render.D3D12Renderer` owns the D3D12 device, command queue, swapchain,
  frame resources, render targets, shaders, and present path.
- `AquariumRuntime` owns simulation state and emits one `AquariumFrame` per
  tick.
- `OrbitCameraRig` and `GridFrame` preserve the first invariant: the camera
  target is the Grid center, and Grid radius derives from zoom distance rather
  than pitch, yaw, or screen projection.

## Why Vortice

Vortice gives direct access to D3D/DXGI without forcing an engine protocol on
top. It is low ceremony enough to build the renderer Aquarium needs while
keeping C# and Rider as the everyday working environment.

## Current Renderer

The renderer owns the visible Aquarium frame:

1. Camera and Grid constants come from the live runtime.
2. A Grid-space height pass renders the gravity/terrain height target.
3. A fullscreen D3D12 HLSL pass traces Self, planets, cursor, and the Grid event
   lane.
4. HDR bloom and presentation run before the swapchain is handed to the overlay.
5. DirectWrite overlay text and debug UI draw after the scene pass.

No framework-owned deferred path or ambient light is hiding under the floor.
When richer lighting returns, it enters through an Aquarium-owned renderer
contract, not convenience globals.
