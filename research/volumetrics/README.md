# Realtime Volumetrics Research

This folder preserves the renderer research notes for future Aquarium volumetric
work. Local source artifacts live in `sources/` and extracted working text lives
in `extracted-text/`; both are gitignored because the cache contains large slide
decks and copyrighted papers. The durable repo surface is the manifest and the
distilled notes.

## Files

- `source-manifest.md`: source list, URLs, local cache names, and download notes.
- `synthesis.md`: distilled lessons for Aquarium's renderer architecture.
- `recent-volumetrics-and-gpu-sdf.md`: follow-up citation/Advances scan covering
  newer volumetrics, sparse GPU SDF, Nubis, and learned field storage.
- `transcript-addendum.md`: correction pass using video subtitles where
  available; especially Dreams and GPUOpen sparse SDF.

## Current Direction

Aquarium should treat volumetrics as a renderer-owned field system, not a fog
color slapped onto the final image. The useful production pattern is a
frustum-aligned or Grid-aligned volume that stores physical-ish density,
extinction, scattering, lighting, and transmittance, then composites solids,
transparent surfaces, particles, diegetic UI, and field effects against the same
transport result.

Start small: one low-resolution field volume, one Self light source, Beer
transmittance, single scattering, stable reprojection/jitter, and explicit debug
modes. Add richer atmosphere, clouds, and streaming only after the simple field
earns its keep.

The recent follow-up scan keeps that order. Newer production work mostly says:
start with the froxel contract, use compressed distance/occupancy only when
traversal needs it, and treat sparse brick SDFs or learned probes as later field
storage tools rather than first-pass fog machinery.
