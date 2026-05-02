import PanelPlugin from 'LensStudio:PanelPlugin';
import * as Ui from 'LensStudio:Ui';
import { createAnalyzeSummary, errorToString, importManifest, readManifest } from './importer-core.js';

const PLUGIN_ID = 'com.pratik77221.unity2snap.importer';
const DEFAULT_EXPORT_FOLDER = '';
const STATE = {
    pluginSystem: null,
    connections: [],
    gui: null,
    exportPathInput: null,
    statusLabel: null
};

export class Unity2SnapImporter extends PanelPlugin {
    static descriptor() {
        return {
            id: PLUGIN_ID,
            interfaces: [],
            name: 'Unity2Snap Importer',
            description: 'Imports Unity2Snap USLS scene exports into Lens Studio.',
            dependencies: [
                Editor.Model.IModel,
                Ui.IGui
            ]
        };
    }

    constructor(pluginSystem) {
        super(pluginSystem);
        STATE.pluginSystem = pluginSystem;
        STATE.connections = [];
        STATE.gui = pluginSystem.findInterface(Ui.IGui);
        STATE.exportPathInput = null;
        STATE.statusLabel = null;
    }

    createWidget(parentWidget) {
        const widget = createUiWidget(Ui.Widget, parentWidget);
        const layout = new Ui.BoxLayout();
        layout.setDirection(Ui.Direction.TopToBottom);
        layout.spacing = 8;
        layout.setContentsMargins(12, 12, 12, 12);
        widget.layout = layout;

        const title = createUiWidget(Ui.Label, widget);
        title.text = 'Unity2Snap';
        layout.addWidget(title);

        const description = createUiWidget(Ui.Label, widget);
        description.text = 'Choose a Unity export folder that contains scene.usls.json.';
        layout.addWidget(description);

        const pathRow = new Ui.BoxLayout();
        pathRow.setDirection(Ui.Direction.LeftToRight);
        pathRow.spacing = 6;

        STATE.exportPathInput = createUiWidget(Ui.LineEdit, widget);
        STATE.exportPathInput.placeholderText = 'D:/path/to/Unity2SnapExport';
        STATE.exportPathInput.text = DEFAULT_EXPORT_FOLDER;
        pathRow.addWidgetWithStretch(STATE.exportPathInput, 1, Ui.Alignment.Default);

        const browseButton = createUiWidget(Ui.PushButton, widget);
        browseButton.text = 'Browse Folder...';
        pathRow.addWidget(browseButton);
        layout.addLayout(pathRow);

        const analyzeButton = createUiWidget(Ui.PushButton, widget);
        analyzeButton.text = 'Analyze Export';
        layout.addWidget(analyzeButton);

        const importButton = createUiWidget(Ui.PushButton, widget);
        importButton.text = 'Import Scene';
        layout.addWidget(importButton);

        STATE.statusLabel = createUiWidget(Ui.Label, widget);
        STATE.statusLabel.text = 'Ready.';
        layout.addWidget(STATE.statusLabel);

        STATE.connections.push(analyzeButton.onClick.connect(() => {
            this.analyzeExport();
        }));

        STATE.connections.push(browseButton.onClick.connect(() => {
            this.browseExportFolder();
        }));

        STATE.connections.push(importButton.onClick.connect(() => {
            this.importSceneAsync();
        }));

        return widget;
    }

    browseExportFolder() {
        try {
            const gui = STATE.gui || STATE.pluginSystem.findInterface(Ui.IGui);
            if (!gui || !gui.dialogs) {
                this.setStatus('Folder picker is unavailable. Paste the export folder path manually.');
                return;
            }

            const selectedPath = gui.dialogs.selectFolderToOpen({
                caption: 'Select Unity2Snap Export Folder',
                options: Ui.Dialogs.Options.DirectoriesOnly
            }, createDefaultDialogPath());

            const selectedText = selectedPathToString(selectedPath);
            if (!selectedText) {
                return;
            }

            STATE.exportPathInput.text = selectedText;
            this.analyzeExport();
        } catch (error) {
            this.setStatus('Browse failed: ' + errorToString(error));
        }
    }

    analyzeExport() {
        try {
            const context = readManifest(STATE.exportPathInput.text);
            this.setStatus(createAnalyzeSummary(context.manifest));
        } catch (error) {
            this.setStatus('Analyze failed: ' + errorToString(error));
        }
    }

    async importSceneAsync() {
        try {
            const context = readManifest(STATE.exportPathInput.text);
            const result = await importManifest(STATE.pluginSystem, context.exportDir, context.manifest, (message) => {
                this.setStatus(message);
            });

            this.setStatus(
                'Import complete.\n' +
                'Removed previous imports: ' + result.removedPreviousImports + '\n' +
                'Created objects: ' + result.createdObjects + '\n' +
                'Imported asset files: ' + result.importedAssetFiles + '\n' +
                'Generated primitive assets: ' + result.generatedPrimitiveAssets + '\n' +
                'Generated materials: ' + result.generatedMaterials + '\n' +
                'Instantiated primitives: ' + result.instantiatedPrimitives + '\n' +
                'Instantiated mesh assets: ' + result.instantiatedMeshes + '\n' +
                'Assigned material slots: ' + result.assignedMaterials + '\n' +
                'Lights: ' + result.createdLights + '\n' +
                'Colliders: ' + result.createdColliders + '\n' +
                'Placeholders/notes: ' + result.notes.length
            );

            console.log('[Unity2Snap] Import complete: ' + JSON.stringify(result));
        } catch (error) {
            console.error('[Unity2Snap] Import failed: ' + errorToString(error));
            this.setStatus('Import failed: ' + errorToString(error));
        }
    }

    setStatus(message) {
        console.log('[Unity2Snap] ' + message);
        if (STATE.statusLabel) {
            STATE.statusLabel.text = message;
        }
    }
}

function createUiWidget(type, parent) {
    if (type && typeof type.create === 'function') {
        return type.create(parent);
    }

    return new type(parent);
}

function createDefaultDialogPath() {
    const currentPath = STATE.exportPathInput && STATE.exportPathInput.text ? STATE.exportPathInput.text.trim() : '';
    if (currentPath) {
        return new Editor.Path(currentPath);
    }

    try {
        const model = STATE.pluginSystem.findInterface(Editor.Model.IModel);
        if (model && model.project && model.project.projectDirectory) {
            return model.project.projectDirectory;
        }
    } catch (error) {
        // Empty default is fine; Lens Studio will choose the last-used folder.
    }

    return '';
}

function selectedPathToString(path) {
    if (!path) {
        return '';
    }

    try {
        if (path.isEmpty) {
            return '';
        }
    } catch (error) {
        // Some dialog builds return a plain string.
    }

    return String(path).replace(/\\/g, '/');
}
