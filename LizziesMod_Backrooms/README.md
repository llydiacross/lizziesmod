# LizziesMod: Backrooms

This is a generated-dimension add-on for `02_LizziesMod`. It registers the `backrooms` generator and declares the `Backrooms` dimension in `Config/Dimensions.xml`.

The mod requires the core LizziesMod DLL. Its generator uses a concrete foundation below the configured main floor, then builds persistent Backrooms rooms and corridors above it.

`defaultPriority="100"` makes Backrooms the portal destination while this add-on is installed. Lower-priority definitions remain available but are not selected by the current portal UI.

## Access Terminal

The **Backrooms Access Terminal** is a direct portal to The Backrooms. Craft it at a workbench with 3 Flux Cells, 12 Electrical Parts, 8 Mechanical Parts, and 6 Forged Steel. It uses a native industrial control-panel model, and its action always targets Backrooms regardless of the currently configured default portal destination.

## Layout Settings

The **LizziesMod_Backrooms** entry in Mod Settings exposes these restart-required values:

- `MainFloorY` sets the normal room floor height. It accepts `16` through `200`.
- `StoreyHeight` sets both room clearance and the upper-storey elevation. It accepts `4` through `7`.
- `PitDepth` sets the distance below `MainFloorY` for internal pit-wall footings. Pit floors sit one additional block lower, creating crawl space beneath those walls. It accepts `4` through `12`, limited by the chosen main-floor height.
- `BasementCorridorHeight` sets the clear height at pit entrances. It accepts `3` through one less than `PitDepth`.
- `SplitLevelDepth` sets the deepest point of split-level rooms. It accepts `1` through `4`.

The pit-floor height is derived as `MainFloorY - PitDepth - 1`, so those settings remain consistent with the crawl space. Settings affect newly generated chunks only. After changing them, restart the client and recreate the generated Backrooms dimension (or delete its saved `Region` directory) before testing the new layout.

## Exploration

Pit macro-rooms use deterministic layouts including grid wells, service trenches, ring walkways, and larger corner hazards. Their lower service nooks vary between abandoned workstations, supply caches, and electronics crates that use the game's native loot system. Ambient chairs, computers, and lamps appear more often in ordinary, pit, and two-storey rooms while preserving clear entry and outer-door space.