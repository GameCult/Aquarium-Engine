# Aquarium Engine Doctrine

Aquarium is a native engine now. The job is not to wrap a dashboard in a nicer
skin; the job is to build a field machine that can host agents, state, sound,
and UI as living objects.

## Frame Contract

- One camera target sits on the global XY plane.
- That target is the Grid center.
- Grid radius is derived from camera zoom distance.
- Orbit changes yaw and pitch. Zoom changes distance. Pan moves the target.
- Body anchors remain in world space.
- The renderer may move the Grid visibility window, but world-space texture and
  field domains must stay stable.

## Renderer Contract

- The visible world is shader-owned.
- Traditional geometry is scaffolding only when it helps the renderer stand up.
- Raymarched bodies and Grid terrain must shade from the same field story that
  created their hits.
- Broad-phase structures may cull, bin, and skip, but they must remain
  conservative. A culling primitive does not get to edit visible geometry.
- Planet-local detail is local to the body domain. Moving a body must move its
  surface with it, not scroll the noise through the body.
- Grid texturing and weather sample world-space domains.
- HDR bloom spreads energy before tonemapping. ACES or its successor owns final
  display compression.

## Lighting Contract

- Self is the first diegetic emitter.
- Surfaces should never be brighter than the light source unless the transport
  story explicitly explains it.
- No ambient or global fill light in the default Aquarium path.
- The long-term lighting target is a field sampled by terrain, bodies,
  atmospheres, particles, and diegetic UI.
- Debug modes must expose hit coverage, normals, depth, field occupancy,
  lighting energy, and post-stack behavior before beauty work gets clever.

## UI Contract

- Debug overlay is allowed, but it is explicit chrome.
- Overlay text uses DirectWrite and stays out of the HDR scene unless a feature
  intentionally projects it into the world.
- Diegetic labels and menus should anchor to world objects and unfold locally.
- Object-owned controls beat global panels.
- Crisp text, focus, keyboard input, and accessibility are real requirements,
  not optional little manners.

## State Contract

- Runtime state is CultCache-backed and typed.
- Live reload must rehydrate from cache before pretending it survived.
- Shader reload may fail without killing the last good frame.
- Live assembly reload may fail without killing the last good runtime.
- Persistent state should be small, structured, and useful for future work.
