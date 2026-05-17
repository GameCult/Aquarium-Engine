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
- Aquarium.Zyphos renders a spherical planet, atmosphere, night-side glints, and
  moon through the shared Aquarium host and SDF renderer path.
- No automatic sync exists. Changes cross the boundary by explicit notes and
  state-map updates.

## Invariants

- Setting canon belongs in the vault.
- Aquarium docs may describe render requirements, demo scope, and questions, but
  must not become the authoritative setting bible.
- Zyphos-specific code belongs under `src/Aquarium.Zyphos`; generic renderer
  machinery belongs under Aquarium.Engine only when it serves multiple clients.
- Design inference must be labeled before it hardens into canon.
- Rendering constraints should feed back into the vault as questions, not silent
  lore edits hidden in shader code.

## First Conversation

The first Zyphos pass should stage the planet as two legible continental systems:

- the Airawa home continent, where living memory networks, mother trees,
  disconnected networks, and imperial memetic infrastructure create visible
  ecological-political structure;
- the Sa'auei'a continent, where nomadic reciprocity, breeding grounds, remembered
  routes, and mobile family-unit infrastructure create a different map logic.

This does not require detailed species bodies yet. The first useful render target
is planetary-scale readability: landmass logic, ecological network hints,
settlement/non-settlement patterns, and night-side signals that reflect distinct
civilizational structures.

## Feedback Path

When Zyphos work creates a worldbuilding question, update the vault note
`Eusocial Interbeing/World/Zyphos Simulation Brief.md` first. Aquarium should
then reference the settled result, not carry private lore.

When the vault changes canon that affects the demo, update this file and
`state/map.yaml` so Zyphos does not keep simulating an obsolete world because a
shader once looked nice. The shader has no voting rights.
