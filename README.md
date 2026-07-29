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

Inputs also expose `Held` through `WasTriggered(..., CustomInputTrigger.Held)` or `IsHeld(...)`. `category` and `description` are retained as metadata for a future controls screen. Invalid declarations are reported through the XML diagnostics window; matching key groups in separate mods are allowed but generate a warning. These bindings use the mod input registry and are not yet player-rebindable through the native game controls menu.

## Spawn Menu

Enable `LizziesMod_PropSpawner`, then enter a Creative Mode world. Press `Ctrl+P` to open the Spawn Menu directly, or select its icon in the Creative Menu header. The **Props**, **Ragdolls**, and **Entities** tabs each have their own category list and search results. Select a tile to spawn it at the point you are aiming at. **Undo Last Spawn** removes your latest Spawn Menu item, while **Remove My Spawns** removes every item you created through the menu.

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
<customTextures>
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
</customTextures>
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
