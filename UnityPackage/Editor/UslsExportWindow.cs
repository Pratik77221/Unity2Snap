using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity2Snap.Editor
{
    internal sealed class UslsExportWindow : EditorWindow
    {
        private string outputDirectory;
        private bool exportSelectedRootsOnly;
        private bool exportInactiveObjects = true;
        private bool includeEditorOnlyObjects;
        private bool includeMeshMetadata = true;
        private bool includeMaterialMetadata = true;
        private bool includeColliders = true;
        private bool includeLights = true;
        private bool includeCameraHints = true;
        private bool includePlayerSpawnMarkers = true;
        private bool includeUnityUiObjects;
        private bool convertMetersToCentimeters = true;
        private bool convertUnityToLensHandedness = true;
        private bool copySupportedSourceAssets = true;
        private Vector2 scroll;

        [MenuItem("Tools/Unity2Snap/Export Active Scene")]
        private static void Open()
        {
            var window = GetWindow<UslsExportWindow>();
            window.titleContent = new GUIContent("Unity2Snap Export");
            window.minSize = new Vector2(460f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(outputDirectory))
            {
                outputDirectory = UslsFileUtility.DefaultOutputDirectory;
            }
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Unity2Snap", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            EditorGUILayout.HelpBox(
                "Exports scene layout data for a later Lens Studio importer. This is not a Unity gameplay/code converter.",
                MessageType.Info);

            DrawOutputSection();
            DrawScopeSection();
            DrawContentSection();
            DrawConversionSection();
            DrawActions();

            EditorGUILayout.EndScrollView();
        }

        private void DrawOutputSection()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                outputDirectory = EditorGUILayout.TextField("Folder", outputDirectory);
                if (GUILayout.Button("Browse", GUILayout.Width(80f)))
                {
                    var selected = EditorUtility.OpenFolderPanel("Choose Unity2Snap Export Folder", outputDirectory, string.Empty);
                    if (!string.IsNullOrEmpty(selected))
                    {
                        outputDirectory = selected;
                    }
                }
            }
        }

        private void DrawScopeSection()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Scope", EditorStyles.boldLabel);
            exportSelectedRootsOnly = EditorGUILayout.ToggleLeft("Export selected roots only", exportSelectedRootsOnly);
            exportInactiveObjects = EditorGUILayout.ToggleLeft("Include inactive objects", exportInactiveObjects);
            includeEditorOnlyObjects = EditorGUILayout.ToggleLeft("Include EditorOnly tagged objects", includeEditorOnlyObjects);

            if (exportSelectedRootsOnly && Selection.gameObjects.Length == 0)
            {
                EditorGUILayout.HelpBox("No GameObjects are selected. The exporter will produce an empty object list.", MessageType.Warning);
            }
            else if (exportSelectedRootsOnly)
            {
                EditorGUILayout.HelpBox("Unselected parent transforms are exported as empty anchors so selected XR/VR rigs keep their world placement.", MessageType.None);
            }
        }

        private void DrawContentSection()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Content", EditorStyles.boldLabel);
            includeMeshMetadata = EditorGUILayout.ToggleLeft("Mesh metadata and source model references", includeMeshMetadata);
            includeMaterialMetadata = EditorGUILayout.ToggleLeft("Basic material metadata and texture references", includeMaterialMetadata);
            includeColliders = EditorGUILayout.ToggleLeft("Colliders", includeColliders);
            includeLights = EditorGUILayout.ToggleLeft("Lights", includeLights);
            includeCameraHints = EditorGUILayout.ToggleLeft("Camera hints", includeCameraHints);
            includePlayerSpawnMarkers = EditorGUILayout.ToggleLeft("Player spawn markers", includePlayerSpawnMarkers);
            includeUnityUiObjects = EditorGUILayout.ToggleLeft("Unity UI hierarchy (experimental)", includeUnityUiObjects);
            copySupportedSourceAssets = EditorGUILayout.ToggleLeft("Copy source FBX/OBJ/glTF/GLB/PNG/JPG assets when possible", copySupportedSourceAssets);
        }

        private void DrawConversionSection()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Conversion", EditorStyles.boldLabel);
            convertMetersToCentimeters = EditorGUILayout.ToggleLeft("Convert meters to centimeters", convertMetersToCentimeters);
            convertUnityToLensHandedness = EditorGUILayout.ToggleLeft("Apply Unity-to-Lens handedness conversion", convertUnityToLensHandedness);

            if (convertUnityToLensHandedness)
            {
                EditorGUILayout.HelpBox("The v0 conversion flips local Z position and reflects local rotation across Z. Validate with simple orientation test scenes before production use.", MessageType.None);
            }
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(16f);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(outputDirectory)))
            {
                if (GUILayout.Button("Export Active Scene", GUILayout.Height(36f)))
                {
                    Export();
                }
            }
        }

        private void Export()
        {
            var settings = new UslsExportSettings
            {
                OutputDirectory = outputDirectory,
                ExportSelectedRootsOnly = exportSelectedRootsOnly,
                SelectedRoots = new List<GameObject>(Selection.gameObjects),
                ExportInactiveObjects = exportInactiveObjects,
                IncludeEditorOnlyObjects = includeEditorOnlyObjects,
                IncludeMeshMetadata = includeMeshMetadata,
                IncludeMaterialMetadata = includeMaterialMetadata,
                IncludeColliders = includeColliders,
                IncludeLights = includeLights,
                IncludeCameraHints = includeCameraHints,
                IncludePlayerSpawnMarkers = includePlayerSpawnMarkers,
                IncludeUnityUiObjects = includeUnityUiObjects,
                ConvertMetersToCentimeters = convertMetersToCentimeters,
                ConvertUnityToLensHandedness = convertUnityToLensHandedness,
                CopySupportedSourceAssets = copySupportedSourceAssets
            };

            try
            {
                EditorUtility.DisplayProgressBar("Unity2Snap", "Exporting USLS manifest...", 0.5f);
                var result = UslsSceneExporter.ExportActiveScene(settings);
                EditorUtility.ClearProgressBar();

                EditorUtility.DisplayDialog(
                    "Unity2Snap Export Complete",
                    "Exported " + result.Manifest.stats.objectCount + " objects with " +
                    result.Manifest.stats.warningCount + " warnings, " +
                    result.Manifest.stats.errorCount + " errors, and " +
                    result.Manifest.stats.noticeCount + " notices.\n\n" +
                    "Manifest:\n" + result.ManifestPath,
                    "OK");

                EditorUtility.RevealInFinder(result.OutputDirectory);
            }
            catch (System.Exception exception)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Unity2Snap Export Failed", exception.Message, "OK");
            }
        }
    }
}
