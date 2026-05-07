# Epiphany Aquarium Engine

C# engine-core seed for the next Epiphany Aquarium line.

This repo starts after the web and Rust prototypes taught the expensive lesson:
the Aquarium wants an owned renderer, owned state model, and owned interaction
grammar. Stride can donate useful structure and tooling, but the engine shape is
Aquarium-first rather than framework-first.

## Intent

- C# host with renderer architecture kept legible in Rider.
- Grid-centered camera and world-space interaction invariants as first-class
  engine contracts.
- Diegetic UI surfaces driven by Aquarium objects instead of admin chrome.
- CultNet for Epiphany communication.
- CultCache for settings, persistent client state, and reload recovery.
- Procedural audio and visual state treated as coupled signals.

## Borrowed Lessons

- The web client proved the interaction grammar: objects first, panels only as
  local unfold surfaces.
- The Bevy prototype proved the renderer direction: raymarched bodies,
  Grid-space fields, HDR, bloom, and camera/Grid invariants.
- The Rust synth work proved the audio module deserves its own small, sharp API.

## First Build Target

Create the smallest C# executable that opens a window, owns the camera/Grid
invariant, and renders one sun plus orbiting planets with diegetic labels.
Everything else earns its way back in.
