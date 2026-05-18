# Scratch

## Current Slice

Aquarium's active focus is clean engine architecture and shiny Epiphany agent
visuals.

Keep the engine/client boundary strict:

- Aquarium.Engine owns Win32, D3D12, input, reload, renderer abstractions,
  debug UI plumbing, and device/toolchain integration.
- Aquarium.Epiphany owns client policy, semantic state interpretation, role
  layout, and render-plan configuration through Aquarium.Engine.Contracts.
- Aquarium consumes AquaSynth.Faust as a pinned package in the repo-local
  `packages` feed; AquaSynth.Core arrives transitively for patch editor/parser
  surfaces. AquaSynth owns synth internals and tests; update
  the package only through an intentional version bump.

The renderer work that matters next:

- Continue replacing legacy fixed D3D12 pass execution with the declared render
  graph.
- Keep body/SDF proxy passes bounded, per-object, and client-authored.
- Build role visuals from coherent low-cost form models before ornament.
- Use `research/rendering/cube-sphere-fractal-brushes.md` as the live map for
  the cube-sphere planetary domain and shared IFS brush grammar migration.
  Its IFS/CSG DSL notes now pull from neighboring VibeGeometry's `vg_grammar`
  and `vg_csg` tree work.
  Its LOD model uses cached node contribution summaries to select stable
  hierarchy cuts and stream/fade child detail only when visible pixels justify
  the cost. Contribution weights are online estimates: update a probabilistic
  subset each frame, track uncertainty and sample age, and keep conservative
  summaries as the safety floor if later learned scoring is added.
- The consolidated architecture and roadmap live at
  `research/rendering/fractal-brush-architecture-plan.md`, including module
  boundaries, mock seams, and unit/performance test strategy.
- The actionable execution checklist lives at
  `research/rendering/fractal-brush-implementation-roadmap.md`; first work
  packet is `Aquarium.Engine.Fractal` plus tests, cube-face/tile keys,
  identity/tangent projections, area distortion sampler, face-edge tests, and
  a projection report.
- Use `docs/zyphos-eusocial-sync.md` before evolving Zyphos world content.
  Eusocial Interbeing owns canon in `E:\Projects\Eusocial Interbeing`; Zyphos
  owns render constraints and feeds design questions back through the vault's
  `Eusocial Interbeing/World/Zyphos Simulation Brief.md`.
- Preserve debug UI as shared chrome and input capture, not parallel panels.

## Verification

- `dotnet build Aquarium.Engine.sln --no-restore`
- `.\scripts\verify-boundaries.ps1`
- `.\scripts\dev-reload.ps1 -Headless -RetainSlots 4`
