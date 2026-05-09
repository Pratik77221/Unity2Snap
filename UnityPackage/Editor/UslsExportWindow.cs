using System.Collections.Generic;
using System.Text;
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
        private UslsManifest lastAnalysis;
        private string lastAnalysisSummary;
        private MessageType lastAnalysisMessageType = MessageType.None;
        private int selectedPanel;
        private static readonly string[] PanelNames = { "Analyze", "Export" };

        [MenuItem("Tools/Unity2Snap/Export Active Scene")]
        private static void Open()
        {
            var window = GetWindow<UslsExportWindow>();
            window.titleContent = new GUIContent("Unity2Snap Export");
            window.minSize = new Vector2(500f, 580f);
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

            DrawPanelMenu();
            if (selectedPanel == 0)
            {
                DrawAnalyzePanel();
            }
            else
            {
                DrawExportPanel();
                DrawSettingsSection();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawPanelMenu()
        {
            EditorGUILayout.Space(8f);
            selectedPanel = GUILayout.Toolbar(selectedPanel, PanelNames, GUILayout.Height(32f));
        }

        private void DrawAnalyzePanel()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Analyze", EditorStyles.boldLabel);

            if (GUILayout.Button("Analyze Scene", GUILayout.Height(32f)))
            {
                Analyze();
            }

            EditorGUILayout.Space(4f);
            if (lastAnalysis == null)
            {
                EditorGUILayout.HelpBox("Preview object counts, import risks, and warnings before exporting.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(lastAnalysisSummary, lastAnalysisMessageType);
                DrawWarningPreview(lastAnalysis);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawExportPanel()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Export", EditorStyles.boldLabel);

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

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(outputDirectory)))
            {
                if (GUILayout.Button("Export Active Scene", GUILayout.Height(32f)))
                {
                    Export();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSettingsSection()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Export Settings", EditorStyles.boldLabel);
            DrawScopeSection();
            DrawContentSection();
            DrawConversionSection();
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

        private void Analyze()
        {
            var settings = CreateSettings();
            try
            {
                EditorUtility.DisplayProgressBar("Unity2Snap", "Analyzing active scene...", 0.5f);
                var result = UslsSceneExporter.AnalyzeActiveScene(settings);
                EditorUtility.ClearProgressBar();

                StoreAnalysis(result.Manifest);
                Debug.Log("[Unity2Snap] Analysis complete\n" + lastAnalysisSummary);
            }
            catch (System.Exception exception)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Unity2Snap Analysis Failed", exception.Message, "OK");
            }
        }

        private void Export()
        {
            var settings = CreateSettings();

            try
            {
                EditorUtility.DisplayProgressBar("Unity2Snap", "Analyzing scene before export...", 0.25f);
                var analysisResult = UslsSceneExporter.AnalyzeActiveScene(settings);
                StoreAnalysis(analysisResult.Manifest);

                if (analysisResult.Manifest.stats.errorCount > 0)
                {
                    EditorUtility.ClearProgressBar();
                    var shouldContinue = EditorUtility.DisplayDialog(
                        "Unity2Snap Analysis Found Errors",
                        lastAnalysisSummary + "\n\nContinue exporting anyway?",
                        "Export Anyway",
                        "Cancel");

                    if (!shouldContinue)
                    {
                        return;
                    }
                }

                EditorUtility.DisplayProgressBar("Unity2Snap", "Exporting USLS manifest...", 0.65f);
                var result = UslsSceneExporter.ExportActiveScene(settings);
                EditorUtility.ClearProgressBar();

                StoreAnalysis(result.Manifest);

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

        private UslsExportSettings CreateSettings()
        {
            return new UslsExportSettings
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
        }

        private void StoreAnalysis(UslsManifest manifest)
        {
            lastAnalysis = manifest;
            lastAnalysisSummary = CreateAnalysisSummary(manifest);
            lastAnalysisMessageType = GetAnalysisMessageType(manifest);
            Repaint();
        }

        private static MessageType GetAnalysisMessageType(UslsManifest manifest)
        {
            if (manifest.stats.errorCount > 0)
            {
                return MessageType.Error;
            }

            if (manifest.stats.warningCount > 0)
            {
                return MessageType.Warning;
            }

            return MessageType.Info;
        }

        private static string CreateAnalysisSummary(UslsManifest manifest)
        {
            var stats = manifest.stats;
            var builder = new StringBuilder();
            builder.AppendLine("Scene: " + manifest.exporter.sceneName);
            builder.AppendLine("Objects: " + stats.objectCount + " | Meshes: " + stats.meshObjectCount + " | Primitives: " + stats.primitiveObjectCount);
            builder.AppendLine("Lights: " + stats.lightCount + " | Camera hints: " + stats.cameraHintCount + " | Colliders: " + stats.colliderCount + " | Player spawns: " + stats.playerSpawnCount);
            builder.AppendLine("Materials: " + stats.materialCount + " | Textures: " + stats.textureCount + " | Triangles: " + stats.totalTriangles);
            builder.AppendLine("Warnings: " + stats.warningCount + " | Errors: " + stats.errorCount + " | Notices: " + stats.noticeCount);
            builder.AppendLine("Importable asset references: " + CountImportableAssets(manifest));
            return builder.ToString().TrimEnd();
        }

        private static int CountImportableAssets(UslsManifest manifest)
        {
            var count = 0;
            for (var i = 0; i < manifest.assets.Count; i++)
            {
                if (!string.IsNullOrEmpty(manifest.assets[i].path))
                {
                    count++;
                }
            }

            return count;
        }

        private static void DrawWarningPreview(UslsManifest manifest)
        {
            var shown = 0;
            for (var i = 0; i < manifest.warnings.Count; i++)
            {
                var warning = manifest.warnings[i];
                if (warning.severity == "info")
                {
                    continue;
                }

                if (shown == 0)
                {
                    EditorGUILayout.LabelField("Top Warnings", EditorStyles.boldLabel);
                }

                EditorGUILayout.LabelField(warning.code + " - " + warning.objectPath, EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(warning.message, EditorStyles.wordWrappedLabel);
                shown++;

                if (shown >= 5)
                {
                    break;
                }
            }

            if (manifest.stats.warningCount > shown)
            {
                EditorGUILayout.LabelField("+" + (manifest.stats.warningCount - shown) + " more warnings in the export report.", EditorStyles.miniLabel);
            }
        }
    }
}
