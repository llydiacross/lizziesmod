# LizziesMod

LizziesMod adds a bunch of awesome stuff and implements a powerful mod settings system. Enable/Disable mods in game! Create mod packs! Travel through time! Play with a physgun!

**For 7 Days To Die v3.0+**

## Credits & Special Thanks

Thanks to https://github.com/OCB7D2D/OcbCustomTexturesPaints for their work on custom textures

## Spawnable Props

Enable `LizziesMod_PropSpawner`, then enter a Creative Mode world. Select the Prop Spawner icon in the Creative Menu header to open its catalog. Choose a category or search by name, then select a prop tile to spawn a physics prop at the point you are aiming at. Use **Undo Last Prop** to remove your most recently spawned prop, or **Remove My Props** to remove every prop you spawned.

The spawner is restricted to server administrators by default. An administrator can change this, the spawn distance, and the per-player/world limits from **Mod Settings**. Props are owned by the player who spawned them, and the limits default to 30 props per player and 150 props in the world.

Other mods can contribute props with `Config/SpawnableProps.xml`:

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
</SpawnableProps>
```

Each prop needs a unique `id` and the name of an existing block. The category is optional; an undeclared category is created automatically. `displayName`, `tags`, and `mass` are optional, with `tags` supporting catalog search.

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

## First time setup

On opening the mod, you should be able to find the new "Mod Settings" page, as well as the icons for whichever mods you have installed on your system.
If you can click into the menu, then you have successfully installed the mod! :D

