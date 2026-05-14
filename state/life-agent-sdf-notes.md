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
- Current failure as of `20260514-141015`: the render is less egg-shaped, but the center reads as a mechanical mouth because the spiral/seam field owns too much material authority and the aperture is a bite. The next model must make the shell mass first, then use spiral coordinates for raised pale ribs and bead trail only.
- Concept target: front view should show a heavy teal shell mass with a round outer whorl, a gold inner aperture bowl on the left, a visible ember near the inner spiral, pale raised ribs arcing from the center to the outer shell, and memory beads along the spiral lip. Oblique views may reveal thickness, but must not expose detached ornaments or a hollow ring.
- Accepted direction as of `20260514-143806`: filled directional shell mass owns the silhouette, aperture carves a smooth golden bowl, ember remains visible, log-spiral relief produces pale raised ribs and rounder bead samples, and scar/groove material is subordinate. The spiral organizes the shell; it is not the shell body.

Latest preview used for judgment:

- `artifacts/agent-previews/Life/20260514-143806/life-front.png`
- `artifacts/agent-previews/Life/20260514-143806/life-mouth-oblique.png`
- `artifacts/agent-previews/Life/20260514-143806/life-three-quarter.png`
