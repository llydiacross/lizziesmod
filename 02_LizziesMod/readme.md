# LizziesMod

For version 7 days to die v3.0+

## Credits

Uses work from https://github.com/OCB7D2D/OcbCustomTexturesPaints to implement custom textures

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