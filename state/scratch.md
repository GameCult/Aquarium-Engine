# Scratch

## Current Slice

Aquarium is being refocused on the Epiphany client mission. Runtime volumetrics,
cloud/fog research, and the old D3D11 renderer are shelved; Git history can hold
the fossils. Do not reintroduce them as live docs, state, shader sources, or
debug UI until the product mission actually asks for them.

Live renderer shape:

- D3D12 owns the visible frame.
- Grid height is a deferred scalar target painted by brushes.
- Grid linework is direct-traced as a separate event lane.
- Self, planets, and cursor are SDF/analytic solids.
- HDR bloom/presentation and DirectWrite overlay UI remain active.
- GraphicsSettings persists only render debug, exposure, bloom intensity, and
  bloom veil.

Immediate cleanup debt: the live volumetric behavior and user controls are cut,
but some medium-era render-target/history scaffolding still needs a second
sanitation pass through D3D12Renderer, D3D12Scene, and D3D12Post. Do that as a
lean refactor, not by preserving compatibility with removed data.

## Verification

- `dotnet build Aquarium.Engine.slnx --no-restore`
- `.\scripts\dev-reload.ps1 -Headless -RetainSlots 4`
