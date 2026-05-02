# Lens Studio Plugin Modules

This folder is the Lens Studio plugin modules directory for Unity2Snap.

Add this parent folder in Lens Studio:

```text
LensStudioPlugin
```

Lens Studio discovers child folders with a `module.json` file. The production plugin is:

```text
LensStudioPlugin/
  Unity2SnapImporter/
    module.json
    main.js
    importer-core.js
```

## Setup

1. Open Lens Studio.
2. Go to `Preferences > Plugins`.
3. Add `LensStudioPlugin` under `Additional Libraries`.
4. Enable `Unity2Snap Importer`.
5. Open the importer panel and choose a Unity export folder containing `scene.usls.json`.

The importer uses Lens Studio's Editor API and `filesystem` permission. It does not edit `.scene` files directly.
