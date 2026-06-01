# Unity2Snap - Export Unity Scenes to LensStudio

![Unity2Snap poster](media/poster.png)

[![Status](https://img.shields.io/badge/status-MVP-blue)](https://github.com/Pratik77221/Unity2Snap)
[![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black)](UnityPackage/package.json)
[![Lens Studio](https://img.shields.io/badge/Lens%20Studio-5.x-yellow)](LensStudioPlugin)

Unity2Snap is a bridge for moving Unity scene layout data into Snapchat's Lens Studio, with Spectacles projects as the main target.

The core idea is simple: Unity exports a scene manifest, Lens Studio imports that manifest and rebuilds the scene hierarchy. It does not try to convert Unity gameplay code into Lens Studio scripts.

```text
Unity scene -> scene.usls.json + assets -> Lens Studio scene objects
```

## Demo [ Sound ON ]

<video src="https://github.com/user-attachments/assets/bd0a1a3a-85ae-4f84-9beb-c78440f49336" width="100%" controls></video>

## What Works

- Unity hierarchy export with stable IDs, names, enabled state, and parent relationships
- Local transforms converted from Unity meters to Lens Studio centimeters
- Selected-root parent transform anchors for XR/VR rig alignment
- Unity primitive detection: box, plane, sphere, cylinder, capsule
- Lens Studio primitive fallback mesh generation
- Unity material base color export
- Texture copy or PNG bake fallback for material textures
- Generated Lens materials assigned to imported mesh/model visuals
- Per-material OBJ/MTL fallback generation for Unity primitives
- Lights mapped to Lens `LightSource` components
- Camera hints for Spectacles/device-camera workflows
- Player/XR rig markers used to offset imported scene root
- Basic collider metadata and guarded Lens physics collider creation
- Unity-side pre-export analysis for counts, warnings, and import risks
- Human-readable export report and import warning summary

## Current Limits

- Unity scripts, MonoBehaviours, interactions, and gameplay systems are not converted
- Custom shaders are reduced to base color and simple texture references
- Complex materials, VFX, post-processing, animation controllers, and Unity UI need manual rebuilds
- Arbitrary embedded meshes still need a future per-object GLB/FBX extraction pipeline
- Physics import is first-pass only and should be validated inside Lens Studio

## Repository Layout

```text
Unity2Snap/
  UnityPackage/              Unity Package Manager package
    package.json
    Editor/
    Runtime/
  LensStudioPlugin/          Lens Studio plugin modules directory
    Unity2SnapImporter/
      module.json
      main.js
      importer-core.js
  docs/
    ROADMAP.md
    USLS_CONTRACT.md
  media/
    poster.png
    analyse_Unity.png
    export_Unity.png
    Snap_Package.png
  README.md
  CHANGELOG.md
```

## Unity Setup

Recommended:

- Unity 2021.3 LTS or newer
- Tested target workflow: Unity 6 and Lens Studio 5.x

Install from Git URL:

```text
https://github.com/Pratik77221/Unity2Snap.git?path=/UnityPackage
```

Or install from a local clone:

1. Open Unity Package Manager.
2. Click `+`.
3. Choose `Add package from disk...`.
4. Select `UnityPackage/package.json`.

If Unity reports an old dependency error for `com.unitysnapbridge.usls-exporter`, remove that entry from your Unity project's `Packages/manifest.json` and add:

```json
"com.pratik77221.unity2snap": "file:D:/A_Projects/Unity-Snap_Bridge/UnityPackage"
```

This repository also includes a root compatibility `package.json` so older local-path installs do not fail while you migrate.

Export a scene:

1. Open the Unity scene you want to export.
2. Go to `Tools > Unity2Snap > Export Active Scene`.
3. Use the tabs to switch panels:

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>Analyze Tab</h3>
      <p>Click <strong>Analyze Scene</strong> to preview object counts, active/inactive states, warnings, and import risks.</p>
    </td>
    <td width="50%" valign="top">
      <h3>Export Tab</h3>
      <p>Choose an output folder and click <strong>Export Active Scene</strong> to perform the export.</p>
    </td>
  </tr>
  <tr>
    <td valign="top">
      <img src="media/analyse_Unity.png" alt="Analyze Tab" width="100%" />
    </td>
    <td valign="top">
      <img src="media/export_Unity.png" alt="Export Tab" width="100%" />
    </td>
  </tr>
</table>


Unity writes:

```text
Unity2SnapExport/
  scene.usls.json
  report.md
  assets/
    meshes/
    textures/
```

## Lens Studio Setup

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>Lens Studio Importer Setup</h3>
      <p>Configure the plugin and import your Unity scene layout into Lens Studio:</p>
      <ol>
        <li>Open Lens Studio.</li>
        <li>Open <code>Preferences > Plugins</code>.</li>
        <li>Under <code>Additional Libraries</code>, add the <code>LensStudioPlugin</code> folder.</li>
        <li>Enable <strong>Unity2Snap Importer</strong>.</li>
        <li>Open the importer panel.</li>
        <li>Click <strong>Browse Folder...</strong> and choose the Unity export folder containing <code>scene.usls.json</code>.</li>
        <li>Review the analysis summary, then click <strong>Import Scene</strong>.</li>
      </ol>
      <p>⚠️ <em>Note: Do not add <code>LensStudioPlugin/Unity2SnapImporter</code> directly. Lens Studio expects the parent <code>LensStudioPlugin</code> directory containing the modules.</em></p>
    </td>
    <td width="50%" valign="top">
      <img src="media/Snap_Package.png" alt="Unity2Snap Lens Studio importer panel" width="100%" />
    </td>
  </tr>
</table>

## Spectacles Notes

Unity2Snap treats VR/XR rig roots as authored user-origin references. On import, the Lens Studio scene root is offset so the selected Unity player/XR rig marker lands at Lens origin. This preserves authored room-scale layout while keeping Spectacles device tracking in charge of the camera.

For best results, export the parent rig root or full scene. If you export selected children, Unity2Snap automatically includes transform-only parent anchors so world placement survives.

## Format

The shared format is `scene.usls.json`. It contains:

- `objects`: hierarchy, transforms, types, metadata
- `assets`: importable files and metadata-only assets
- `warnings`: conversion gaps and manual follow-up notes
- `stats`: counts for objects, materials, textures, lights, colliders, and warnings

See [docs/USLS_CONTRACT.md](docs/USLS_CONTRACT.md) for importer behavior and schema expectations.

## Roadmap

See [docs/ROADMAP.md](docs/ROADMAP.md) for the planned version rollout. The near-term focus is reliable visual fidelity, native Lens Studio primitive creation, safer reimport workflow, and richer scene metadata before Spectacles/XR setup.

## Repository

GitHub: https://github.com/Pratik77221/Unity2Snap
