# Input

Input is currently Win32-backed and intentionally small.

## Flow

- `Win32Window` translates window messages into `InputState`.
- `AquariumHost` begins each input frame, pumps messages, then updates runtime.
- `AquariumRuntime` passes input into `OrbitCameraRig`.
- The renderer receives the updated camera and Grid frame as constants.

## Controls

- Middle mouse drag: orbit camera around the Grid center.
- Mouse wheel: exponential zoom.
- Right mouse drag: pan along the Grid plane.
- `WASD`: pan along the Grid plane.

The camera target remains the Grid center. Panning moves that shared target;
orbiting changes only yaw/pitch; zoom changes only camera distance and therefore
Grid radius.
