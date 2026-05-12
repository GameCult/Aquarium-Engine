# Scratch

## Current Slice

Aquarium's active focus is clean engine architecture and shiny Epiphany agent
visuals.

Keep the engine/client boundary strict:

- Aquarium.Engine owns Win32, D3D12, input, reload, renderer abstractions,
  debug UI plumbing, and device/toolchain integration.
- Aquarium.Epiphany owns client policy, semantic state interpretation, role
  layout, and render-plan configuration through Aquarium.Engine.Contracts.
- AquariumSynthCSharp owns synth internals and tests. Aquarium should remember
  only the hosting boundary.

The renderer work that matters next:

- Continue replacing legacy fixed D3D12 pass execution with the declared render
  graph.
- Keep body/SDF proxy passes bounded, per-object, and client-authored.
- Build role visuals from coherent low-cost form models before ornament.
- Preserve debug UI as shared chrome and input capture, not parallel panels.

## Verification

- `dotnet build Aquarium.Engine.sln --no-restore`
- `.\scripts\verify-boundaries.ps1`
- `.\scripts\dev-reload.ps1 -Headless -RetainSlots 4`
