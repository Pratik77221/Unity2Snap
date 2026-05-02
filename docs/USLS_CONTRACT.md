# Unity2Snap USLS Import Contract

This document describes the import behavior expected by `scene.usls.json` exports from the Unity2Snap Unity package.

## Import Order

1. Read and validate `version`.
2. Remove prior generated import roots named `Imported_Unity_Scene` when the host API supports scene object destruction.
3. Create one root object, for example `Imported_Unity_Scene`.
4. Create all manifest objects by `id`.
5. Apply parent relationships from `parentId`.
6. Apply `transform` as local transform values.
7. Import assets with a non-empty `path`.
8. Attach components based on object `type` plus optional payloads.
9. Display manifest `warnings` and `report.md`.

## Units And Coordinates

The Unity exporter writes positions in centimeters by default.

```json
"coordinateSystem": {
  "source": "unity",
  "sourceUnit": "meter",
  "target": "lens_studio",
  "targetUnit": "centimeter",
  "positionScale": 100,
  "conversion": "unity_local_to_lens_local_v0"
}
```

The v0 handedness conversion flips local Z position and reflects local rotation across Z. Validate this against a simple axis scene in Lens Studio before relying on production placement.

## Object Types

| Type | Expected Lens Import |
|---|---|
| `empty` | Empty SceneObject |
| `mesh` | SceneObject with imported render mesh where possible |
| `primitive` | Native primitive or fallback mesh |
| `light` | LightSource-style component |
| `camera_hint` | Marker object and optional disabled Camera component; do not replace Spectacles camera |
| `collider` | Physics collider/body approximation for simple shapes; otherwise metadata note |
| `player_spawn` | Marker used to offset imported scene root |

Unity UI hierarchies are excluded by default in the Unity exporter because Unity Canvas, RectTransform, TextMeshPro, and Unity UI event components do not map cleanly to Lens Studio scene objects. Enable the experimental UI toggle only when the importer has an explicit UI migration strategy.

Selected-root exports may include `empty` objects tagged `selected_root_parent_anchor`. These are transform-only ancestors, emitted so an exported XR/VR rig or spawn point keeps its full Unity world placement without exporting unrelated sibling objects.

## Player Spawn Rule

Do not move the Spectacles/device tracking origin.

Use `player_spawn` like this:

```text
sceneRoot.localPosition -= playerSpawn.worldPosition
```

The exact implementation depends on how the Lens importer applies transforms, but the concept is: move the imported Unity world so the authored spawn point lands at Lens origin. Use the spawn's full hierarchy/world position, not only its local position, so VR/XR rig parent offsets survive the export.

## Asset Rules

Assets are listed in `assets`.

- If `path` is non-empty, the Lens importer may import that file from the export folder.
- If `path` is empty and `importHint` is `metadata_only`, the Unity exporter did not write a Lens-importable file.
- Texture assets may use `importHint` `copied_source_texture` or `baked_png_texture`; import both as Lens texture assets and assign them from material texture ids.
- Meshes with `MESH_FILE_EXPORT_REQUIRED` need a future Unity-side glTF/FBX export integration.

## Material Rules

`object.material` is the first non-null material and is kept for simple importers.

`object.materials` contains all exported material slots. Importers that support multi-material meshes should prefer this list and apply each material by `slot`.

The first-pass Lens importer should create a generated material for each imported mesh material `assetId`, copy `baseColor` to the material pass `baseColor`, assign imported `albedoTextureAssetId` to common base texture slots when available, and then override instantiated mesh visuals with those generated materials. Primitive fallback assets should be generated as per-material OBJ/MTL files, then the Lens-imported MTL material should be assigned back onto the primitive fallback visual after instantiation. Do not replace primitive fallback visuals with a second generated material unless the imported OBJ material is missing. This prevents imported primitive fallback materials from rendering pink or black when the original Unity shader is not available in Lens Studio.

## Warning Rules

Warnings are part of the product surface. The Lens importer should preserve and show them after import instead of hiding conversion loss.

Important codes:

- `MONOBEHAVIOUR_SKIPPED`
- `NON_STANDARD_SHADER_FALLBACK`
- `MESH_FILE_EXPORT_REQUIRED`
- `TEXTURE_OVER_2048`
- `TEXTURE_BAKED_TO_PNG`
- `MULTI_COLLIDER_FIRST_ONLY`
- `XR_COMPONENT_SKIPPED`
- `VFX_COMPONENT_SKIPPED`
- `POST_PROCESSING_SKIPPED`
- `SELECTED_ROOT_PARENT_TRANSFORM_ANCHOR`
