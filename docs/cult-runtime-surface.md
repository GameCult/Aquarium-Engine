# Cult Runtime Surface

Aquarium uses CultLib directly for runtime identity and reload recovery.

## State Store

`AquariumCultStateStore` opens a `SingleFileMessagePackBackingStore` and attaches
it to a `CultCache`. The dev scripts pass:

```powershell
--cache E:\Projects\Aquarium-Engine\artifacts\dev-reload\cultcache\aquarium-client.msgpack
```

Direct runs can also use `--cache <path>` or `AQUARIUM_CULTCACHE_PATH`.
If the single-file MessagePack snapshot is unreadable at startup, Aquarium
quarantines it beside the live file with a `.corrupt-<timestamp>` suffix and
boots a fresh typed state. Schema guardrails protect valid persisted documents
whose shape has changed; they cannot rescue a truncated backing file with no
complete MessagePack envelope.

CultLib's single-file backing store writes snapshots through a same-directory
temporary file and atomic replace. That prevents process death during save from
turning the live cache path into a zero-byte startup trap.

## Live State

`AquariumLiveState` is a CultCache document:

- schema name: `epiphany.aquarium.live_state`
- schema version: `epiphany.aquarium.live_state.v0`
- record key: `aquarium-client/live-state`
- name: `aquarium-client`

It currently stores the camera target, yaw, pitch, distance, runtime time, and a
monotonic save generation. `AquariumRuntime` writes it every 0.2 seconds and
again during orderly shutdown.

## CultNet Surface

The runtime creates a `CultNetHelloMessage` from the local CultCache registry.
That gives the engine a concrete CultNet identity and a schema catalog surface
before transport work starts:

- runtime kind: `aquarium-engine`
- role: `visual-client`
- display name: `Epiphany Aquarium`
- supported document types: taken from `CultDocumentRegistry.AllDescriptors`

Transport is still the next layer. The important thing for reload is already
true: live state is a typed Cult document backed by CultLib MessagePack storage.
