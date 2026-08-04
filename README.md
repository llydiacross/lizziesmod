# LizziesMod

LizziesMod adds a bunch of awesome stuff and implements a powerful mod settings system. Enable/Disable mods in game! Create mod packs! Travel through time! Play with a physgun!

**For 7 Days To Die v3.0+**

## Installation

You can download a zip archive by going to the "clone" option, or alternatively going to the [releases](https://github.com/llydiacross/lizziesmod/releases) page.
Afterwards, locate the *Mods* folder for 7 days to die on your respective operating system.

### Windows

```powershell
C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Mods\
````

### Linux

```sh
~/.steam/steamapps/common/7 Days To Die/Mods
```

### OSX / MacOS

```sh
~/Library/steam/steamapps/common/7 Days To Die/Mods
 ```

## Launch 7 Days to Die for fast playtesting

`02_LizziesMod/Launch-Playtest.ps1` rebuilds the shared DLL and starts the local
client with the game's native `-loadsavegame=true` quick-continue preference.
It validates that the default disposable test save, `Limbo/test`, exists and
writes a timestamped client log under `%APPDATA%\7DaysToDie\logs`.

The game uses its last selected local save for quick-continue. Select
`Limbo/test` once through its Continue screen, then run:

```powershell
& '.\02_LizziesMod\Launch-Playtest.ps1'
```

By default, the launcher looks for the game in `C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die`. If Steam is installed on another drive or in a custom library, provide `-GameRoot` with the folder that contains `7DaysToDie.exe`:

```powershell
& '.\02_LizziesMod\Launch-Playtest.ps1' -GameRoot 'D:\SteamLibrary\steamapps\common\7 Days To Die'
```

The launcher uses this folder as the client's working directory as well. It validates the executable before building or starting the game, so a missing or incorrect path produces a direct error instead of launching from the wrong location. `-GameRoot` can be combined with any other launcher switch:

```powershell
& '.\02_LizziesMod\Launch-Playtest.ps1' -GameRoot 'E:\Games\Steam\steamapps\common\7 Days To Die' -DevMode
```

Use PowerShell's dry-run support to verify the command and paths without
building or starting the client:

```powershell
& '.\02_LizziesMod\Launch-Playtest.ps1' -WhatIf
```

Use `-MainMenu` when a save needs to be selected or created manually:

```powershell
& '.\02_LizziesMod\Launch-Playtest.ps1' -MainMenu
```

Use the companion stop script before a fresh launch when the client is still running. It force-stops `7DaysToDie` and its Easy Anti-Cheat helper, so save and exit normally when game progress matters:

```powershell
& '.\02_LizziesMod\Stop-Playtest.ps1'
```

The launch and stop scripts are designed for repeatable development loops. Automated tools, including AI coding agents, can stop a disposable client non-interactively, make and build changes, then start a new playtest:

```powershell
& '.\02_LizziesMod\Stop-Playtest.ps1' -Confirm:$false
& '.\02_LizziesMod\Launch-Playtest.ps1' -DevMode
```

## Developer Settings

Committed `ModSettings.xml` files use player-safe defaults. Local development overrides live in the ignored `02_LizziesMod/DevSettings.xml` file and apply only when the client starts in developer mode:

```xml
<DevSettings>
	<Mod name="LizziesMod">
		<Setting name="ExperimentalFeatures" value="true" />
	</Mod>
	<Mod name="LizziesMod_Backrooms">
		<Setting name="MainFloorY" value="59" />
	</Mod>
	<Mod name="LizziesMod_PocketDimension">
		<Setting name="EnableExperimentalLayout" value="true" type="bool" />
	</Mod>
</DevSettings>
```

Create DevSettings.xml inside of the 02_LizziesMod folder and put this inside to test this feature.

Run the playtest launcher with `-DevMode` to enable the overrides for that client process:

```powershell
& '.\02_LizziesMod\Launch-Playtest.ps1' -DevMode
```

Overrides require a loaded mod and validate values against existing setting types. A setting name not declared by the mod is registered for that developer session using its `value` as the default; types are inferred as `bool`, `int`, `float`, or `string`, or can be declared explicitly with `type`. Developer-defined settings are locked in the Mod Settings UI and never enter `ModSettings.xml` or saved profiles. Closing that UI or changing regular settings does not write developer values back to committed configuration.

## Setting Warnings

Settings that can alter save behavior can require confirmation before the player applies a changed value in Mod Settings:

```xml
<Setting name="ExperimentalFeatures" value="false" type="bool" requiresRestart="true" warning="true" />
```

With `warning="true"`, the player is told that the setting can make a save incompatible or unstable and is prompted to back up the save. Selecting Cancel restores the previous value; only confirmation applies the change.

## Dimensions

Dimensions are a single-player experimental feature. The game supports one active region directory, chunk provider, and chunk cache, so entering a dimension moves the whole local session between the Overworld and one selected realm; separate per-player realms are not supported.

Enable `ExperimentalFeatures` in **Mod Settings**, use a disposable normal generated or Navezgane save, then activate a Dimensional Portal. Do not use the `Playtesting` prefab world: its flat-world provider cannot reload save-backed region data. The system creates a safety backup before the first transition.

Each realm has isolated region-backed terrain, blocks, tile entities, dropped items, and spawned non-player entities. Player inventory, quests, profile data, and character state remain shared. Return to the Overworld before exiting the game.

The core mod owns transitions and save storage. Companion mods add dimensions by implementing `IDimensionGenerator` and registering it during `IModApi.InitMod`. Inherit `GeneratedDimensionGeneratorBase` for generated realms: it handles one-time initialization, required-block caching, clamped integer settings, terrain-column helpers, stability columns, and final chunk state.

```csharp
public sealed class ExampleDimensionGenerator : GeneratedDimensionGeneratorBase
{
	public override string Id { get { return "example-generated"; } }

	protected override void Initialize()
	{
		// Read settings and prepare generator state once.
	}

	protected override Vector3 GetEntryPositionCore(DimensionDefinition definition, Vector3 defaultPosition)
	{
		return defaultPosition;
	}

	protected override bool GenerateChunk(Chunk chunk)
	{
		// Write the complete chunk, then call FinalizeGeneratedChunk(chunk).
		return true;
	}
}

// Then, in your Main.cs
private static readonly ExampleDimensionGenerator generator = new ExampleDimensionGenerator();

// and inside of your ModInit function
if (!DimensionGeneratorRegistry.RegisterAndLoadDefinitions(modInstance, generator))
{
	Logger.Error("[ExampleDimension] Generator registration failed.");
}
```

`GenerateChunk` receives each new `Chunk`; return `true` after fully writing it to suppress normal terrain, or `false` to use the normal generator. Override `HasMainThreadWork` and `ProcessMainThread()` only when the generator needs main-thread work after chunk generation. Existing saved chunks are loaded instead of regenerated. Definitions belong to the companion mod:

```xml
<Dimensions default="ExampleDimension" defaultPriority="100">
	<Dimension id="ExampleDimension" displayName="Example Dimension" generator="example-generated" />
</Dimensions>
```

When several mods declare defaults, the highest `defaultPriority` wins. The included `LizziesMod_Backrooms` add-on provides the stable `backrooms` generator and `Backrooms` dimension. Its layout settings apply only to new generated chunks, so restart and recreate its realm after changing them.

## Custom Inputs

Every loaded mod can add `Config/CustomInput.xml`. The input is namespaced by the mod that owns the file, so names only need to be unique within that mod:

```xml
<CustomInputs>
	<Input name="openSpawnMenu"
		   category="Spawn Menu"
		   description="Open the Spawn Menu"
		   keys="Ctrl+P" />
</CustomInputs>
```

`keys` is a `+`-separated combination of keycodes. `Ctrl`, `Shift`, and `Alt` match either left or right modifier key; all other values must be Unity `KeyCode` names such as `P`, `F5`, `Mouse0`, or `Keypad1`. Every key in the group must be held. The registry derives `Pressed` and `Released` from the full group state, so releasing either the modifier or primary key correctly ends the input.

In code, subscribe to an input or query it by its owner mod and name:

```csharp
CustomInputManager.Subscribe(
	"ExampleMod",
	"openWorkbench",
	CustomInputTrigger.Pressed,
	OpenWorkbench);

if (CustomInputManager.WasTriggered(
		"ExampleMod",
		"openWorkbench",
		CustomInputTrigger.Released))
{
	CloseWorkbench();
}
```

Inputs also expose `Held` through `WasTriggered(..., CustomInputTrigger.Held)` or `IsHeld(...)`. Invalid declarations are reported through the XML diagnostics window; matching key groups in separate mods are allowed but generate a warning.

Use **Input Bindings** from the main menu or escape menu to change a binding, or select a mod in **Mod Settings** and choose **Edit Inputs**. Rebinding captures the next non-modifier key with any held `Ctrl`, `Shift`, or `Alt` modifiers; press `Escape` to cancel. **Reset** restores the mod-provided default. User overrides are stored separately in `Application.persistentDataPath/LizziesMod/CustomInputOverrides.xml`, so no mod's `CustomInput.xml` is modified. These bindings use the mod input registry rather than the native game controls menu.

## Custom Audio

Mods declare playable jukebox and Walkman tracks in `Config/CustomAudio.xml`. Audio files are never discovered from filenames alone, so every track has a stable mod-namespaced ID and explicit metadata:

```xml
<CustomAudio>
	<Track id="midnight-drive"
				 file="CustomAudio/midnight-drive.ogg"
				 title="Midnight Drive"
				 artist="Example Artist"
				 album="After Dark"
				 track_number="3" />
</CustomAudio>
```

`id` and `file` are required. `title`, `artist`, `album`, and `track_number` are optional; missing title and artist fall back to the ID and `Unknown Artist`. The `file` path must stay inside the owning mod and reference an existing `.ogg`, `.wav`, or `.mp3`. The runtime track key is `<mod name>:<id>`, for example `ExampleMod:midnight-drive`.

Custom audio needs an item for each way the track is discovered. A cassette is reusable and plays from inventory when the player owns an `itemWalkman`; a disc is consumed when used while aiming at a powered Jukebox. Both reference the same stable track key:

```xml
<item name="cassette_midnightDrive" extends="resourcePaper">
	<property class="Action1">
		<property name="Class" value="LizziesMod.ItemActionPlayCassette, LizziesMod" />
	</property>
	<property name="MediaType" value="cassette" />
	<property name="TrackName" value="ExampleMod:midnight-drive" />
</item>

<item name="musicDisc_midnightDrive" extends="resourcePaper">
	<property class="Action1">
		<property name="Class" value="LizziesMod.ItemActionInsertMusicDisc, LizziesMod" />
	</property>
	<property name="MediaType" value="disc" />
	<property name="TrackName" value="ExampleMod:midnight-drive" />
</item>
```

Each Jukebox keeps its own unlocked library in the world save. The first player to insert a disc becomes its owner and can set a visitor price in Dukes; playback is always free for that owner. The server verifies disc ownership, track unlocks, prices, and payment before broadcasting audio to nearby clients.

## Spawn Menu

Enable `LizziesMod_PropSpawner`, then enter a Creative Mode world. Press `Ctrl+P` to open the Spawn Menu directly, or select its icon in the Creative Menu header. The **Props**, **Ragdolls**, and **Entities** tabs each have their own category list and search results. Select a tile to spawn it at the point you are aiming at. **Undo Last Spawn** removes your latest Spawn Menu item, while **Remove My Spawns** removes every item you created through the menu.

Hold `Shift` while selecting a **Prop** tile to add that prop's block item to your inventory instead of spawning it. The request is validated by the server against the approved Spawn Menu catalog; entities and ragdolls cannot be added as items.

The menu is restricted to server administrators by default. An administrator can change its spawn distance and per-player/world limits from **Mod Settings**. Props, live entities, and ragdolls have independent limits. Defaults are 30 props per player / 150 world-wide, and 10 entities or ragdolls per player / 30 world-wide. Entity and ragdoll spawning can also be disabled independently.

Other mods can contribute props, entities and ragdolls with `Config/SpawnableProps.xml`:

```xml
<SpawnableProps>
	<Categories>
		<Category id="decor" name="Decor" order="40" />
	</Categories>
	<Props>
		<Prop id="decor.exampleChair"
			  block="chairWood01"
			  category="decor"
			  displayName="Example Chair"
			  tags="decor,chair,wood"
			  mass="10" />
	</Props>
	<EntityOverrides>
		<Entity entity_class="exampleWorkshopGuard"
			category="friendly"
			ragdoll_category="friendly"
			displayName="Workshop Guard"
			tags="guard,friendly,custom" />
	</EntityOverrides>
</SpawnableProps>
```

Each prop needs a unique `id` and the name of an existing block. The category is optional; an undeclared category is created automatically. `displayName`, `tags`, and `mass` are optional, with `tags` supporting catalog search.


### Spawnable Entities And Ragdolls

The **Entities** tab automatically includes every loaded vanilla or modded `entity_class` whose `UserSpawnType` is `Menu`. This uses the same eligibility setting as 7 Days to Die's built-in spawn menu, so templates and internal-only entities remain unavailable:

```xml
<entity_class name="exampleWorkshopGuard" extends="zombieTemplateMale">
	<property name="UserSpawnType" value="Menu" />
</entity_class>
```

The **Ragdolls** tab is generated from those same approved entities, but only includes classes that declare ragdoll support. A ragdoll remains alive under a hidden persistent ragdoll buff instead of using the corpse death lifecycle, so **Undo Last Spawn** and **Remove My Spawns** can remove it cleanly.

An optional `Config/SpawnMenu.xml` can customize the presentation of an approved entity without changing eligibility. Categories are tab-specific, and an entity override may set `category`, `ragdoll_category`, `displayName`, and `tags`:

```xml
<SpawnMenu>
	<Categories>
		<Category type="entities" id="friendly" name="Friendly NPCs" order="30" />
		<Category type="ragdolls" id="friendly" name="Friendly NPCs" order="30" />
	</Categories>
	<EntityOverrides>
		<Entity entity_class="exampleWorkshopGuard"
			category="friendly"
			ragdoll_category="friendly"
			displayName="Workshop Guard"
			tags="guard,friendly,custom" />
	</EntityOverrides>
</SpawnMenu>
```

The client only sends the selected catalog entry ID. The server re-resolves it from the approved catalog before creating an entity, so clients cannot request arbitrary entity classes.

### Custom Prop Models

`SpawnableProps.xml` does not load a model by itself. It turns a resolved block into a physics prop, so define the model-backed block first and then reference that block from the prop catalog. The game resolves a prefab from an asset bundle with this model reference format:

```
#@modfolder:Resources/<bundle>.unity3d?<prefab>
```

For example, put `ExampleChairPrefab` and all of its required meshes, materials, and textures in `Resources/ExampleProps.unity3d`, then add `Config/blocks.xml` to that same mod:

```xml
<configs>
	<append xpath="/blocks">
		<block name="lmExampleChair" extends="decoEntityWoodMaster">
			<property name="CreativeMode" value="None" />
			<property name="CustomIcon" value="chairWood01" />
			<property name="Shape" value="ModelEntity" />
			<property name="Model" value="#@modfolder:Resources/ExampleProps.unity3d?ExampleChairPrefab" />
			<property name="IsTerrainDecoration" value="true" />
		</block>
	</append>
</configs>
```

Then register that new block as a prop:

```xml
<Prop id="decor.exampleChair"
	  block="lmExampleChair"
	  category="decor"
	  displayName="Example Chair"
	  tags="decor,chair,custom"
	  mass="10" />
```

`@modfolder` is resolved relative to the mod that owns `blocks.xml`, and the prefab name after `?` must match the asset-bundle prefab exactly. Include the bundle and XML in the same mod package installed by every player. `CustomTextures.xml` is only for opaque block-paint textures; it does not register prefab models.

The physics prop uses each `MeshFilter` in the resolved model to create a convex `MeshCollider`. Keep collision meshes simple, split complex models into several mesh objects where necessary, and test the prop in-game before shipping it.

## Manuals

Books in `ModManual.xml` with `is_readme="true"` appear in the Mod README library, available from the main menu and in-game. Legacy pages with only text and an optional `image` attribute remain supported. For a free-form scrollable layout, give a README page a `canvas_size` and add positioned `TextArea` and `Image` elements:

```xml
<Page title="Getting Started" canvas_size="1030,720">
	<TextArea id="intro" pos="0,0" size="1010,100"><![CDATA[
Welcome to the mod.
	]]></TextArea>
	<Image id="controls" source="controls.png" pos="0,-125" size="600,338" />
	<TextArea id="notes" pos="625,-125" size="385,338"><![CDATA[
Explain the controls beside the image.
	]]></TextArea>
</Page>
```

`pos` uses XUi coordinates: positive `x` moves right and negative `y` moves down. Every element needs a unique `id`, `pos`, and positive `size`. Images must be `.png`, `.jpg`, or `.jpeg` files under the owning mod's `ManualResources` folder. The shipped reader provides up to 32 text areas and 32 images per page; extra elements are reported in the game log. The page scrolls as one canvas, and its scrollbar is hidden until the content exceeds the reading viewport.

## Custom Block Paints

LizziesMod discovers `Config/CustomTextures.xml` in every loaded mod. Each `opaque` entry becomes a paint-menu entry using the next available native paint slot, while its texture ID is appended after the existing opaque atlas mappings. Use the texture `id` in block XML:

```xml
<block name="example_custom_block">
	<property name="Texture" value="example_paint"/>
	<property name="UiBackgroundTexture" value="example_paint"/>
</block>
```

Define `example_paint` in `Config/CustomTextures.xml`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<CustomTextures>
	<opaque
		id="example_paint"
		name="Example Paint"
		bundle="Resources/Atlas.unity3d"
		diffuse="Example_Diffuse"
		normal="Example_Normal"
		specular="Example_Specular"
		group="Custom"
		paintCost="1"
		sortIndex="255"
		hidden="false" />
</CustomTextures>
```

All three assets must be in the specified bundle. They must use the same format as the live opaque diffuse, normal, and specular arrays, and they must provide a complete compatible mip chain. The game uses 512x512 atlas slices; larger source textures are accepted when a matching 512px mip level and every lower mip level are present. Invalid assets are rejected with a channel-specific log message instead of being copied partially.

## Scroll Wheel Custom Actions

Custom item actions can block the scroll wheel. Add the XML opt-in to the action:

```xml
<property class="Action0">
	<property name="Class" value="YourNamespace.ItemActionExample, YourAssembly"/>
	<property name="UsesScrollWheel" value="true"/>
</property>
```

While UsesScrollWheel is true, LizziesMod blocks wheel item cycling, previous/next slot input, toolbelt updates, and inventory item-index changes. Missing, `false`, or malformed `UsesScrollWheel` values leave the action unlocked.

## Credits & Special Thanks

Thanks to https://github.com/OCB7D2D/OcbCustomTexturesPaints for their work on custom textures
