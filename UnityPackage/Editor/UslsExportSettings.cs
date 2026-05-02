using System.Collections.Generic;
using UnityEngine;

namespace Unity2Snap.Editor
{
    internal sealed class UslsExportSettings
    {
        public string OutputDirectory;
        public bool ExportSelectedRootsOnly;
        public List<GameObject> SelectedRoots = new List<GameObject>();

        public bool ExportInactiveObjects = true;
        public bool IncludeEditorOnlyObjects;
        public bool IncludeMeshMetadata = true;
        public bool IncludeMaterialMetadata = true;
        public bool IncludeColliders = true;
        public bool IncludeLights = true;
        public bool IncludeCameraHints = true;
        public bool IncludePlayerSpawnMarkers = true;
        public bool IncludeUnityUiObjects;
        public bool ConvertMetersToCentimeters = true;
        public bool ConvertUnityToLensHandedness = true;
        public bool CopySupportedSourceAssets = true;
    }

    internal sealed class UslsExportResult
    {
        public bool Success;
        public string ManifestPath;
        public string ReportPath;
        public string OutputDirectory;
        public UslsManifest Manifest;
    }
}
