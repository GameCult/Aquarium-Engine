# Scratch

## Current Slice

Merged the native C# engine branch to `main`, then stripped historical branch
memorial language from the live docs. The repo now needs to stay pure: C# host,
Vortice/D3D11 renderer, HLSL shaders, CultMath/CultLib, DirectWrite overlay,
CultCache state, CultNet future transport.

## Hot Context

- Root: `E:\Projects\Aquarium-Engine`
- Upstream: `https://github.com/GameCult/Aquarium-Engine.git`
- Live branch: `main`
- Main verification:
  - `dotnet build Aquarium.Engine.slnx`
  - `.\scripts\dev-reload.ps1 -Headless -RetainSlots 4`
- Prototype histories remain external references. Do not re-import their source
  trees into this engine repo.
