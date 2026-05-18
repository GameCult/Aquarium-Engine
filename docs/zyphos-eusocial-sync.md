# Zyphos and Eusocial Interbeing Sync

Zyphos simulates the world defined by the neighboring Eusocial Interbeing vault
at `E:\Projects\Eusocial Interbeing`. The vault is the setting authority.
Aquarium is the renderer and interaction testbed.

## Objective

Use Zyphos as a visible planetary demo for Eusocial Interbeing without turning
Aquarium into a second lore repository.

## Current Mechanism

- Eusocial Interbeing stores canon and design inference in Obsidian notes under
  `Eusocial Interbeing/`.
- Aquarium.Zyphos renders Zyphos, fixed-sky Umbros, atmosphere, night-side
  glints, and first-pass eclipse cadence through the shared Aquarium host and
  SDF renderer path.
- `ZyphosUmbrosSystem` owns the current render-scale binary constants:
  Zyphos radius, Umbros radius ratio, 8-Zyphos-radii separation, sea level, and
  star phase used by the shader.
- `Worlds/zyphos-first-fractal-terrain.aquageo` owns the first semantic world
  patch: solar, orbital, planetary, and lat/long domains wrap the tile terrain
  so later fractal grammar nodes have real nested reference frames instead of
  undocumented coordinate folklore.
- No automatic sync exists. Changes cross the boundary by explicit notes and
  state-map updates.

## Invariants

- Setting canon belongs in the vault.
- Aquarium docs may describe render requirements, demo scope, and questions, but
  must not become the authoritative setting bible.
- Zyphos bends to physics. If orbital mechanics, light budget, eclipse geometry,
  or climate constraints narrow an early visual idea, the demo should use that
  pressure as worldbuilding fuel rather than hiding an impossible sky in shader
  code.
- Zyphos-specific code belongs under `src/Aquarium.Zyphos`; generic renderer
  machinery belongs under Aquarium.Engine only when it serves multiple clients.
- Design inference must be labeled before it hardens into canon.
- Rendering constraints should feed back into the vault as questions, not silent
  lore edits hidden in shader code.

## First Conversation

The first Zyphos pass should stage the planet as a dimly lit close-binary world
with two legible continental systems:

- Umbros is the slightly smaller twin of Zyphos, mutually tidally locked with it
  and fixed in the sky except for precession/libration;
- the primary star is dim, so the habitable zone is close and the biosphere is
  energy-starved relative to Earth;
- the working vault baseline gives Umbros a very large apparent diameter
  (roughly 10-13 degrees for the current 8-10 Earth-radii separation candidates)
  and daily central eclipses around an hour near the relevant equatorial track;
- the biosphere is founded on mutable cellular memory exchange, so sentience and
  eusocial contracts exist across cellular, organismal, social, and ecological
  scales instead of being reserved for a few special species;

- the Airawa home continent, where living memory networks, mother trees,
  disconnected networks, and imperial memetic infrastructure create visible
  ecological-political structure;
- the Sa'auei'a continent, where nomadic reciprocity, breeding grounds, remembered
  routes, and mobile family-unit infrastructure create a different map logic.

This does not require detailed species bodies yet. The first useful render target
is planetary-scale readability: landmass logic, ecological network hints,
settlement/non-settlement patterns, and night-side signals that reflect distinct
civilizational structures.

Aquarium should treat these as lighting, sky, time-of-day, and ecological
pressure constraints. The accepted physical baseline lives in the vault note
`Eusocial Interbeing/World/Zyphos Umbros Binary System.md`.
The accepted biosphere baseline lives in
`Eusocial Interbeing/Ecology/Mutable Memory Endosymbiosis.md`.
First concrete biosphere handles live in
`Eusocial Interbeing/Ecology/Zyphos Biosphere Examples.md`.
Future inhabitant-language names should be coordinated through Weksa at
`E:\Projects\weksa`, with names derived from language-project state rather than
English label substitution.

## Feedback Path

When Zyphos work creates a worldbuilding question, update the vault note
`Eusocial Interbeing/World/Zyphos Simulation Brief.md` first. Aquarium should
then reference the settled result, not carry private lore.

When the vault changes canon that affects the demo, update this file and
`state/map.yaml` so Zyphos does not keep simulating an obsolete world because a
shader once looked nice. The shader has no voting rights.
