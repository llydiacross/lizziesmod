# Dimension System Design

## Decision

Dimensions are feasible as a **single active realm**. The server can switch every connected player between the Overworld and one selected dimension, but the game exposes one global region directory, one chunk provider, and one chunk cache. Independent dimensions for different players at the same time are not feasible with this architecture.

The first supported release must be single-player only. A later server release may move every connected player together after the same transition protocol is proven reliable.

## What Is Disabled

The original experiment changed `World.worldTime`, appended a year/dimension suffix to the region directory, and flushed the active chunk cache. That did not copy a world or isolate its entities, tile entities, player state, or world-level metadata. Flux teleporters now only teleport between bedrolls or waypoints. The portal remains visible but cannot activate until this design has passed its proof-of-concept stages.

## Current Save Isolation Probe

The first proof of concept is now implemented behind the Dimensional Portal. It is single-player only and requires `ExperimentalFeatures` to be enabled. On first entry it saves the world, creates a full external safety backup, atomically clones the complete Overworld save into a sibling directory named `<save>_LizziesMod/Dimensions/SaveSnapshotTest`, switches the base save lookup, unloads the active chunks, and rebinds the existing provider to the destination region storage. Returning follows the same path back to the Overworld.

The probe requires a normal, save-backed generated world. The `Playtesting` prefab world uses `ChunkProviderGenerateFlat`, whose reload implementation is empty and therefore cannot prove save isolation; the portal rejects it before any transition starts. Use a disposable Navezgane or generated-world save instead.

The current probe isolates region-backed blocks, tile entities, dropped items, spawned NPCs, and other chunk-backed non-player entities. Player entity, inventory, quest journal, and profile data remain shared. It does not persist the active realm across a game restart. Use a disposable world and always return to the Overworld before leaving the game.

To run the Save Isolation gate:

1. Create or select a disposable single-player Navezgane or generated world with Experimental Features enabled.
2. Place a Dimensional Portal next to a distinctive block at known coordinates.
3. Activate the portal and wait for the `Save snapshot loaded` message.
4. Change the distinctive block and place a powered tile entity in the snapshot realm.
5. Return through the portal and verify the Overworld block and tile entity are unchanged.
6. Enter once more and verify the snapshot changes remain. Stop the test if any entities, chunks, or tile entities behave unexpectedly.

## Dimension Definitions And Generators

`02_LizziesMod` owns the transition, save routing, region rebinding, and a public generator registry. Generated-dimension content lives in companion mods. `LizziesMod_Backrooms` is the first add-on: it registers `backrooms-test`, owns `LizziesMod_Backrooms/Config/Dimensions.xml`, and generates its stable room-and-corridor test realm.

Each companion mod registers its generator during `IModApi.InitMod`, then loads its own definition file:

```csharp
DimensionGeneratorRegistry.Register(new DimensionGeneratorDefinition(
		"example-generated",
		DimensionSaveMode.Generated,
		GetEntryPosition,
		GenerateChunk));
DimensionRegistry.LoadDefinitions(modInstance);
```

The generator callback receives each fresh `Chunk`. Return `true` after fully writing the chunk to suppress normal terrain; return `false` to use the normal generator. The core always reloads saved region chunks first, so generated chunks are not invoked again after players modify them.

Definitions live in the registering mod:

```xml
<Dimensions default="BackroomsTest" defaultPriority="100">
	<Dimension id="BackroomsTest" displayName="The Backrooms (Test)" generator="backrooms-test" />
</Dimensions>
```

`defaultPriority` decides the portal destination when multiple mods provide defaults. Higher priority wins; ties use the mod name in ordinal order, so the result does not depend on initialization order. The built-in `save-snapshot` fallback has priority `0`.

The Backrooms test generator creates an isolated save shell with an empty `Region` directory, a fixed entry chamber, and a concrete foundation from Y=0 through Y=59. Future Backrooms work can add authored textures, props, lighting, ambience, and population rules inside `LizziesMod_Backrooms` without changing the core transition or storage boundaries.

On first entry, the system creates an atomic copy of the Overworld **region data only** into a staging directory, validates it, and publishes it as the dimension region directory. A dimension begins with copied terrain, player-built blocks, and supported tile entities. It does not copy ambient or spawned entities.

## Realm Transition

The transition is a server-owned state machine, not a coroutine that changes global flags mid-frame:

1. Validate that the world is single-player and the portal/dimension ID is allowed.
2. Block a second transition and capture the player return position.
3. Save the active world and wait for completion.
4. Ensure the destination save snapshot exists, creating it atomically on first use.
5. Despawn eligible non-player entities from the active realm through supported engine APIs.
6. Change the one active dimension context, rebuild the region-file manager, then reload chunks through the engine's supported reload path.
7. Verify the destination chunk and its tile entities loaded from the destination directory before unfreezing the player.
8. Spawn the destination's authored entity population from its seed/manifest.
9. Persist the active dimension and release input.

Every failure before step 6 returns to the original active context. Every failure after step 6 attempts the reverse switch; if that fails, the player remains frozen with a recoverable diagnostic instead of continuing with mixed state.

## Entity Policy

Do not copy then delete every entity. Dynamic entities are serialized independently from terrain, and they can reference players, vehicles, AI state, ownership, quests, or chunk observers. Version one treats them as dimension-local population:

- Never carry zombies, animals, vehicles, dropped items, or other non-player entities across realms.
- Keep player entities, inventory, quest journal, party state, and profile data shared.
- Spawn new dimension entities deterministically from a manifest seed and explicit spawn definitions only after the destination chunks load.
- Keep player-built tile entities only after their save/load behavior has passed the tile-entity proof of concept.

The entity proof of concept must find a supported way to enumerate and despawn every non-player entity, then prove that returning to the Overworld restores its original population unchanged. If that is not possible, the feature stops before release rather than patching entity save files directly.

## Proof Of Concept Gates

### 1. Region Isolation

Create a disposable dimension copy. Change a known block and a powered tile entity in the dimension, switch back, and verify the Overworld is unchanged. Reload the save and repeat in both directions. This must also prove that no stale chunk or open region-file handle survives the switch.

### 2. Entity Isolation

Place a vehicle, spawn a zombie, and drop an item in the Overworld. Enter the dimension and verify none are present. Spawn approved dimension entities, return, and verify the original Overworld entities are still present and dimension entities are absent. Repeat after save/load.

### 3. Failure Recovery

Inject failures during snapshot creation, after save, and during destination chunk load. The original world must remain loadable and no partially published dimension directory may be selected.

### 4. Multiplayer

Only consider multiplayer after the first three gates pass. Because the active region directory is global, the server must transition every connected player together. Per-player dimensions require a different server/world architecture and are out of scope.

## Implementation Order

1. Replace duplicated `TimeManager.currentDimension` and `DimensionManager.currentDimension` fields with one validated `DimensionContext` service.
2. Build an atomic `DimensionStorage` service that creates and validates save snapshots while the game storage boundaries are mapped.
3. Build a transition state machine with rollback and diagnostics; do not expose the portal yet.
4. Prove region and tile-entity isolation in a disposable test world.
5. Add the entity enumeration/despawn and deterministic population layer, then pass the entity isolation gate.
6. Re-enable the portal only for supported single-player dimensions.