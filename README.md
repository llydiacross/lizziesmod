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

### Send client console commands

`02_LizziesMod/Invoke-ConsoleCommand.ps1` queues a command through a file-backed developer inbox. The running client atomically claims the request, executes it through its native console dispatcher on the main thread, removes the request file, and writes a result receipt. It does not require the game window, F1 console, or focus to be available:

```powershell
& '.\02_LizziesMod\Invoke-ConsoleCommand.ps1' 'lizziesdebug diagnostics' -WaitForResult
```

The inbox is available only when the client starts with `-DevMode`. Requests live under `02_LizziesMod/ConsoleCommandInbox/Pending`; completed result receipts are written to `Results`. `-WaitForResult` prints the exact console output and fails after `-TimeoutSeconds` if the client has not processed the request. A client restart recovers any request that was claimed during a shutdown.

When the loading screen shows **Ready to Spawn in the World**, use the native, focus-free spawn shortcut instead of clicking the button:

```powershell
& '.\02_LizziesMod\Invoke-ConsoleCommand.ps1' -SpawnWorld -WaitForResult
```

It queues a developer-only inbox spawn request; the receipt reports `queued` until the game reaches its native spawn-ready state, when it invokes the same `GameManager.DoSpawn()` action as the loading-screen button. `lizziesgame spawnstatus` reports whether a spawn request is queued and ready after the world has started.

`-KeyboardFallback` retains the former F1-keyboard path for testing an older core DLL. It requires a visible, foregroundable `7DaysToDie` window; `-ConsoleAlreadyOpen` and `-ConsoleOpenDelayMilliseconds` apply only to that fallback. `-WhatIf` never writes a request or sends keyboard input.

### LizziesMod debug commands

Run `lizziesdebug help` in the native console for the current command list. `lizziesdev` is an alias.

- `lizziesdebug dimensions` lists registered dimensions and generators, including supported and active state.
- `lizziesdebug status [dimension-id]` reports a dimension's generator, transition state, save location, and Region/archive counts.
- `lizziesdebug region [dimension-id]` reports only the generated terrain storage state.
- `lizziesdebug position` and `lizziesdebug chunk [chunk-x chunk-z]` report player coordinates and loaded chunk collision/regeneration flags.
- `lizziesdebug settings [mod-name]` lists loaded setting groups or effective values, including developer overrides and restart requirements.
- `lizziesdebug diagnostics` prints captured XML error and warning details.
- `lizziesdebug enter <dimension-id>` and `lizziesdebug return` request the normal guarded dimension transition. They use the same single-player and Experimental Features checks as the portal.
- `lizziesregendimension <dimension-id>` remains the intentional terrain-reset command. It can only run in the Overworld and takes an Overworld backup before archiving the old Region directory.

### Gameplay QA commands

Run `lizziesgame help` for the gameplay test command family. `lizziesqa` is an alias.

- `lizziesgame player`, `world`, `time`, `block [x y z]`, and `entities [radius]` inspect the current local state without changing it.
- `lizziesgame give|take|count <item-or-block> [count]` manages test inventory items. `give` and `take` validate names and report the actual result.
- `lizziesgame teleport <x> <y> <z>` moves the local player, while `lizziesgame buff add|remove|has <buff-id>` exercises buff state.
- `lizziesgame spawn list [props|entities|ragdolls] [filter]` searches the Prop Spawner catalogue. `spawn <entry-id>`, `spawn grant <prop-id>`, `spawn undo`, and `spawn clear` retain the existing admin, ownership, and enabled-setting checks.
- `lizziesgame inputs [filter]`, `textures [filter]`, `xml [items|blocks|recipes] [filter]`, and `portal` inspect the corresponding LizziesMod systems.
- `lizziesgame ui <window-name>` opens a named XUi window for local UI testing, such as `windowModSettings`, `windowModLibrary`, or `windowSpawnMenu`.

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

## XML Definition Editor

The **XML Editor** is available from the main menu and escape menu. It lists the loaded item, block, and recipe definitions with search and paging, then creates small valid generated definitions without rewriting any mod's raw `Config/*.xml` file.

Generated definitions are stored locally in the ignored `02_LizziesMod/UserXmlDefinitions.xml` file and are injected into the final item, block, or recipe XML while the game loads. A restart is required after creating or removing a definition. The editor normalizes generated names under `lizziesUser_`, so entering `exampleHammer` creates `lizziesUser_exampleHammer`.

Items inherit a selected existing item, blocks inherit a selected existing block, and recipes require an existing generated output item plus an existing item ingredient. The editor verifies those references before saving. Removing a generated definition only changes the local user file; it never modifies a downloaded or installed mod.

Selecting a definition also shows its supported fields in a table. Items and blocks expose direct `property` `name`/`value` pairs while their `Extends` value remains the base-definition field. Recipes expose `ingredient` `name`/`count` pairs and their output count. Loaded definitions are templates: creating from one writes a separate generated definition. Selecting a generated definition enables saving changes to its base, properties, or ingredients. The editor supports up to 24 editable rows and validates property names, item/block bases, recipe outputs, ingredient references, and numeric counts before writing XML.

## Mod Portal

The **Mod Portal** is available from the main menu, escape menu, and Mod Settings. It is intentionally inactive until a local endpoint is configured in the ignored `02_LizziesMod/ModPortalSettings.xml` file:

```xml
<ModPortal endpoint="https://mods.example.invalid/catalog.xml" />
```

The endpoint must use HTTPS. Refreshing the catalog is a user action; a successful catalog is cached locally as `ModPortalCatalog.cache.xml`. Portal packages are limited to XML files below `Config/`, downloaded into a staging directory, parsed, checked against their SHA-256 hashes, and then installed with a generated `ModInfo.xml`. Existing folders are never overwritten unless they were previously installed by the portal.

The catalog contract is:

```xml
<ModPortalCatalog version="1">
	<Package id="ExampleXmlMod"
					 display_name="Example XML Mod"
					 version="1.0.0"
					 description="A config-only mod."
					 author="Example Author"
					 website="https://mods.example.invalid/example"
					 game_version="3.1">
		<File path="Config/items.xml"
					url="https://mods.example.invalid/files/ExampleXmlMod/items.xml"
					sha256="0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
					size="1234" />
	</Package>
</ModPortalCatalog>
```

`id`, `version`, every file `path`, `url`, and `sha256` are required. Paths must stay under `Config/` and end in `.xml`; URLs must use HTTPS; SHA-256 values must contain 64 hexadecimal characters. DLLs, asset bundles, scripts, and arbitrary archives are rejected. After a successful install, restart the client. When a loaded profile has a missing mod with an exact cached portal ID and version match, its missing-mods screen exposes the matching portal package.

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

Books in `ModManual.xml` with `is_readme="true"` are technical readmes and appear in the Mod README library, available from the main menu and in-game. Legacy pages with only text and an optional `image` attribute remain supported. For a free-form scrollable layout, give a README page a `canvas_size` and add positioned `TextArea` and `Image` elements:

```xml
<Page title="Getting Started" canvas_size="1030,720">
	<TextArea id="intro" pos="20,0" size="855,100"><![CDATA[
Welcome to the mod.
	]]></TextArea>
	<Image id="controls" source="controls.png" pos="20,-125" size="600,338" />
	<TextArea id="notes" pos="640,-125" size="235,338"><![CDATA[
Explain the controls beside the image.
	]]></TextArea>
</Page>
```

`pos` uses XUi coordinates: positive `x` moves right and negative `y` moves down. Every element needs a unique `id`, `pos`, and positive `size`. The reader has a fixed 1030px canvas with a visible authoring area from `x="20"` through `x="875"`, so full-width content should use `pos="20,..."` and a maximum width of `855`; wider or out-of-bounds content is automatically contained instead of overflowing or clipping at the reader pane's right edge. `canvas_size` may extend the page vertically, but not horizontally. Images must be `.png`, `.jpg`, or `.jpeg` files under the owning mod's `ManualResources` folder. The shipped reader provides up to 32 text areas and 32 images per page; extra elements are reported in the game log. The page scrolls as one canvas, and its scrollbar is hidden until the content exceeds the reading viewport.

For lore or in-world guides, omit `is_readme="true"` from the book and bind its ID to a craftable item with `ItemActionOpenModManual`. Non-readme books stay out of the menu library and open only through that item:

```xml
<property class="Action0">
	<property name="Class" value="LizziesMod.ItemActionOpenModManual, LizziesMod" />
	<property name="BookId" value="guide_example_lore" />
</property>
```

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
