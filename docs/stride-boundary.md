# Stride Boundary

Stride enters Aquarium as a package dependency, not as vendored engine source.

## What Stride Provides

- Window/device/input scaffolding.
- Game loop hosting.
- Asset and graphics infrastructure where it helps.
- A place to insert Aquarium-owned renderer passes.

## What Aquarium Owns

- Camera/Grid invariants.
- World model and simulation state.
- CultNet and CultCache boundaries.
- Renderer architecture and field ownership.
- Diegetic UI grammar.
- Debug command surface.

## First Invariant

The camera target is a point on global XY. The Grid center is that same point.
Grid radius is derived from camera zoom distance, not from camera pitch/yaw or
screen projection. Orbiting the camera must not resize the Grid.

Current code expresses this through `OrbitCameraRig` and `GridFrame`.

## Current Warnings

`Stride.CommunityToolkit.Windows` brings Stride's asset/compiler toolchain into
the restore graph. The first restore/build currently reports transitive NuGet
advisories for `Microsoft.Build.Tasks.Core`, `NuGet.Packaging`, and
`NuGet.Protocol`, plus Stride's default missing `GameSettings` warning. These
are dependency/tooling warnings, not Aquarium code warnings.

Do not hide them globally yet. Track whether newer Stride/Toolkit packages clear
the advisories before adding warning suppressions.
