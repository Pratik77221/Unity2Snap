# Unity2Snap Roadmap

This roadmap describes the planned version rollout for Unity2Snap. The goal is to make the bridge reliable for normal Unity scenes first, then add richer scene semantics, and only then move into Spectacles/XR-specific setup.

## v0.1 - MVP Scene Transfer

Current release.

- Export Unity scene hierarchy into `scene.usls.json`
- Import the manifest into Lens Studio
- Preserve object names, parent relationships, enabled state, and transforms
- Convert Unity meters to Lens Studio centimeters
- Export basic lights and camera hints
- Detect Unity primitives
- Create primitive fallback visuals in Lens Studio
- Export material base colors and basic texture references
- Create basic collider metadata and guarded Lens physics components
- Analyze the Unity scene before export without writing files
- Generate export reports and import warning summaries

## v0.2 - Reliable Visual Fidelity

Goal: make common Unity scenes look visually correct in Lens Studio.

- Create Lens-native primitive objects for Unity primitives wherever Lens Studio exposes stable plugin APIs
- Use generated OBJ/MTL primitive meshes only as a fallback path
- Add per-object GLB/FBX export for non-primitive Unity meshes
- Improve material mapping for Unity Built-in Standard and URP materials
- Support albedo/base color, normal, metallic, and roughness texture assignment
- Support multi-material mesh import
- Improve primitive normals, material assignment, and fallback behavior
- Improve collider shape mapping for box, sphere, capsule, and mesh-like objects
- Add asset deduplication for repeated meshes, textures, and generated primitives

Primitive strategy: Unity primitives should become Lens Studio primitives, not exported Unity mesh files. The generated OBJ/MTL primitive path exists only as a compatibility fallback until Lens Studio primitive creation is stable enough through the plugin API.

## v0.3 - Import Reliability And Workflow

Goal: make repeated export/import work feel dependable during real production.

- Add a stable reimport/update flow
- Remove previous imports safely without touching unrelated scene objects
- Preserve stable object IDs across exports
- Add pre-import validation before scene objects are created
- Improve import error messages inside Lens Studio
- Keep generated assets in cleaner, predictable folders
- Add better warning summaries grouped by object
- Add sample Unity scenes for regression testing
- Document tested Unity and Lens Studio version combinations

## v0.4 - Scene Semantics And Physics

Goal: carry more useful Unity intent into Lens Studio without pretending gameplay code is portable.

- Export Unity tags and layers
- Export static/dynamic object metadata
- Export Rigidbody summaries
- Preserve trigger/collider distinction
- Improve collider type selection in Lens Studio where the API allows it
- Add interaction markers as metadata
- Add script inventory reporting
- Group unsupported component warnings by object
- Generate clearer manual rebuild notes for scripts, animation, UI, and custom shaders

## v0.5 - Spectacles, AR, And XR Metadata

Goal: detect Unity AR/XR project intent and export enough metadata for manual Lens Studio/Spectacles reconstruction.

- Detect Unity XR Rig and XROrigin setups
- Detect player/head origin transforms
- Map authored Unity rig roots to Lens Studio/Spectacles origin placement
- Detect AR Foundation components such as `ARSession`, `ARPlaneManager`, and `ARTrackedImageManager`
- Export plane tracking and image tracking intent as metadata
- Add Spectacles performance warnings for object count, texture size, material count, and collider complexity
- Add validation checks for world scale and user-origin placement

## v1.0 - Production-Ready Unity2Snap

Goal: ship a stable migration toolkit that Unity and Lens Studio creators can trust.

- Lock a stable USLS schema version
- Add schema compatibility checks between Unity exporter and Lens Studio importer
- Provide release packages for Unity and Lens Studio
- Add sample projects and before/after demo scenes
- Add a public demo video workflow
- Publish a clear supported/unsupported feature table
- Add robust error recovery and troubleshooting docs
- Add contribution guidelines
- Maintain a tested Unity/Lens Studio compatibility matrix
