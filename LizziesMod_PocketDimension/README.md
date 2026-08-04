# LizziesMod: Pocket Dimension

This companion mod is a minimal working example of a generated dimension and a block that targets that dimension directly.

It has four moving parts:

1. `Harmony/Main.cs` registers `pocket-dimension` with the shared generator registry.
2. `Config/Dimensions.xml` maps that generator to `PocketDimension`.
3. `Harmony/PocketDimensionGenerator.cs` fills every generated chunk with terrain support through Y=64 and a flat concrete floor at Y=65.
4. `Config/blocks.xml` maps a CRT television mesh to `BlockPocketDimensionPortal`, which calls `DimensionManager.TryStartDimension(player, "PocketDimension")`.

Craft the **Pocket Dimension Television** at a workbench with 2 Flux Cells, 8 Electrical Parts, 6 Mechanical Parts, and 4 Forged Steel. Place it in a normal single-player world with Experimental Features enabled. Use it in the Overworld to enter the flat realm. A matching return television is generated at the Pocket Dimension origin, eight blocks from the entry point; use it to return to the Overworld.

The generated realm is saved at `<save>_LizziesMod/Dimensions/PocketDimension`. Delete its `Region` directory before retesting changed generator code.

## Settings

`FloorY` sets the concrete floor height. It defaults to `65` and is clamped to the supported range of `16` through `240`. The setting requires a game restart. After changing it, archive or delete the existing `PocketDimension` realm so its chunks are regenerated at the new height.