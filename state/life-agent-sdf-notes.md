# Life Agent SDF Notes

Reference: `artifacts/ig_0b80263584b1a81e016a051475aeac8191acb0ced03b9a3e7e.png`.
Supershape shell reference: `D:/Downloads/supershape-2026-05-14-0437-09.json`.

Working model:

- The readable form is a memory nautilus, not a generic seed.
- An ellipsoid silhouette with spiral material masks reads as wallpaper and fails the reference.
- A disc/cup welded onto the shell is worse: it enshrines the failed cut-face model.
- The source-backed model comes from `artifacts/Nautilus.glsl`: solve the nearest logarithmic spiral boundary in cylindrical shell space, then derive wall thickness, septa, aperture/cut exposure, ribs, stripes, lip, pearls, cracks, and chamber light from that solved domain.
- Do not replace the solve with a loose `sin(phase)` whorl. That was the false shortcut.
- Do not recreate the Shadertoy Nautilus. Its durable lesson is spatial-domain mapping, not the Life silhouette.
- The spec authority is a protective revolved seed/cardioid shell around a visible ember. The Nautilus spiral math should only coordinate seam, bead trail, aperture logic, and surface breathing.
- Lip, pearls, ribs, cracks, and chamber light are fields on the same shell/spiral coordinates, not separate child objects.

Latest preview used for judgment:

- `artifacts/agent-previews/Life/20260514-134914/life-front.png`
- `artifacts/agent-previews/Life/20260514-134914/life-mouth-oblique.png`
