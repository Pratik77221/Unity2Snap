# Unity2Snap Importer

Lens Studio panel plugin for importing Unity2Snap `scene.usls.json` exports.

## Install

1. Open Lens Studio.
2. Open `Preferences > Plugins`.
3. Add the parent folder:

   ```text
   LensStudioPlugin
   ```

4. Enable `Unity2Snap Importer`.

Do not add this child folder directly. Lens Studio expects the parent plugin modules directory.

## Use

1. Export from Unity with `Tools > Unity2Snap > Export Active Scene`.
2. In Lens Studio, click `Browse Folder...` and choose the export folder containing `scene.usls.json`.
3. Confirm the analysis summary.
4. Click `Import Scene`.

## Import Scope

Implemented:

- Reads `scene.usls.json`
- Provides a `Browse Folder...` picker for selecting Unity export folders
- Creates one `Imported_Unity_Scene` root
- Removes previous generated import roots when Lens Studio allows scene object destruction
- Rebuilds hierarchy from `id` and `parentId`
- Applies local position, Euler rotation, and scale
- Imports files listed in `assets` when `path` is present
- Creates generated Lens materials from Unity base color and simple texture metadata for imported mesh/model visuals
- Generates per-material temporary OBJ/MTL assets for Unity primitives: box, plane, sphere, cylinder, and capsule
- Instantiates generated primitive assets and imported mesh/model assets when possible
- Assigns the Lens-imported OBJ/MTL material back onto primitive fallback visuals after instantiation
- Assigns generated Lens materials to imported mesh/model visuals when needed
- Creates Lens `LightSource` components for light objects
- Adds disabled camera hint markers for Spectacles/device-camera projects
- Uses Lens `Shape.createBoxShape`, `Shape.createSphereShape`, and `Shape.createCapsuleShape` for simple Unity colliders when available
- Offsets the imported root using the player/XR rig marker's world position
- Writes an import summary to the panel and Lens Studio console

Not yet implemented:

- Full Unity shader conversion
- Per-object mesh extraction from arbitrary Unity meshes
- Complex physics fidelity
- Scripts, VFX, post-processing, Animator controllers, or Unity UI migration

The importer deliberately avoids direct `.scene` text editing so Lens Studio's project model remains consistent.
