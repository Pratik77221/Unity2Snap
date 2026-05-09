using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity2Snap.Editor
{
    internal static class UslsSceneExporter
    {
        public static UslsExportResult AnalyzeActiveScene(UslsExportSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var analysisSettings = settings.Clone();
            NormalizeSettings(analysisSettings);
            analysisSettings.AnalyzeOnly = true;

            var manifest = BuildManifest(analysisSettings);
            return new UslsExportResult
            {
                Success = true,
                Manifest = manifest,
                OutputDirectory = analysisSettings.OutputDirectory,
                ManifestPath = Path.Combine(analysisSettings.OutputDirectory, "scene.usls.json"),
                ReportPath = Path.Combine(analysisSettings.OutputDirectory, "report.md")
            };
        }

        public static UslsExportResult ExportActiveScene(UslsExportSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var exportSettings = settings.Clone();
            NormalizeSettings(exportSettings);
            exportSettings.AnalyzeOnly = false;

            Directory.CreateDirectory(exportSettings.OutputDirectory);
            Directory.CreateDirectory(Path.Combine(exportSettings.OutputDirectory, "assets"));
            Directory.CreateDirectory(Path.Combine(exportSettings.OutputDirectory, "assets", "meshes"));
            Directory.CreateDirectory(Path.Combine(exportSettings.OutputDirectory, "assets", "textures"));

            var manifest = BuildManifest(exportSettings);

            var manifestPath = Path.Combine(exportSettings.OutputDirectory, "scene.usls.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));

            var reportPath = Path.Combine(exportSettings.OutputDirectory, "report.md");
            File.WriteAllText(reportPath, UslsReportWriter.CreateReport(manifest));

            AssetDatabase.Refresh();

            return new UslsExportResult
            {
                Success = true,
                Manifest = manifest,
                OutputDirectory = exportSettings.OutputDirectory,
                ManifestPath = manifestPath,
                ReportPath = reportPath
            };
        }

        private static UslsManifest BuildManifest(UslsExportSettings settings)
        {
            var scene = SceneManager.GetActiveScene();
            var manifest = CreateManifest(scene, settings);
            var warnings = new UslsWarningSink(manifest);
            var assets = new UslsAssetExporter(settings, manifest, warnings);
            var objectIds = new Dictionary<GameObject, string>();

            var roots = GetExportRoots(scene, settings);
            if (roots.Count == 0)
            {
                warnings.Add(
                    "warning",
                    "NO_EXPORT_ROOTS",
                    null,
                    "/",
                    "No export roots were found for the current scope.",
                    "Select at least one GameObject or disable selected-roots-only export.");
            }

            WarnAboutSelectedRootParentTransforms(roots, settings, warnings);
            foreach (var root in roots)
            {
                RegisterSelectedAncestorIds(root, settings, objectIds);
                RegisterObjectIds(root, settings, objectIds);
            }

            var exportedAnchors = new HashSet<GameObject>();
            foreach (var root in roots)
            {
                var parentId = settings.ExportSelectedRootsOnly
                    ? ExportSelectedAncestorAnchors(root.transform.parent, null, settings, manifest, objectIds, exportedAnchors)
                    : null;
                ExportHierarchy(root, parentId, settings, manifest, warnings, assets, objectIds);
            }

            FinalizeStats(manifest, assets);
            return manifest;
        }

        private static void NormalizeSettings(UslsExportSettings settings)
        {
            if (string.IsNullOrEmpty(settings.OutputDirectory))
            {
                settings.OutputDirectory = UslsFileUtility.DefaultOutputDirectory;
            }
        }

        private static UslsManifest CreateManifest(Scene scene, UslsExportSettings settings)
        {
            var manifest = new UslsManifest();
            manifest.exporter.unityVersion = Application.unityVersion;
            manifest.exporter.exportedAtUtc = DateTime.UtcNow.ToString("o");
            manifest.exporter.sceneName = scene.name;
            manifest.exporter.scenePath = scene.path;

            manifest.exportOptions.exportInactiveObjects = settings.ExportInactiveObjects;
            manifest.exportOptions.includeEditorOnlyObjects = settings.IncludeEditorOnlyObjects;
            manifest.exportOptions.includeMeshMetadata = settings.IncludeMeshMetadata;
            manifest.exportOptions.includeMaterialMetadata = settings.IncludeMaterialMetadata;
            manifest.exportOptions.includeColliders = settings.IncludeColliders;
            manifest.exportOptions.includeLights = settings.IncludeLights;
            manifest.exportOptions.includeCameraHints = settings.IncludeCameraHints;
            manifest.exportOptions.includePlayerSpawnMarkers = settings.IncludePlayerSpawnMarkers;
            manifest.exportOptions.includeUnityUiObjects = settings.IncludeUnityUiObjects;
            manifest.exportOptions.convertMetersToCentimeters = settings.ConvertMetersToCentimeters;
            manifest.exportOptions.convertUnityToLensHandedness = settings.ConvertUnityToLensHandedness;
            manifest.exportOptions.copySupportedSourceAssets = settings.CopySupportedSourceAssets;
            manifest.coordinateSystem.positionScale = settings.ConvertMetersToCentimeters ? 100f : 1f;

            return manifest;
        }

        private static List<GameObject> GetExportRoots(Scene scene, UslsExportSettings settings)
        {
            if (!settings.ExportSelectedRootsOnly)
            {
                return new List<GameObject>(scene.GetRootGameObjects());
            }

            var selected = new HashSet<GameObject>();
            foreach (var item in settings.SelectedRoots)
            {
                if (item != null && item.scene == scene)
                {
                    selected.Add(item);
                }
            }

            var roots = new List<GameObject>();
            foreach (var item in selected)
            {
                var parent = item.transform.parent;
                var hasSelectedParent = false;
                while (parent != null)
                {
                    if (selected.Contains(parent.gameObject))
                    {
                        hasSelectedParent = true;
                        break;
                    }

                    parent = parent.parent;
                }

                if (!hasSelectedParent)
                {
                    roots.Add(item);
                }
            }

            roots.Sort((a, b) => string.CompareOrdinal(GetHierarchyPath(a), GetHierarchyPath(b)));
            return roots;
        }

        private static void RegisterObjectIds(GameObject gameObject, UslsExportSettings settings, Dictionary<GameObject, string> objectIds)
        {
            if (!ShouldExport(gameObject, settings))
            {
                return;
            }

            objectIds[gameObject] = CreateObjectId(gameObject);
            for (var i = 0; i < gameObject.transform.childCount; i++)
            {
                RegisterObjectIds(gameObject.transform.GetChild(i).gameObject, settings, objectIds);
            }
        }

        private static void WarnAboutSelectedRootParentTransforms(
            List<GameObject> roots,
            UslsExportSettings settings,
            UslsWarningSink warnings)
        {
            if (!settings.ExportSelectedRootsOnly || roots == null)
            {
                return;
            }

            for (var i = 0; i < roots.Count; i++)
            {
                var root = roots[i];
                if (root == null || root.transform.parent == null)
                {
                    continue;
                }

                warnings.Add(
                    "info",
                    "SELECTED_ROOT_PARENT_TRANSFORM_ANCHOR",
                    null,
                    GetHierarchyPath(root),
                    "Selected export root has a Unity parent. The exporter will include parent transform anchors so Lens Studio can reconstruct the selected root's world placement.",
                    "This preserves placement without exporting the parent's unrelated siblings. Export the full scene if you need the parent's components too.");
            }
        }

        private static void RegisterSelectedAncestorIds(GameObject root, UslsExportSettings settings, Dictionary<GameObject, string> objectIds)
        {
            if (!settings.ExportSelectedRootsOnly || root == null)
            {
                return;
            }

            var parent = root.transform.parent;
            while (parent != null)
            {
                if (!objectIds.ContainsKey(parent.gameObject))
                {
                    objectIds[parent.gameObject] = CreateObjectId(parent.gameObject);
                }

                parent = parent.parent;
            }
        }

        private static string ExportSelectedAncestorAnchors(
            Transform ancestor,
            string parentId,
            UslsExportSettings settings,
            UslsManifest manifest,
            Dictionary<GameObject, string> objectIds,
            HashSet<GameObject> exportedAnchors)
        {
            if (ancestor == null)
            {
                return parentId;
            }

            var ancestorParentId = ExportSelectedAncestorAnchors(
                ancestor.parent,
                parentId,
                settings,
                manifest,
                objectIds,
                exportedAnchors);

            var gameObject = ancestor.gameObject;
            string objectId;
            if (!objectIds.TryGetValue(gameObject, out objectId))
            {
                objectId = CreateObjectId(gameObject);
                objectIds[gameObject] = objectId;
            }

            if (!exportedAnchors.Contains(gameObject))
            {
                ExportSelectedAncestorAnchor(gameObject, ancestorParentId, objectId, settings, manifest);
                exportedAnchors.Add(gameObject);
            }

            return objectId;
        }

        private static void ExportSelectedAncestorAnchor(
            GameObject gameObject,
            string parentId,
            string objectId,
            UslsExportSettings settings,
            UslsManifest manifest)
        {
            var anchor = new UslsObject
            {
                id = objectId,
                parentId = parentId,
                sourceInstanceId = GetGlobalObjectId(gameObject),
                name = gameObject.name,
                path = GetHierarchyPath(gameObject),
                enabled = gameObject.activeSelf,
                type = UslsSchema.TypeEmpty,
                transform = UslsTransformConverter.FromLocalTransform(gameObject.transform, settings)
            };

            anchor.tags.Add("selected_root_parent_anchor");
            anchor.notes.Add("Auto-included as a transform-only parent so selected export roots keep their Unity world placement.");
            manifest.objects.Add(anchor);
        }

        private static void ExportHierarchy(
            GameObject gameObject,
            string parentId,
            UslsExportSettings settings,
            UslsManifest manifest,
            UslsWarningSink warnings,
            UslsAssetExporter assets,
            Dictionary<GameObject, string> objectIds)
        {
            if (!ShouldExport(gameObject, settings))
            {
                manifest.stats.skippedObjectCount++;
                return;
            }

            string objectId;
            if (!objectIds.TryGetValue(gameObject, out objectId))
            {
                objectId = CreateObjectId(gameObject);
            }

            var path = GetHierarchyPath(gameObject);
            var uslsObject = new UslsObject
            {
                id = objectId,
                parentId = parentId,
                sourceInstanceId = GetGlobalObjectId(gameObject),
                name = gameObject.name,
                path = path,
                enabled = gameObject.activeSelf,
                transform = UslsTransformConverter.FromLocalTransform(gameObject.transform, settings)
            };

            FillComponentList(gameObject, uslsObject, warnings);
            uslsObject.type = ClassifyObject(gameObject, settings);

            if (settings.IncludePlayerSpawnMarkers && IsPlayerSpawn(gameObject))
            {
                uslsObject.tags.Add("player_spawn_source");
                uslsObject.notes.Add("Lens importer should offset the imported scene root so this point aligns with Lens origin.");
                if (LooksLikeVrRigRoot(gameObject))
                {
                    uslsObject.tags.Add("vr_rig_root");
                    uslsObject.tags.Add("spectacles_origin_reference");
                    uslsObject.notes.Add("Detected as a VR/XR rig root. Parent/local transforms are preserved so Lens import can align the authored user origin.");
                }
            }

            if (settings.IncludeMeshMetadata)
            {
                FillMeshAndPrimitive(gameObject, uslsObject, settings, assets, warnings);
            }

            if (settings.IncludeMaterialMetadata)
            {
                FillMaterial(gameObject, uslsObject, assets, warnings);
            }

            if (settings.IncludeLights)
            {
                FillLight(gameObject, uslsObject, warnings);
            }

            if (settings.IncludeCameraHints)
            {
                FillCameraHint(gameObject, uslsObject);
            }

            if (settings.IncludeColliders)
            {
                FillCollider(gameObject, uslsObject, settings, assets, warnings);
            }

            WarnAboutTransforms(gameObject, objectId, path, warnings);
            manifest.objects.Add(uslsObject);

            for (var i = 0; i < gameObject.transform.childCount; i++)
            {
                ExportHierarchy(gameObject.transform.GetChild(i).gameObject, objectId, settings, manifest, warnings, assets, objectIds);
            }
        }

        private static bool ShouldExport(GameObject gameObject, UslsExportSettings settings)
        {
            if (!settings.ExportInactiveObjects && !gameObject.activeInHierarchy)
            {
                return false;
            }

            if (!settings.IncludeUnityUiObjects && IsUnityUiObject(gameObject))
            {
                return false;
            }

            return settings.IncludeEditorOnlyObjects || !string.Equals(gameObject.tag, "EditorOnly", StringComparison.Ordinal);
        }

        private static bool IsUnityUiObject(GameObject gameObject)
        {
            return gameObject.GetComponent<RectTransform>() != null ||
                   gameObject.GetComponent<CanvasRenderer>() != null;
        }

        private static string ClassifyObject(GameObject gameObject, UslsExportSettings settings)
        {
            if (settings.IncludePlayerSpawnMarkers && IsPlayerSpawn(gameObject))
            {
                return UslsSchema.TypePlayerSpawn;
            }

            if (settings.IncludeLights && gameObject.GetComponent<Light>() != null)
            {
                return UslsSchema.TypeLight;
            }

            if (settings.IncludeCameraHints && gameObject.GetComponent<Camera>() != null)
            {
                return UslsSchema.TypeCameraHint;
            }

            if (settings.IncludeMeshMetadata && TryGetPrimaryMesh(gameObject, out _, out var mesh) && mesh != null)
            {
                return DetectPrimitiveShape(gameObject, mesh) == null ? UslsSchema.TypeMesh : UslsSchema.TypePrimitive;
            }

            if (settings.IncludeColliders && gameObject.GetComponent<Collider>() != null)
            {
                return UslsSchema.TypeCollider;
            }

            return UslsSchema.TypeEmpty;
        }

        private static bool IsPlayerSpawn(GameObject gameObject)
        {
            if (string.Equals(gameObject.tag, "Player", StringComparison.Ordinal) ||
                gameObject.GetComponent<CharacterController>() != null ||
                LooksLikeVrRigRoot(gameObject))
            {
                return true;
            }

            if (ContainsInsensitive(gameObject.name, "PlayerSpawner") ||
                ContainsInsensitive(gameObject.name, "Player Spawn") ||
                ContainsInsensitive(gameObject.name, "SpawnPoint") ||
                ContainsInsensitive(gameObject.name, "Spawn_Point"))
            {
                return true;
            }

            var components = gameObject.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();
                var fullName = type.FullName ?? type.Name;
                if (ContainsInsensitive(fullName, "PlayerSpawner") ||
                    ContainsInsensitive(fullName, "PlayerSpawn") ||
                    ContainsInsensitive(fullName, "SpawnPoint") ||
                    IsVrRigTypeName(fullName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeVrRigRoot(GameObject gameObject)
        {
            var name = gameObject.name;
            if (ContainsInsensitive(name, "XROrigin") ||
                ContainsInsensitive(name, "XR Origin") ||
                ContainsInsensitive(name, "XR Rig") ||
                ContainsInsensitive(name, "VR Rig") ||
                ContainsInsensitive(name, "Player Rig") ||
                ContainsInsensitive(name, "OVRCameraRig"))
            {
                return true;
            }

            var components = gameObject.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();
                var fullName = type.FullName ?? type.Name;
                if (IsVrRigTypeName(fullName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsVrRigTypeName(string fullName)
        {
            return ContainsInsensitive(fullName, "Unity.XR.CoreUtils.XROrigin") ||
                   ContainsInsensitive(fullName, ".XROrigin") ||
                   ContainsInsensitive(fullName, "OVRCameraRig") ||
                   ContainsInsensitive(fullName, "VRCameraRig");
        }

        private static void FillComponentList(GameObject gameObject, UslsObject uslsObject, UslsWarningSink warnings)
        {
            var components = gameObject.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    uslsObject.sourceComponents.Add("Missing Script");
                    warnings.Add(
                        "warning",
                        "MISSING_SCRIPT",
                        uslsObject.id,
                        uslsObject.path,
                        "A missing Unity component reference was found.",
                        "Remove the missing script or recreate the behavior manually in Lens Studio.");
                    continue;
                }

                var type = component.GetType();
                uslsObject.sourceComponents.Add(type.Name);

                if (LooksLikePostProcessing(type))
                {
                    warnings.Add(
                        "warning",
                        "POST_PROCESSING_SKIPPED",
                        uslsObject.id,
                        uslsObject.path,
                        "Post-processing component '" + type.Name + "' was not converted.",
                        "Recreate the look with Lens Studio-supported effects.");
                }
                else if (LooksLikeVfxComponent(type))
                {
                    warnings.Add(
                        "warning",
                        "VFX_COMPONENT_SKIPPED",
                        uslsObject.id,
                        uslsObject.path,
                        "VFX component '" + type.Name + "' was not converted.",
                        "Rebuild the effect with Lens Studio VFX, particles, or a baked mesh/texture sequence.");
                }
                else if (LooksLikeXrComponent(type))
                {
                    warnings.Add(
                        "warning",
                        "XR_COMPONENT_SKIPPED",
                        uslsObject.id,
                        uslsObject.path,
                        "XR component '" + type.Name + "' was not converted.",
                        "Use the exported player_spawn marker and rebuild interaction logic with Spectacles/Lens Studio APIs.");
                }
                else if (component is MonoBehaviour)
                {
                    warnings.Add(
                        "info",
                        "MONOBEHAVIOUR_SKIPPED",
                        uslsObject.id,
                        uslsObject.path,
                        "MonoBehaviour '" + type.FullName + "' was not converted.",
                        "Re-implement this behavior as a Lens Studio script if it is needed.");
                }
                else if (component is ParticleSystem)
                {
                    warnings.Add(
                        "warning",
                        "PARTICLE_SYSTEM_SKIPPED",
                        uslsObject.id,
                        uslsObject.path,
                        "Unity ParticleSystem was not converted.",
                        "Rebuild the effect with Lens Studio VFX or particles.");
                }
            }
        }

        private static void FillMeshAndPrimitive(
            GameObject gameObject,
            UslsObject uslsObject,
            UslsExportSettings settings,
            UslsAssetExporter assets,
            UslsWarningSink warnings)
        {
            if (!TryGetPrimaryMesh(gameObject, out var renderer, out var mesh) || mesh == null)
            {
                return;
            }

            var meshAsset = assets.GetOrCreateMeshAsset(mesh, uslsObject.id, uslsObject.path);
            var isSkinned = renderer is SkinnedMeshRenderer;
            uslsObject.mesh = new UslsMeshRef
            {
                assetId = meshAsset != null ? meshAsset.id : null,
                assetRef = meshAsset != null ? meshAsset.path : null,
                meshName = mesh.name,
                vertexCount = mesh.vertexCount,
                triangleCount = UslsAssetExporter.CountTriangles(mesh),
                subMeshCount = mesh.subMeshCount,
                skinned = isSkinned
            };

            var primitiveShape = DetectPrimitiveShape(gameObject, mesh);
            if (primitiveShape != null)
            {
                uslsObject.primitive = new UslsPrimitive
                {
                    shape = primitiveShape,
                    fallbackMeshAssetId = meshAsset != null ? meshAsset.id : null
                };
            }

            if (isSkinned)
            {
                warnings.Add(
                    "warning",
                    "SKINNED_MESH_LIMITED",
                    uslsObject.id,
                    uslsObject.path,
                    "Skinned mesh metadata was exported, but rig and animation behavior are not guaranteed.",
                    "Treat this as a static/asset transfer unless the later Lens importer explicitly supports skinned glTF/FBX import.");
            }
        }

        private static void FillMaterial(GameObject gameObject, UslsObject uslsObject, UslsAssetExporter assets, UslsWarningSink warnings)
        {
            var renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                return;
            }

            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null)
                {
                    continue;
                }

                var baseColor = GetColor(material, Color.white, "_BaseColor", "_Color");
                var emissionColor = GetColor(material, Color.black, "_EmissionColor");
                var baseTexture = GetTexture(material, "_BaseMap", "_MainTex");
                var normalTexture = GetTexture(material, "_BumpMap", "_NormalMap");
                var metallicTexture = GetTexture(material, "_MetallicGlossMap");
                var baseTextureAsset = assets.GetOrCreateTextureAsset(baseTexture, uslsObject.id, uslsObject.path);
                var normalTextureAsset = assets.GetOrCreateTextureAsset(normalTexture, uslsObject.id, uslsObject.path);
                var metallicTextureAsset = assets.GetOrCreateTextureAsset(metallicTexture, uslsObject.id, uslsObject.path);

                var materialRef = new UslsMaterialRef
                {
                    assetId = assets.GetOrCreateMaterialAssetId(material),
                    name = material.name,
                    shaderName = material.shader != null ? material.shader.name : string.Empty,
                    baseColor = ColorToArray(baseColor),
                    albedoTextureAssetId = baseTextureAsset != null ? baseTextureAsset.id : null,
                    normalTextureAssetId = normalTextureAsset != null ? normalTextureAsset.id : null,
                    metallicTextureAssetId = metallicTextureAsset != null ? metallicTextureAsset.id : null,
                    slot = i,
                    metallic = GetFloat(material, 0f, "_Metallic"),
                    roughness = 1f - GetFloat(material, 0.5f, "_Smoothness", "_Glossiness"),
                    transparent = baseColor.a < 0.999f || material.renderQueue >= 3000,
                    emission = emissionColor.maxColorComponent > 0.001f || material.IsKeywordEnabled("_EMISSION"),
                    emissionColor = ColorToArray(emissionColor)
                };

                uslsObject.materials.Add(materialRef);
                if (uslsObject.material == null)
                {
                    uslsObject.material = materialRef;
                }

                if (!LooksLikeSimpleLitShader(material))
                {
                    warnings.Add(
                        "warning",
                        "NON_STANDARD_SHADER_FALLBACK",
                        uslsObject.id,
                        uslsObject.path,
                        "Material slot " + i + " uses shader '" + materialRef.shaderName + "', which is not a guaranteed Lens Studio material match.",
                        "Bake the material to PBR textures or recreate the material in Lens Studio.");
                }
            }
        }

        private static void FillLight(GameObject gameObject, UslsObject uslsObject, UslsWarningSink warnings)
        {
            var light = gameObject.GetComponent<Light>();
            if (light == null)
            {
                return;
            }

            uslsObject.light = new UslsLight
            {
                lightType = light.type.ToString().ToLowerInvariant(),
                color = new[] { light.color.r, light.color.g, light.color.b },
                intensity = light.intensity,
                range = light.range,
                spotAngle = light.spotAngle,
                shadows = light.shadows != LightShadows.None
            };

            if (light.type == LightType.Rectangle)
            {
                warnings.Add(
                    "warning",
                    "AREA_LIGHT_APPROXIMATION",
                    uslsObject.id,
                    uslsObject.path,
                    "Unity Area Light does not have a direct first-pass Lens mapping.",
                    "Approximate with point/spot/directional lighting in Lens Studio.");
            }
        }

        private static void FillCameraHint(GameObject gameObject, UslsObject uslsObject)
        {
            var camera = gameObject.GetComponent<Camera>();
            if (camera == null)
            {
                return;
            }

            uslsObject.camera = new UslsCameraHint
            {
                fieldOfView = camera.fieldOfView,
                orthographic = camera.orthographic,
                orthographicSize = camera.orthographicSize,
                nearClip = camera.nearClipPlane,
                farClip = camera.farClipPlane
            };
        }

        private static void FillCollider(
            GameObject gameObject,
            UslsObject uslsObject,
            UslsExportSettings settings,
            UslsAssetExporter assets,
            UslsWarningSink warnings)
        {
            var colliders = gameObject.GetComponents<Collider>();
            if (colliders == null || colliders.Length == 0)
            {
                return;
            }

            if (colliders.Length > 1)
            {
                warnings.Add(
                    "warning",
                    "MULTI_COLLIDER_FIRST_ONLY",
                    uslsObject.id,
                    uslsObject.path,
                    "GameObject has multiple colliders; the manifest currently records the first collider.",
                    "Extend the schema/importer to support collider arrays before relying on multi-collider fidelity.");
            }

            var collider = colliders[0];
            var rigidbody = gameObject.GetComponent<Rigidbody>();
            var uslsCollider = new UslsCollider
            {
                trigger = collider.isTrigger,
                physicsMode = rigidbody == null ? "static" : rigidbody.isKinematic ? "kinematic" : "dynamic"
            };

            var box = collider as BoxCollider;
            var sphere = collider as SphereCollider;
            var capsule = collider as CapsuleCollider;
            var meshCollider = collider as MeshCollider;

            if (box != null)
            {
                uslsCollider.colliderType = "box";
                uslsCollider.center = UslsTransformConverter.Vector3ToScaledArray(box.center, settings);
                uslsCollider.size = UslsTransformConverter.SizeToScaledArray(box.size, settings);
            }
            else if (sphere != null)
            {
                uslsCollider.colliderType = "sphere";
                uslsCollider.center = UslsTransformConverter.Vector3ToScaledArray(sphere.center, settings);
                uslsCollider.radius = ScaleDistance(sphere.radius, settings);
            }
            else if (capsule != null)
            {
                uslsCollider.colliderType = "capsule";
                uslsCollider.center = UslsTransformConverter.Vector3ToScaledArray(capsule.center, settings);
                uslsCollider.radius = ScaleDistance(capsule.radius, settings);
                uslsCollider.height = ScaleDistance(capsule.height, settings);
                uslsCollider.direction = capsule.direction;
            }
            else if (meshCollider != null)
            {
                uslsCollider.colliderType = "mesh";
                uslsCollider.convex = meshCollider.convex;
                var meshAsset = assets.GetOrCreateMeshAsset(meshCollider.sharedMesh, uslsObject.id, uslsObject.path);
                uslsCollider.meshAssetId = meshAsset != null ? meshAsset.id : null;
                warnings.Add(
                    "warning",
                    "MESH_COLLIDER_APPROXIMATION",
                    uslsObject.id,
                    uslsObject.path,
                    "Mesh collider was exported as metadata and may need simplification for Lens Studio physics.",
                    "Prefer box/sphere/capsule colliders for the first importer version.");
            }
            else
            {
                uslsCollider.colliderType = collider.GetType().Name;
                warnings.Add(
                    "warning",
                    "UNKNOWN_COLLIDER_TYPE",
                    uslsObject.id,
                    uslsObject.path,
                    "Collider type '" + collider.GetType().Name + "' has no explicit mapping.",
                    "Add a mapper for this collider type or replace it with a simpler collider.");
            }

            uslsObject.collider = uslsCollider;
        }

        private static void WarnAboutTransforms(GameObject gameObject, string objectId, string objectPath, UslsWarningSink warnings)
        {
            var scale = gameObject.transform.localScale;
            if (scale.x < 0f || scale.y < 0f || scale.z < 0f)
            {
                warnings.Add(
                    "warning",
                    "NEGATIVE_SCALE",
                    objectId,
                    objectPath,
                    "Object has negative local scale.",
                    "Validate handedness, normals, and collider behavior in Lens Studio.");
            }

            if (!Approximately(scale.x, scale.y) || !Approximately(scale.y, scale.z))
            {
                warnings.Add(
                    "info",
                    "NON_UNIFORM_SCALE",
                    objectId,
                    objectPath,
                    "Object has non-uniform local scale.",
                    "Check mesh and collider dimensions after Lens import.");
            }
        }

        private static bool TryGetPrimaryMesh(GameObject gameObject, out Renderer renderer, out Mesh mesh)
        {
            var skinned = gameObject.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null)
            {
                renderer = skinned;
                mesh = skinned.sharedMesh;
                return mesh != null;
            }

            var meshFilter = gameObject.GetComponent<MeshFilter>();
            var meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshFilter != null && meshRenderer != null)
            {
                renderer = meshRenderer;
                mesh = meshFilter.sharedMesh;
                return mesh != null;
            }

            renderer = null;
            mesh = null;
            return false;
        }

        private static string DetectPrimitiveShape(GameObject gameObject, Mesh mesh)
        {
            if (mesh == null)
            {
                return null;
            }

            var assetPath = AssetDatabase.GetAssetPath(mesh);
            var meshName = mesh.name.ToLowerInvariant();

            if (!string.IsNullOrEmpty(assetPath) && !assetPath.Contains("unity default resources"))
            {
                return null;
            }

            if (meshName.Contains("cube"))
            {
                return "box";
            }

            if (meshName.Contains("sphere"))
            {
                return "sphere";
            }

            if (meshName.Contains("cylinder"))
            {
                return "cylinder";
            }

            if (meshName.Contains("capsule"))
            {
                return "capsule";
            }

            if (meshName.Contains("plane") || meshName.Contains("quad"))
            {
                return "plane";
            }

            return null;
        }

        private static Texture GetTexture(Material material, params string[] propertyNames)
        {
            for (var i = 0; i < propertyNames.Length; i++)
            {
                var propertyName = propertyNames[i];
                if (material.HasProperty(propertyName))
                {
                    return material.GetTexture(propertyName);
                }
            }

            return null;
        }

        private static Color GetColor(Material material, Color fallback, params string[] propertyNames)
        {
            for (var i = 0; i < propertyNames.Length; i++)
            {
                var propertyName = propertyNames[i];
                if (material.HasProperty(propertyName))
                {
                    return material.GetColor(propertyName);
                }
            }

            return fallback;
        }

        private static float GetFloat(Material material, float fallback, params string[] propertyNames)
        {
            for (var i = 0; i < propertyNames.Length; i++)
            {
                var propertyName = propertyNames[i];
                if (material.HasProperty(propertyName))
                {
                    return material.GetFloat(propertyName);
                }
            }

            return fallback;
        }

        private static float[] ColorToArray(Color color)
        {
            return new[] { color.r, color.g, color.b, color.a };
        }

        private static float ScaleDistance(float value, UslsExportSettings settings)
        {
            return settings.ConvertMetersToCentimeters ? value * 100f : value;
        }

        private static bool LooksLikeSimpleLitShader(Material material)
        {
            if (material == null || material.shader == null)
            {
                return true;
            }

            var shaderName = material.shader.name;
            return shaderName == "Standard" ||
                   shaderName == "Universal Render Pipeline/Lit" ||
                   shaderName == "Universal Render Pipeline/Simple Lit" ||
                   shaderName == "Unlit/Color" ||
                   shaderName == "Unlit/Texture" ||
                   shaderName == "Universal Render Pipeline/Unlit";
        }

        private static bool LooksLikePostProcessing(Type type)
        {
            var fullName = type.FullName ?? type.Name;
            return fullName.IndexOf("PostProcess", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fullName.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   fullName.IndexOf("Rendering", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeVfxComponent(Type type)
        {
            var fullName = type.FullName ?? type.Name;
            return fullName.IndexOf("UnityEngine.VFX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fullName.IndexOf("VisualEffect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fullName.IndexOf("VFX", StringComparison.Ordinal) >= 0;
        }

        private static bool LooksLikeXrComponent(Type type)
        {
            var fullName = type.FullName ?? type.Name;
            var name = type.Name;
            return fullName.IndexOf("UnityEngine.XR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fullName.IndexOf(".XR.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fullName.IndexOf(".XR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.StartsWith("XR", StringComparison.Ordinal) ||
                   name.EndsWith("XR", StringComparison.Ordinal) ||
                   fullName.IndexOf("Teleport", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsInsensitive(string value, string match)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool Approximately(float a, float b)
        {
            return Mathf.Abs(a - b) <= 0.0001f;
        }

        private static string CreateObjectId(GameObject gameObject)
        {
            return UslsFileUtility.StableId("obj", GetGlobalObjectId(gameObject));
        }

        private static string GetGlobalObjectId(GameObject gameObject)
        {
            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString();
            if (!string.IsNullOrEmpty(globalId))
            {
                return globalId + ":" + gameObject.scene.path + ":" + GetHierarchyPath(gameObject);
            }

            return gameObject.scene.path + ":" + GetHierarchyPath(gameObject) + ":" + gameObject.GetInstanceID();
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            var names = new Stack<string>();
            var current = gameObject.transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return "/" + string.Join("/", names.ToArray());
        }

        private static void FinalizeStats(UslsManifest manifest, UslsAssetExporter assets)
        {
            manifest.stats.objectCount = manifest.objects.Count;
            manifest.stats.materialCount = assets.MaterialAssetCount;
            manifest.stats.textureCount = assets.TextureAssetCount;
            manifest.stats.noticeCount = 0;
            manifest.stats.warningCount = 0;
            manifest.stats.errorCount = 0;

            for (var i = 0; i < manifest.warnings.Count; i++)
            {
                var severity = manifest.warnings[i].severity;
                if (string.Equals(severity, "info", StringComparison.OrdinalIgnoreCase))
                {
                    manifest.stats.noticeCount++;
                }
                else if (string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase))
                {
                    manifest.stats.errorCount++;
                }
                else
                {
                    manifest.stats.warningCount++;
                }
            }

            var totalTriangles = 0;
            for (var i = 0; i < manifest.objects.Count; i++)
            {
                var item = manifest.objects[i];
                if (item.type == UslsSchema.TypeMesh)
                {
                    manifest.stats.meshObjectCount++;
                }
                else if (item.type == UslsSchema.TypePrimitive)
                {
                    manifest.stats.primitiveObjectCount++;
                }
                else if (item.type == UslsSchema.TypeLight)
                {
                    manifest.stats.lightCount++;
                }
                else if (item.type == UslsSchema.TypeCameraHint)
                {
                    manifest.stats.cameraHintCount++;
                }
                else if (item.type == UslsSchema.TypePlayerSpawn)
                {
                    manifest.stats.playerSpawnCount++;
                }

                if (item.collider != null)
                {
                    manifest.stats.colliderCount++;
                }

                if (item.mesh != null)
                {
                    totalTriangles += item.mesh.triangleCount;
                }
            }

            manifest.stats.totalTriangles = totalTriangles;
        }
    }
}
