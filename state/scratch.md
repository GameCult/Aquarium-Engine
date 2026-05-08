# Scratch

## Current Slice

Planning and first implementation for the SDF field renderer. The core rule:
Aquarium/Aetheria clouds are local SDF media above, below, around, or enclosing
the camera, never skybox-only decoration. `docs/sdf-field-renderer-plan.md`
captures the architecture. `Aquarium.hlsl` now has a first analytic SDF cloud
prototype integrated along the camera ray before surface composition.

## Hot Context

- Root: `E:\Projects\Aquarium-Engine`
- Upstream: `https://github.com/GameCult/Aquarium-Engine.git`
- Live branch: `main`
- Main verification:
  - `dotnet build Aquarium.Engine.slnx`
  - `.\scripts\dev-reload.ps1 -Headless -RetainSlots 4`
- Prototype histories remain external references. Do not re-import their source
  trees into this engine repo.
- Current cloud prototype is deliberately not the final volume pass. Next
  renderer steps: debug views, field instance contract, then explicit local
  volume/froxel pass.
- First artifact found: near ellipsoid edges can occlude farther clouds if the
  SDF boundary emits a positive-distance absorbing veil. Keep density inside the
  sculpted container, eroded, feathered, and low-extinction.
- Follow-up fix made the march structural: intersect each analytic cloud
  ellipsoid, jump empty space to the next true entry, and sample only inside
  cloud intervals so near containers cannot eat the budget before far clouds.
- Method correction: stop iterating cloud features by agile half-step. Build the
  target renderer as a waterfall machine: field contract, broad phase, explicit
  local volume pass, composition, then acceleration. Current shader cloud code is
  temporary scaffolding only.
- Current scene correction: analytic cloud scaffold is no longer composited.
  Bodies render as solids; the Grid traces separately and blends as a transparent
  schematic overlay so future volumetrics can own actual scene mass.
- Grid transparency correction: do not use alpha blending over raymarched scene
  content. Use Aetheria-style stochastic coverage and only draw the Grid when it
  is nearer than the nearest solid body, so it does not overlay planets.
- Pulled Aetheria's `Assets/Resources/LDR_LLL1_0.png` into Aquarium as source
  PNG plus runtime `R8_UNorm` bytes. The renderer binds it as `t2` with wrapping
  point sampling for stochastic Grid coverage.
