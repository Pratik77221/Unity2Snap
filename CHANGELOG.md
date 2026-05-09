# Changelog

## 0.1.0 - Initial Public MVP

- Renamed and structured the project as `Unity2Snap - Import Unity Scenes to Snapchat`.
- Moved the Unity Package Manager package into `UnityPackage/`.
- Kept the Lens Studio plugin modules in `LensStudioPlugin/`.
- Added root README, poster media, package screenshots, GitHub install paths, and setup instructions.
- Added root compatibility `package.json` for Unity projects still referencing the old local package path.
- Kept long-form documentation at repo root under `docs/` and removed optional Unity package `Documentation~` content.
- Added USLS manifest schema models and Unity editor export window.
- Added active scene scanning, migration reports, warning summaries, and stable source IDs.
- Added Unity-side pre-export scene analysis that builds the manifest in dry-run mode before files are written.
- Reworked the Unity exporter window with a top Analyze/Export panel menu.
- Exported hierarchy, transforms, enabled state, meshes, primitives, materials, textures, lights, cameras, colliders, and player/XR rig markers.
- Added selected-root parent anchors so XR/VR rig parent transforms survive export.
- Added texture copy and PNG bake fallback for Lens Studio-friendly material textures.
- Added Lens Studio importer for hierarchy reconstruction, transform application, primitive fallback meshes, generated materials, lights, camera hints, colliders, warning notes, and Spectacles origin alignment.
- Added a Lens Studio `Browse Folder...` picker so users no longer need to paste export paths manually.
- Added generated Lens material assignment from Unity base colors and simple textures for imported mesh/model visuals.
- Added per-material OBJ/MTL primitive fallback generation for Lens Studio material color reliability.
- Assigned Lens-imported OBJ/MTL materials back onto primitive fallback instances instead of replacing them with a second generated material after import.
- Hardened Lens material assignment to avoid clearing material slots before assignment, which can crash some Lens Studio builds.
- Fixed generated plane primitive winding so the visible side faces upward in Lens Studio.
- Switched Lens collider shape creation to `Shape.createBoxShape`, `Shape.createSphereShape`, and `Shape.createCapsuleShape` instead of protected editor constructors.
- Removed local generated exports, sample `.scene` files, and smoke-test plugin scaffolding from the public repo layout.

## Known MVP Limits

- Unity scripts, MonoBehaviours, gameplay systems, Unity UI, VFX, post-processing, and Animator controller behavior are not converted.
- Custom shaders are reduced to base color and simple texture references.
- Arbitrary Unity meshes still need a future per-object GLB/FBX extraction pipeline for reliable visual transfer.
- Physics import is a guarded first pass and should be validated in Lens Studio.
